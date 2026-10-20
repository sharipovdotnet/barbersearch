# Deployment smoke for the container/proxy slice.
#
# Creates an isolated *_smoke database, applies the migrations, starts several API
# instances on localhost and checks the behaviour that only a running host can show:
#
#   * cookie login, refresh rotation, logout, change password;
#   * Data Protection key ring survives an API restart and is shared by a second instance;
#   * X-Forwarded-For drives the auth rate limit, and an untrusted proxy is ignored;
#   * HTTPS redirect, HSTS and CORS preflight with credentials behind a trusted proxy;
#   * /health/live and /health/ready (including an unreachable database);
#   * booking creation through the cookie session;
#   * fail-fast startup for missing/invalid deploy settings.
#
# It is a local verification tool, not part of the API. It needs the locally installed
# PostgreSQL 16, psql.exe and dotnet-ef, like load/run.ps1. Run with danger-full-access:
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File deploy/smoke.ps1
#
# Evidence is written to deploy/generated/smoke.txt (git-ignored).

param(
    [string]$Database = 'barbersearch_deploy_smoke',
    [int]$DevPort = 5243,
    [int]$SecondPort = 5245,
    [int]$ProdPort = 5244,
    [int]$BadDbPort = 5246,
    [int]$FailFastPort = 5291,
    [switch]$SkipBuild,
    [switch]$SkipDatabaseSetup
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

if ($Database -notmatch '_smoke$') {
    throw "Refusing to recreate '$Database': the name must end with '_smoke'."
}

$root = Split-Path $PSScriptRoot -Parent
$generated = Join-Path $PSScriptRoot 'generated'
$keysPath = Join-Path $generated 'keys'
$webProject = Join-Path $root 'BarberSearch/src/BarberSearch.Web'
$solution = Join-Path $root 'BarberSearch/BarberSearch.sln'
$psql = 'C:\Program Files\PostgreSQL\16\bin\psql.exe'
$connection = "Host=127.0.0.1;Port=5432;Database=$Database;Username=postgres;Password=postgres;Timeout=5"
$webDll = Join-Path $webProject 'bin/Debug/net10.0/BarberSearch.Web.dll'
$barberId = '44444444-4444-4444-4444-444444444444'
$barberServiceId = '77777777-7777-7777-7777-777777777777'
$salonId = '11111111-1111-1111-1111-111111111111'
$newPassword = 'Smoke!NewPassw0rd'

New-Item -ItemType Directory -Force -Path $generated | Out-Null
$env:PGPASSWORD = 'postgres'

$script:evidence = @()
$script:checks = 0
$script:processes = @()

$managedEnvironmentKeys = @(
    'ASPNETCORE_ENVIRONMENT',
    'ASPNETCORE_URLS',
    'ASPNETCORE_HTTPS_PORT',
    'ConnectionStrings__Postgres',
    'Auth__SigningKey',
    'Auth__CookieSameSite',
    'DataProtection__KeysPath',
    'ForwardedHeaders__Enabled',
    'ForwardedHeaders__KnownProxies__0',
    'ForwardedHeaders__KnownProxies__1',
    'ForwardedHeaders__KnownNetworks__0',
    'Cors__AllowedOrigins__0',
    'Cors__AllowedOrigins__1',
    'HealthChecks__DatabaseTimeoutSeconds',
    'Deployment__ShutdownTimeoutSeconds',
    'Logging__EventLog__LogLevel__Default',
    'Logging__LogLevel__Microsoft.EntityFrameworkCore'
)

function Clear-ManagedEnvironment {
    foreach ($key in $managedEnvironmentKeys) {
        Remove-Item -Path "Env:$key" -ErrorAction SilentlyContinue
    }
}

function Record {
    param([string]$Message)

    $script:checks++
    $line = "PASS $Message"
    Write-Host $line
    $script:evidence += $line
}

function Assert-True {
    param([bool]$Condition, [string]$Name)

    if (-not $Condition) {
        throw "assertion failed: $Name"
    }

    Record $Name
}

function Assert-Equal {
    param($Actual, $Expected, [string]$Name)

    if ("$Actual" -ne "$Expected") {
        throw "assertion failed: $Name (expected '$Expected', got '$Actual')"
    }

    Record "$Name -> $Actual"
}

function Assert-Contains {
    param([string]$Text, [string]$Needle, [string]$Name)

    if (-not $Text.Contains($Needle)) {
        throw "assertion failed: $Name (expected to contain '$Needle'); response: $Text"
    }

    Record $Name
}

function Assert-Status {
    param($Response, [int]$Expected, [string]$Name)

    if ($Response.Status -ne $Expected) {
        throw "assertion failed: $Name (expected HTTP $Expected, got $($Response.Status)); body: $($Response.Body)"
    }

    Record "$Name -> HTTP $Expected"
}

function New-HttpClient {
    param([bool]$UseCookies)

    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.UseCookies = $UseCookies
    $handler.AllowAutoRedirect = $false

    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(30)

    return [pscustomobject]@{
        Client = $client
        Handler = $handler
    }
}

function Send-Request {
    param(
        $Client,
        [string]$Method,
        [string]$Path,
        [int]$Port,
        [string]$Body = $null,
        [hashtable]$Headers = @{},
        [string]$HostHeader = $null
    )

    $request = New-Object System.Net.Http.HttpRequestMessage(
        (New-Object System.Net.Http.HttpMethod($Method)),
        ([Uri]("http://127.0.0.1:$Port$Path")))

    if (-not [string]::IsNullOrEmpty($Body)) {
        $request.Content = New-Object System.Net.Http.StringContent(
            $Body,
            [System.Text.Encoding]::UTF8,
            'application/json')
    }

    foreach ($key in $Headers.Keys) {
        [void]$request.Headers.TryAddWithoutValidation($key, $Headers[$key])
    }

    if (-not [string]::IsNullOrWhiteSpace($HostHeader)) {
        $request.Headers.Host = $HostHeader
    }

    $response = $Client.SendAsync($request).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

    $setCookies = @()
    try {
        $setCookies = @($response.Headers.GetValues('Set-Cookie'))
    }
    catch {
        $setCookies = @()
    }

    $location = $null
    if ($null -ne $response.Headers.Location) {
        $location = $response.Headers.Location.ToString()
    }

    $headerMap = @{}
    foreach ($header in $response.Headers) {
        $headerMap[$header.Key] = [string]::Join(', ', $header.Value)
    }

    $result = [pscustomobject]@{
        Status = [int]$response.StatusCode
        Body = $text
        SetCookies = $setCookies
        Location = $location
        Headers = $headerMap
    }

    $response.Dispose()
    $request.Dispose()

    return $result
}

function Get-CookieValue {
    param($Handler, [int]$Port, [string]$Name)

    $cookies = $Handler.CookieContainer.GetCookies([Uri]("http://127.0.0.1:$Port/"))
    $cookie = $cookies | Where-Object { $_.Name -eq $Name } | Select-Object -First 1

    return $cookie.Value
}

function Wait-Api {
    param(
        [int]$Port,
        [string]$HostHeader = $null,
        [bool]$UseForwardedHeaders = $false
    )

    $context = New-HttpClient -UseCookies $false
    $headers = @{}

    if ($UseForwardedHeaders) {
        $headers['X-Forwarded-Proto'] = 'https'
        $headers['X-Forwarded-For'] = '127.0.0.1'
    }

    $deadline = (Get-Date).AddSeconds(45)
    $lastError = $null

    try {
        while ((Get-Date) -lt $deadline) {
            try {
                $response = Send-Request -Client $context.Client -Method 'GET' -Path '/health/live' `
                    -Port $Port -Headers $headers -HostHeader $HostHeader

                if ($response.Status -eq 200) {
                    return
                }

                $lastError = "HTTP $($response.Status): $($response.Body)"
            }
            catch {
                $lastError = $_.Exception.Message
                Start-Sleep -Milliseconds 300
            }
        }

        throw "the API on port $Port did not become ready within 45 seconds (last error: $lastError)"
    }
    finally {
        $context.Client.Dispose()
    }
}

function Start-Api {
    param(
        [string]$Name,
        [int]$Port,
        [hashtable]$Environment
    )

    Clear-ManagedEnvironment

    $environmentMap = @{}
    foreach ($key in $Environment.Keys) {
        $environmentMap[$key] = $Environment[$key]
    }

    $environmentMap['ASPNETCORE_URLS'] = "http://127.0.0.1:$Port"

    foreach ($key in $environmentMap.Keys) {
        Set-Item -Path "Env:$key" -Value $environmentMap[$key]
    }

    $stdOut = Join-Path $generated "$Name.out.log"
    $stdErr = Join-Path $generated "$Name.err.log"

    $process = Start-Process -FilePath 'dotnet' `
        -ArgumentList 'bin/Debug/net10.0/BarberSearch.Web.dll' `
        -WorkingDirectory $webProject `
        -PassThru `
        -RedirectStandardOutput $stdOut `
        -RedirectStandardError $stdErr

    $api = [pscustomobject]@{
        Name = $Name
        Process = $process
        Port = $Port
        StdOut = $stdOut
        StdErr = $stdErr
    }

    $script:processes += $api
    return $api
}

function Stop-Api {
    param($Api)

    if ($null -eq $Api) {
        return
    }

    $process = $Api.Process

    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        $process.WaitForExit(5000) | Out-Null
    }
}

function New-BaseEnvironment {
    param([string]$EnvironmentName)

    return @{
        'ASPNETCORE_ENVIRONMENT' = $EnvironmentName
        'ConnectionStrings__Postgres' = $connection
        'Auth__SigningKey' = $script:signingKey
        'DataProtection__KeysPath' = $keysPath
        'ForwardedHeaders__Enabled' = 'true'
        'ForwardedHeaders__KnownProxies__0' = '127.0.0.1'
        'ForwardedHeaders__KnownProxies__1' = '::1'
        'HealthChecks__DatabaseTimeoutSeconds' = '2'
        'Deployment__ShutdownTimeoutSeconds' = '30'
        'Logging__EventLog__LogLevel__Default' = 'None'
        'Logging__LogLevel__Microsoft.EntityFrameworkCore' = 'Warning'
    }
}

function Assert-StartupFails {
    param(
        [string]$Name,
        [hashtable]$Environment,
        [string]$Expected
    )

    Clear-ManagedEnvironment

    foreach ($key in $Environment.Keys) {
        Set-Item -Path "Env:$key" -Value $Environment[$key]
    }

    $stdOut = Join-Path $generated "fail-fast-$Name.out.log"
    $stdErr = Join-Path $generated "fail-fast-$Name.err.log"

    $process = Start-Process -FilePath 'dotnet' `
        -ArgumentList 'bin/Debug/net10.0/BarberSearch.Web.dll' `
        -WorkingDirectory $webProject `
        -PassThru `
        -RedirectStandardOutput $stdOut `
        -RedirectStandardError $stdErr

    $exited = $process.WaitForExit(15000)

    if (-not $exited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "fail-fast '$Name': the process started instead of failing on the invalid configuration"
    }

    Start-Sleep -Milliseconds 200

    $output = ''
    if (Test-Path $stdOut) {
        $output += Get-Content $stdOut -Raw -ErrorAction SilentlyContinue
    }
    if (Test-Path $stdErr) {
        $output += Get-Content $stdErr -Raw -ErrorAction SilentlyContinue
    }

    if (-not $output.Contains($Expected)) {
        throw "fail-fast '$Name': expected '$Expected' in the startup output; got: $output"
    }

    Record "fail-fast $Name -> startup rejected"
}

function Test-InvalidLogin {
    param(
        $Client,
        [int]$Port,
        [string]$ForwardedFor
    )

    $body = @{ email = 'nobody@example.test'; password = 'wrong-password' } | ConvertTo-Json -Compress
    $headers = @{ 'X-Forwarded-For' = $ForwardedFor }

    return Send-Request -Client $Client -Method 'POST' -Path '/api/auth/login' `
        -Port $Port -Body $body -Headers $headers
}

$exitCode = 0

try {
    if (-not $SkipBuild) {
        Write-Host 'building the solution...'
        & dotnet build $solution --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw 'solution build failed'
        }
    }

    if (-not (Test-Path $webDll)) {
        throw "the API assembly is missing: $webDll"
    }

    if (-not $SkipDatabaseSetup) {
        Write-Host "recreating isolated smoke database $Database..."
        & $psql -h 127.0.0.1 -p 5432 -U postgres -d postgres -q -v ON_ERROR_STOP=1 `
            -c "DROP DATABASE IF EXISTS $Database WITH (FORCE);"
        & $psql -h 127.0.0.1 -p 5432 -U postgres -d postgres -q -v ON_ERROR_STOP=1 `
            -c "CREATE DATABASE $Database;"

        $env:ConnectionStrings__Postgres = $connection

        Write-Host 'applying migrations...'
        & dotnet ef database update `
            --project (Join-Path $root 'BarberSearch/src/BarberSearch.Infrastructure.Postgresql') `
            --startup-project $webProject `
            --no-build | Out-Host

        if ($LASTEXITCODE -ne 0) {
            throw 'dotnet ef database update failed'
        }

        & $psql -h 127.0.0.1 -p 5432 -U postgres -d $Database -q -v ON_ERROR_STOP=1 `
            -f (Join-Path $root 'load/seed/minimal.sql') | Out-Host

        if ($LASTEXITCODE -ne 0) {
            throw 'minimal seed failed'
        }

        Clear-ManagedEnvironment
    }

    if (Test-Path $keysPath) {
        Remove-Item $keysPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $keysPath | Out-Null

    $script:signingKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')

    $baseEnvironment = New-BaseEnvironment -EnvironmentName 'Development'

    # ---- 1. Cookie session, restart survival, refresh rotation ----------------------------
    Write-Host ''
    Write-Host '1. cookie session and Data Protection restart survival'

    $dev = Start-Api -Name 'dev' -Port $DevPort -Environment $baseEnvironment
    Wait-Api -Port $DevPort

    $cookieContext = New-HttpClient -UseCookies $true
    $cookieClient = $cookieContext.Client
    $cookieHandler = $cookieContext.Handler

    $csrf = Send-Request -Client $cookieClient -Method 'GET' -Path '/api/auth/csrf' -Port $DevPort
    Assert-Status $csrf 200 'csrf issued'
    $csrfToken = ($csrf.Body | ConvertFrom-Json).token
    Assert-True (-not [string]::IsNullOrWhiteSpace($csrfToken)) 'csrf token is not empty'

    $email = "deploy-smoke-$([Guid]::NewGuid().ToString('N').Substring(0, 8))@barbersearch.test"
    $registerBody = @{
        email = $email
        password = 'Smoke!Passw0rd'
        firstName = 'Deploy'
        lastName = 'Smoke'
    } | ConvertTo-Json -Compress

    $register = Send-Request -Client $cookieClient -Method 'POST' -Path '/api/auth/register' `
        -Port $DevPort -Body $registerBody
    Assert-Status $register 201 'register'

    $loginBody = @{
        email = $email
        password = 'Smoke!Passw0rd'
        rememberMe = $false
    } | ConvertTo-Json -Compress

    $login = Send-Request -Client $cookieClient -Method 'POST' -Path '/api/auth/login/cookie' `
        -Port $DevPort -Body $loginBody -Headers @{ 'X-CSRF-TOKEN' = $csrfToken }
    Assert-Status $login 204 'cookie login'

    $authSetCookie = $login.SetCookies | Where-Object { $_ -match '^barbersearch\.auth=' } | Select-Object -First 1
    $refreshSetCookie = $login.SetCookies | Where-Object { $_ -match '^barbersearch\.refresh=' } | Select-Object -First 1
    Assert-True ($null -ne $authSetCookie) 'auth cookie is set'
    Assert-True ($null -ne $refreshSetCookie) 'refresh cookie is set'
    Assert-Contains $authSetCookie.ToLowerInvariant() 'httponly' 'auth cookie is HttpOnly'
    Assert-Contains $authSetCookie.ToLowerInvariant() 'path=/' 'auth cookie path is /'
    Assert-Contains $authSetCookie.ToLowerInvariant() 'samesite=lax' 'same-site auth cookie uses SameSite=Lax'
    Assert-Contains $refreshSetCookie.ToLowerInvariant() 'httponly' 'refresh cookie is HttpOnly'
    Assert-Contains $refreshSetCookie.ToLowerInvariant() 'path=/' 'refresh cookie path is /'

    $oldRefresh = Get-CookieValue -Handler $cookieHandler -Port $DevPort -Name 'barbersearch.refresh'
    Assert-True (-not [string]::IsNullOrWhiteSpace($oldRefresh)) 'refresh cookie has a value'

    $me = Send-Request -Client $cookieClient -Method 'GET' -Path '/api/auth/me' -Port $DevPort
    Assert-Status $me 200 'me with cookie'
    Assert-Contains $me.Body $email 'me returns the logged-in user'

    Stop-Api $dev
    $dev = Start-Api -Name 'dev-restarted' -Port $DevPort -Environment $baseEnvironment
    Wait-Api -Port $DevPort

    $meAfterRestart = Send-Request -Client $cookieClient -Method 'GET' -Path '/api/auth/me' -Port $DevPort
    Assert-Status $meAfterRestart 200 'cookie survives API restart'

    $csrfAfterRestart = Send-Request -Client $cookieClient -Method 'GET' -Path '/api/auth/csrf' -Port $DevPort
    Assert-Status $csrfAfterRestart 200 'csrf after restart'
    $csrfTokenAfterRestart = ($csrfAfterRestart.Body | ConvertFrom-Json).token

    $refresh = Send-Request -Client $cookieClient -Method 'POST' -Path '/api/auth/refresh' `
        -Port $DevPort -Headers @{ 'X-CSRF-TOKEN' = $csrfTokenAfterRestart }
    Assert-Status $refresh 204 'refresh via cookie'

    $newRefresh = Get-CookieValue -Handler $cookieHandler -Port $DevPort -Name 'barbersearch.refresh'
    Assert-True ($newRefresh -ne $oldRefresh) 'refresh token rotates'

    $meAfterRefresh = Send-Request -Client $cookieClient -Method 'GET' -Path '/api/auth/me' -Port $DevPort
    Assert-Status $meAfterRefresh 200 'session still valid after refresh'

    $logout = Send-Request -Client $cookieClient -Method 'POST' -Path '/api/auth/logout' `
        -Port $DevPort -Headers @{ 'X-CSRF-TOKEN' = $csrfTokenAfterRestart }
    Assert-Status $logout 204 'logout'

    $meAfterLogout = Send-Request -Client $cookieClient -Method 'GET' -Path '/api/auth/me' -Port $DevPort
    Assert-Status $meAfterLogout 401 'me is 401 after logout'

    # ---- 2. Change password --------------------------------------------------------------
    Write-Host ''
    Write-Host '2. change password'

    $csrfAnonymous = Send-Request -Client $cookieClient -Method 'GET' -Path '/api/auth/csrf' -Port $DevPort
    $anonymousToken = ($csrfAnonymous.Body | ConvertFrom-Json).token

    $loginOld = Send-Request -Client $cookieClient -Method 'POST' -Path '/api/auth/login/cookie' `
        -Port $DevPort -Body $loginBody -Headers @{ 'X-CSRF-TOKEN' = $anonymousToken }
    Assert-Status $loginOld 204 'login before password change'

    $csrfAuthenticated = Send-Request -Client $cookieClient -Method 'GET' -Path '/api/auth/csrf' -Port $DevPort
    $authenticatedToken = ($csrfAuthenticated.Body | ConvertFrom-Json).token

    $changePasswordBody = @{
        currentPassword = 'Smoke!Passw0rd'
        newPassword = $newPassword
    } | ConvertTo-Json -Compress

    $changePassword = Send-Request -Client $cookieClient -Method 'POST' -Path '/api/auth/change-password' `
        -Port $DevPort -Body $changePasswordBody -Headers @{ 'X-CSRF-TOKEN' = $authenticatedToken }
    Assert-Status $changePassword 204 'change password'

    $meOldCookie = Send-Request -Client $cookieClient -Method 'GET' -Path '/api/auth/me' -Port $DevPort
    Assert-Status $meOldCookie 401 'old cookie is rejected after password change'

    $csrfNewPassword = Send-Request -Client $cookieClient -Method 'GET' -Path '/api/auth/csrf' -Port $DevPort
    $newPasswordToken = ($csrfNewPassword.Body | ConvertFrom-Json).token

    $loginBodyNew = @{
        email = $email
        password = $newPassword
        rememberMe = $false
    } | ConvertTo-Json -Compress

    $loginNew = Send-Request -Client $cookieClient -Method 'POST' -Path '/api/auth/login/cookie' `
        -Port $DevPort -Body $loginBodyNew -Headers @{ 'X-CSRF-TOKEN' = $newPasswordToken }
    Assert-Status $loginNew 204 'login with the new password'

    $meNew = Send-Request -Client $cookieClient -Method 'GET' -Path '/api/auth/me' -Port $DevPort
    Assert-Status $meNew 200 'session with the new password'

    # ---- 3. Booking creation through the cookie session -----------------------------------
    Write-Host ''
    Write-Host '3. booking creation'

    $slotStart = [DateTimeOffset]::UtcNow.Date.AddDays(30 + (Get-Random -Minimum 0 -Maximum 300)).AddHours(10)
    $slotEnd = $slotStart.AddMinutes(30)
    $idempotencyKey = "deploy-smoke-$([Guid]::NewGuid().ToString('N'))"

    $bookingBody = @{
        barberProfileId = $barberId
        barberServiceId = $barberServiceId
        salonId = $salonId
        startAt = $slotStart.ToString('yyyy-MM-ddTHH:mm:ssZ')
        endAt = $slotEnd.ToString('yyyy-MM-ddTHH:mm:ssZ')
        price = 0
        idempotencyKey = $idempotencyKey
        customerNote = 'deploy smoke'
    } | ConvertTo-Json -Compress

    $booking = Send-Request -Client $cookieClient -Method 'POST' -Path '/api/bookings' `
        -Port $DevPort -Body $bookingBody
    Assert-Status $booking 201 'booking created'
    $bookingId = ($booking.Body | ConvertFrom-Json).id
    Assert-True (-not [string]::IsNullOrWhiteSpace($bookingId)) 'booking id returned'

    $bookingRepeat = Send-Request -Client $cookieClient -Method 'POST' -Path '/api/bookings' `
        -Port $DevPort -Body $bookingBody
    Assert-Status $bookingRepeat 201 'same idempotency key is accepted'
    $bookingRepeatId = ($bookingRepeat.Body | ConvertFrom-Json).id
    Assert-Equal $bookingRepeatId $bookingId 'same idempotency key returns the same booking'

    $bookingRows = & $psql -h 127.0.0.1 -p 5432 -U postgres -d $Database -q -tA -v ON_ERROR_STOP=1 `
        -c "SELECT count(*) FROM bookings WHERE idempotency_key = '$idempotencyKey';"
    Assert-Equal ([int]($bookingRows | Select-Object -First 1)) 1 'one booking row per idempotency key'

    # ---- 4. Rate limit by X-Forwarded-For (trusted proxy) ---------------------------------
    Write-Host ''
    Write-Host '4. rate limit uses the forwarded client address'

    $rateContext = New-HttpClient -UseCookies $false
    $forwardedIp = '198.51.100.40'

    for ($attempt = 1; $attempt -le 10; $attempt++) {
        $response = Test-InvalidLogin -Client $rateContext.Client -Port $DevPort -ForwardedFor $forwardedIp
        Assert-Status $response 401 "invalid login $attempt under the rate limit"
    }

    $eleventh = Test-InvalidLogin -Client $rateContext.Client -Port $DevPort -ForwardedFor $forwardedIp
    Assert-Status $eleventh 429 'eleventh login from the same forwarded IP is throttled'

    $otherIp = Test-InvalidLogin -Client $rateContext.Client -Port $DevPort -ForwardedFor '198.51.100.41'
    Assert-Status $otherIp 401 'a different forwarded IP has its own bucket'
    $rateContext.Client.Dispose()

    # ---- 5. Refresh reuse through the bearer flow -----------------------------------------
    Write-Host ''
    Write-Host '5. refresh reuse detection'

    $bearerContext = New-HttpClient -UseCookies $false
    $bearerHeaders = @{ 'X-Forwarded-For' = '198.51.100.42' }

    $bearerLoginBody = @{
        email = $email
        password = $newPassword
    } | ConvertTo-Json -Compress

    $bearerLogin = Send-Request -Client $bearerContext.Client -Method 'POST' -Path '/api/auth/login' `
        -Port $DevPort -Body $bearerLoginBody -Headers $bearerHeaders
    Assert-Status $bearerLogin 200 'bearer login for reuse check'
    $bearerRefresh = ($bearerLogin.Body | ConvertFrom-Json).refreshToken

    $bearerRefreshBody = @{ refreshToken = $bearerRefresh } | ConvertTo-Json -Compress
    $bearerRotation = Send-Request -Client $bearerContext.Client -Method 'POST' -Path '/api/auth/refresh' `
        -Port $DevPort -Body $bearerRefreshBody -Headers $bearerHeaders
    Assert-Status $bearerRotation 200 'bearer refresh rotates'

    $reuse = Send-Request -Client $bearerContext.Client -Method 'POST' -Path '/api/auth/refresh' `
        -Port $DevPort -Body $bearerRefreshBody -Headers $bearerHeaders
    Assert-Status $reuse 401 'replayed refresh token is rejected'
    Assert-Contains $reuse.Body 'auth.refresh.reused' 'reuse error code returned'
    $bearerContext.Client.Dispose()

    # ---- 6. Second instance with the shared key ring --------------------------------------
    Write-Host ''
    Write-Host '6. second instance shares the Data Protection ring'

    $secondEnvironment = New-BaseEnvironment -EnvironmentName 'Development'
    # This instance is deliberately configured with a proxy address that is not the caller,
    # to prove that forwarded headers from an arbitrary client are ignored.
    $secondEnvironment['ForwardedHeaders__KnownProxies__0'] = '203.0.113.1'
    $secondEnvironment['ForwardedHeaders__KnownProxies__1'] = '203.0.113.2'

    $second = Start-Api -Name 'second' -Port $SecondPort -Environment $secondEnvironment
    Wait-Api -Port $SecondPort

    $meOnSecond = Send-Request -Client $cookieClient -Method 'GET' -Path '/api/auth/me' -Port $SecondPort
    Assert-Status $meOnSecond 200 'cookie from instance A is accepted by instance B'

    $untrustedContext = New-HttpClient -UseCookies $false
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        $response = Test-InvalidLogin -Client $untrustedContext.Client -Port $SecondPort -ForwardedFor '203.0.113.50'
        Assert-Status $response 401 "untrusted proxy attempt $attempt is not throttled by header"
    }

    $untrustedOther = Test-InvalidLogin -Client $untrustedContext.Client -Port $SecondPort -ForwardedFor '203.0.113.51'
    Assert-Status $untrustedOther 429 'untrusted X-Forwarded-For is ignored and shares the real IP bucket'
    $untrustedContext.Client.Dispose()

    Stop-Api $second

    # ---- 7. Production-like proxy: redirect, HSTS, CORS and cookie flags ------------------
    Write-Host ''
    Write-Host '7. proxy headers, HTTPS redirect, HSTS, CORS and cross-site cookie attributes'

    $productionEnvironment = New-BaseEnvironment -EnvironmentName 'Production'
    $productionEnvironment['ASPNETCORE_HTTPS_PORT'] = '5443'
    $productionEnvironment['Auth__CookieSameSite'] = 'None'
    $productionEnvironment['Cors__AllowedOrigins__0'] = 'https://spa.example.test'

    $production = Start-Api -Name 'production' -Port $ProdPort -Environment $productionEnvironment
    $proxyHost = 'api.barbersearch.test'
    Wait-Api -Port $ProdPort -HostHeader $proxyHost -UseForwardedHeaders $true

    $plainHttp = Send-Request -Client (New-HttpClient -UseCookies $false).Client -Method 'GET' `
        -Path '/health/live' -Port $ProdPort -HostHeader $proxyHost
    Assert-Status $plainHttp 307 'plain HTTP is redirected'
    Assert-Equal $plainHttp.Location "https://$proxyHost`:5443/health/live" 'redirect target is HTTPS'

    $secureContext = New-HttpClient -UseCookies $false
    $secureHeaders = @{ 'X-Forwarded-Proto' = 'https'; 'X-Forwarded-For' = '127.0.0.1' }
    $secureRequest = Send-Request -Client $secureContext.Client -Method 'GET' -Path '/health/live' `
        -Port $ProdPort -Headers $secureHeaders -HostHeader $proxyHost
    Assert-Status $secureRequest 200 'forwarded HTTPS is not redirected'
    $hsts = $secureRequest.Headers['Strict-Transport-Security']
    Assert-True ($null -ne $hsts -and $hsts.StartsWith('max-age=')) 'HSTS header is present'

    $preflight = Send-Request -Client $secureContext.Client -Method 'OPTIONS' -Path '/api/auth/login/cookie' `
        -Port $ProdPort -HostHeader $proxyHost -Headers @{
            'Origin' = 'https://spa.example.test'
            'Access-Control-Request-Method' = 'POST'
            'Access-Control-Request-Headers' = 'content-type,x-csrf-token'
            'X-Forwarded-Proto' = 'https'
            'X-Forwarded-For' = '127.0.0.1'
        }
    Assert-Status $preflight 204 'CORS preflight with credentials'
    Assert-Equal $preflight.Headers['Access-Control-Allow-Origin'] 'https://spa.example.test' 'preflight allows the explicit origin'
    Assert-Equal $preflight.Headers['Access-Control-Allow-Credentials'] 'true' 'preflight allows credentials'

    $proxyCsrf = Send-Request -Client $secureContext.Client -Method 'GET' -Path '/api/auth/csrf' `
        -Port $ProdPort -Headers $secureHeaders -HostHeader $proxyHost
    Assert-Status $proxyCsrf 200 'csrf behind the proxy'
    Assert-True ($proxyCsrf.SetCookies.Count -gt 0) 'antiforgery cookie is set behind the proxy'
    $proxyToken = ($proxyCsrf.Body | ConvertFrom-Json).token
    $antiforgeryCookie = ($proxyCsrf.SetCookies[0] -split ';')[0]
    Assert-Contains $proxyCsrf.SetCookies[0].ToLowerInvariant() 'secure' 'cross-site antiforgery cookie is Secure'
    Assert-Contains $proxyCsrf.SetCookies[0].ToLowerInvariant() 'samesite=none' 'cross-site antiforgery cookie is SameSite=None'

    $proxyLoginBody = @{
        email = $email
        password = $newPassword
        rememberMe = $false
    } | ConvertTo-Json -Compress

    $proxyLogin = Send-Request -Client $secureContext.Client -Method 'POST' -Path '/api/auth/login/cookie' `
        -Port $ProdPort -Body $proxyLoginBody -HostHeader $proxyHost -Headers @{
            'Cookie' = $antiforgeryCookie
            'X-CSRF-TOKEN' = $proxyToken
            'X-Forwarded-Proto' = 'https'
            'X-Forwarded-For' = '127.0.0.1'
        }
    Assert-Status $proxyLogin 204 'cookie login behind the proxy'

    $productionAuthCookie = $proxyLogin.SetCookies | Where-Object { $_ -match '^__Host-barbersearch=' } | Select-Object -First 1
    $productionRefreshCookie = $proxyLogin.SetCookies | Where-Object { $_ -match '^__Host-barbersearch-refresh=' } | Select-Object -First 1
    Assert-True ($null -ne $productionAuthCookie) 'production auth cookie uses the __Host- prefix'
    Assert-True ($null -ne $productionRefreshCookie) 'production refresh cookie uses the __Host- prefix'

    foreach ($cookie in @($productionAuthCookie, $productionRefreshCookie)) {
        $lower = $cookie.ToLowerInvariant()
        Assert-Contains $lower 'httponly' '__Host- cookie is HttpOnly'
        Assert-Contains $lower 'secure' '__Host- cookie is Secure'
        Assert-Contains $lower 'samesite=none' '__Host- cookie is SameSite=None'
        Assert-Contains $lower 'path=/' '__Host- cookie path is /'
        Assert-True (-not $lower.Contains('domain=')) '__Host- cookie has no Domain'
    }

    $proxyAuthPair = ($productionAuthCookie -split ';')[0]
    $proxyRefreshPair = ($productionRefreshCookie -split ';')[0]
    $proxyRefreshBefore = ($proxyRefreshPair -split '=', 2)[1]

    $proxyCsrfAuthenticated = Send-Request -Client $secureContext.Client -Method 'GET' -Path '/api/auth/csrf' `
        -Port $ProdPort -HostHeader $proxyHost -Headers @{
            'Cookie' = "$proxyAuthPair; $proxyRefreshPair"
            'X-Forwarded-Proto' = 'https'
            'X-Forwarded-For' = '127.0.0.1'
        }
    Assert-Status $proxyCsrfAuthenticated 200 'csrf for the authenticated proxy session'
    Assert-True ($proxyCsrfAuthenticated.SetCookies.Count -gt 0) 'authenticated antiforgery cookie is set'
    $proxyAuthenticatedToken = ($proxyCsrfAuthenticated.Body | ConvertFrom-Json).token
    $proxyAuthenticatedAntiforgery = ($proxyCsrfAuthenticated.SetCookies[0] -split ';')[0]

    $proxyRefresh = Send-Request -Client $secureContext.Client -Method 'POST' -Path '/api/auth/refresh' `
        -Port $ProdPort -HostHeader $proxyHost -Headers @{
            'Cookie' = "$proxyAuthPair; $proxyRefreshPair; $proxyAuthenticatedAntiforgery"
            'X-CSRF-TOKEN' = $proxyAuthenticatedToken
            'X-Forwarded-Proto' = 'https'
            'X-Forwarded-For' = '127.0.0.1'
        }
    Assert-Status $proxyRefresh 204 'refresh via the proxy'

    $rotatedProxyRefresh = $proxyRefresh.SetCookies | Where-Object { $_ -match '^__Host-barbersearch-refresh=' } | Select-Object -First 1
    Assert-True ($null -ne $rotatedProxyRefresh) 'refresh via proxy returns a new refresh cookie'
    $rotatedProxyPair = ($rotatedProxyRefresh -split ';')[0]
    Assert-True ((($rotatedProxyPair -split '=', 2)[1]) -ne $proxyRefreshBefore) 'refresh via proxy rotates the token'
    Assert-Contains $rotatedProxyRefresh.ToLowerInvariant() 'samesite=none' 'rotated refresh cookie keeps SameSite=None'

    $secureContext.Client.Dispose()
    Stop-Api $production

    # ---- 8. Health checks, including an unreachable database ------------------------------
    Write-Host ''
    Write-Host '8. health checks'

    $healthContext = New-HttpClient -UseCookies $false
    $live = Send-Request -Client $healthContext.Client -Method 'GET' -Path '/health/live' -Port $DevPort
    Assert-Status $live 200 'live health'
    Assert-Contains $live.Body '"status":"Healthy"' 'live health body'

    $ready = Send-Request -Client $healthContext.Client -Method 'GET' -Path '/health/ready' -Port $DevPort
    Assert-Status $ready 200 'ready health with reachable PostgreSQL'
    Assert-Contains $ready.Body 'postgres' 'ready health reports the database check'
    $healthContext.Client.Dispose()

    $deadDbEnvironment = New-BaseEnvironment -EnvironmentName 'Development'
    $deadDbEnvironment['ConnectionStrings__Postgres'] = 'Host=127.0.0.1;Port=59999;Database=missing;Username=postgres;Password=postgres;Timeout=1'

    $deadDb = Start-Api -Name 'dead-db' -Port $BadDbPort -Environment $deadDbEnvironment
    Wait-Api -Port $BadDbPort

    $deadContext = New-HttpClient -UseCookies $false
    $deadLive = Send-Request -Client $deadContext.Client -Method 'GET' -Path '/health/live' -Port $BadDbPort
    Assert-Status $deadLive 200 'live health does not depend on PostgreSQL'

    $deadReady = Send-Request -Client $deadContext.Client -Method 'GET' -Path '/health/ready' -Port $BadDbPort
    Assert-Status $deadReady 503 'ready health fails when PostgreSQL is unreachable'
    Assert-Contains $deadReady.Body '"status":"Unhealthy"' 'ready health reports Unhealthy'
    $deadContext.Client.Dispose()
    Stop-Api $deadDb

    # ---- 9. Fail-fast startup validation -------------------------------------------------
    Write-Host ''
    Write-Host '9. fail-fast startup validation'

    $failPort = $FailFastPort

    $missingKey = New-BaseEnvironment -EnvironmentName 'Development'
    $missingKey.Remove('Auth__SigningKey')
    $missingKey['ASPNETCORE_URLS'] = "http://127.0.0.1:$failPort"
    Assert-StartupFails -Name 'signing-key-missing' -Environment $missingKey `
        -Expected 'Auth:SigningKey is not configured'

    $shortKey = New-BaseEnvironment -EnvironmentName 'Development'
    $shortKey['Auth__SigningKey'] = 'short'
    $shortKey['ASPNETCORE_URLS'] = "http://127.0.0.1:$failPort"
    Assert-StartupFails -Name 'signing-key-short' -Environment $shortKey `
        -Expected 'at least 32 characters'

    $wildcardCors = New-BaseEnvironment -EnvironmentName 'Development'
    $wildcardCors['Cors__AllowedOrigins__0'] = '*'
    $wildcardCors['ASPNETCORE_URLS'] = "http://127.0.0.1:$failPort"
    Assert-StartupFails -Name 'cors-wildcard' -Environment $wildcardCors `
        -Expected "'*' is not allowed"

    $noneWithoutCors = New-BaseEnvironment -EnvironmentName 'Development'
    $noneWithoutCors['Auth__CookieSameSite'] = 'None'
    $noneWithoutCors['ASPNETCORE_URLS'] = "http://127.0.0.1:$failPort"
    Assert-StartupFails -Name 'samesite-none-without-cors' -Environment $noneWithoutCors `
        -Expected 'only valid for a cross-site SPA'

    $missingDataProtection = New-BaseEnvironment -EnvironmentName 'Production'
    $missingDataProtection.Remove('DataProtection__KeysPath')
    $missingDataProtection['ASPNETCORE_URLS'] = "http://127.0.0.1:$failPort"
    Assert-StartupFails -Name 'data-protection-missing' -Environment $missingDataProtection `
        -Expected 'DataProtection:KeysPath is not configured'

    $untrustedWithoutProxy = New-BaseEnvironment -EnvironmentName 'Production'
    $untrustedWithoutProxy.Remove('ForwardedHeaders__KnownProxies__0')
    $untrustedWithoutProxy.Remove('ForwardedHeaders__KnownProxies__1')
    $untrustedWithoutProxy['ASPNETCORE_URLS'] = "http://127.0.0.1:$failPort"
    Assert-StartupFails -Name 'forwarded-headers-without-proxy' -Environment $untrustedWithoutProxy `
        -Expected 'no trusted proxy is configured'

    Record "SMOKE PASSED ($script:checks checks)"
}
catch {
    $exitCode = 1
    $line = "FAIL $($_.Exception.Message)"
    Write-Host $line
    $script:evidence += $line

    if ($_.ScriptStackTrace) {
        $script:evidence += $_.ScriptStackTrace
    }
}
finally {
    foreach ($api in $script:processes) {
        Stop-Api $api
    }

    Clear-ManagedEnvironment
    $script:evidence | Set-Content -Encoding utf8 (Join-Path $generated 'smoke.txt')
    Write-Host ''
    Write-Host "evidence: $(Join-Path $generated 'smoke.txt')"
}

exit $exitCode
