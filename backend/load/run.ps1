param(
    [int]$Repeat = 3,
    [string]$Scenario = 'all',
    [int]$SearchConcurrency = 16,
    [int]$SlotsConcurrency = 16,
    [int]$ReadSeconds = 15,
    [switch]$SkipReset,
    [switch]$SkipAuth,
    [switch]$SkipMetrics,
    [int]$AppPid = 0
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$generated = Join-Path $PSScriptRoot 'generated'
$harnessDll = Join-Path $PSScriptRoot 'harness/bin/Release/net10.0/LoadHarness.dll'
$psql = 'C:\Program Files\PostgreSQL\16\bin\psql.exe'
$baseUrl = 'http://127.0.0.1:5243'
$adminEmail = 'load-admin@barbersearch.test'
$customerEmail = 'load-customer@barbersearch.test'
$password = 'LoadTest!Passw0rd'

New-Item -ItemType Directory -Force -Path $generated | Out-Null
$env:PGPASSWORD = 'postgres'

function Invoke-Sql {
    param([string]$Sql)

    & $psql -h 127.0.0.1 -p 5432 -U postgres -d barbersearch_load `
        -q -tA -v ON_ERROR_STOP=1 -c $Sql
}

function Wait-Api {
    param([int]$Seconds = 30)

    $deadline = (Get-Date).AddSeconds($Seconds)

    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri "$baseUrl/openapi/v1.json" `
                -UseBasicParsing -TimeoutSec 2

            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "The API at $baseUrl did not become ready within $Seconds seconds."
}

function Get-ListeningPid {
    $line = netstat -ano |
        Select-String -Pattern '127\.0\.0\.1:5243\s+0\.0\.0\.0:0\s+LISTENING\s+(\d+)' |
        Select-Object -First 1

    if ($null -eq $line -or $line.Matches.Count -eq 0) {
        return 0
    }

    return [int]$line.Matches[0].Groups[1].Value
}

function Ensure-User {
    param([string]$Email, [string]$FirstName, [string]$LastName)

    $body = @{
        email = $Email
        password = $password
        firstName = $FirstName
        lastName = $LastName
    } | ConvertTo-Json

    try {
        Invoke-RestMethod -Method Post -Uri "$baseUrl/api/auth/register" `
            -ContentType 'application/json' -Body $body | Out-Null
        Write-Host "registered $Email"
    }
    catch {
        $status = 0

        if ($null -ne $_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
        }

        if ($status -ne 409) {
            throw "Registration of $Email failed with HTTP $status. If it was 429, wait 60 s and retry."
        }
    }
}

function Login-User {
    param([string]$Email)

    $body = @{ email = $Email; password = $password } | ConvertTo-Json

    try {
        return Invoke-RestMethod -Method Post -Uri "$baseUrl/api/auth/login" `
            -ContentType 'application/json' -Body $body
    }
    catch {
        $status = 0

        if ($null -ne $_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
        }

        throw "Login of $Email failed with HTTP $status. If it was 429, wait 60 s and retry."
    }
}

if (-not (Test-Path $harnessDll)) {
    throw "Harness is not built. Run: dotnet build load/harness/LoadHarness.csproj -c Release"
}

$startedApp = $null
$metricsProcess = $null
$metricsStopFile = Join-Path $generated 'metrics.stop'
$exitCode = 0

try {
    if ($AppPid -le 0) {
        $AppPid = Get-ListeningPid
    }

    if ($AppPid -le 0) {
        Write-Host "starting $baseUrl against barbersearch_load..."

        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $env:Logging__EventLog__LogLevel__Default = 'None'
        ${env:Logging__LogLevel__Microsoft.EntityFrameworkCore} = 'Warning'
        $env:ASPNETCORE_URLS = $baseUrl
        $env:ConnectionStrings__Postgres = 'Host=127.0.0.1;Port=5432;Database=barbersearch_load;Username=postgres;Password=postgres;Maximum Pool Size=100;Timeout=15;Command Timeout=60'
        $env:Auth__SigningKey = 'barbersearch-load-test-signing-key-0123456789abcdef'

        $webProject = Join-Path $root 'BarberSearch/src/BarberSearch.Web'
        $startedApp = Start-Process -FilePath 'dotnet' `
            -ArgumentList 'bin/Debug/net10.0/BarberSearch.Web.dll' `
            -WorkingDirectory $webProject `
            -PassThru `
            -RedirectStandardOutput (Join-Path $generated 'app.out.log') `
            -RedirectStandardError (Join-Path $generated 'app.err.log')

        $AppPid = $startedApp.Id
        Wait-Api
    }
    else {
        Write-Host "using the API process $AppPid on $baseUrl"
    }

    if (-not $SkipAuth) {
        Ensure-User -Email $adminEmail -FirstName 'Load' -LastName 'Admin'
        Ensure-User -Email $customerEmail -FirstName 'Load' -LastName 'Customer'

        Invoke-Sql "UPDATE users SET role = 3 WHERE email = '$adminEmail';" | Out-Null

        $admin = Login-User -Email $adminEmail
        $customer = Login-User -Email $customerEmail

        $adminMe = Invoke-RestMethod -Uri "$baseUrl/api/auth/me" `
            -Headers @{ Authorization = "Bearer $($admin.accessToken)" }
        $customerMe = Invoke-RestMethod -Uri "$baseUrl/api/auth/me" `
            -Headers @{ Authorization = "Bearer $($customer.accessToken)" }

        [ordered]@{
            baseUrl = $baseUrl
            customerToken = $customer.accessToken
            adminToken = $admin.accessToken
            customerId = $customerMe.id
            adminId = $adminMe.id
        } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $generated 'auth.json')

        Write-Host "authenticated admin=$($adminMe.id) customer=$($customerMe.id)"

        Invoke-Sql @"
UPDATE bookings
SET user_id = '$($customerMe.id)'::uuid,
    updated_at = now()
WHERE id IN (
    SELECT md5('booking:' || b || ':' || n)::uuid
    FROM generate_series(2, 9) AS b
    CROSS JOIN generate_series(8, 57) AS n
    WHERE b IN (2, 6, 7, 9)
);
"@ | Out-Null
    }
    elseif (-not (Test-Path (Join-Path $generated 'auth.json'))) {
        throw "auth.json is missing; run without -SkipAuth."
    }

    if (-not $SkipReset) {
        & $psql -h 127.0.0.1 -p 5432 -U postgres -d barbersearch_load `
            -q -v ON_ERROR_STOP=1 -f (Join-Path $PSScriptRoot 'seed/reset-scenarios.sql')
    }

    & $psql -h 127.0.0.1 -p 5432 -U postgres -d barbersearch_load `
        -q -tA -f (Join-Path $PSScriptRoot 'seed/manifest.sql') |
        Set-Content -Encoding utf8 (Join-Path $generated 'manifest.json')

    $auth = Get-Content (Join-Path $generated 'auth.json') -Raw | ConvertFrom-Json
    $manifest = Get-Content (Join-Path $generated 'manifest.json') -Raw | ConvertFrom-Json

    [ordered]@{
        baseUrl = $auth.baseUrl
        customerToken = $auth.customerToken
        adminToken = $auth.adminToken
        customerId = $auth.customerId
        adminId = $auth.adminId
        manifest = $manifest
    } | ConvertTo-Json -Depth 30 | Set-Content -Encoding utf8 (Join-Path $generated 'context.json')

    if (-not $SkipMetrics) {
        if (Test-Path $metricsStopFile) {
            Remove-Item $metricsStopFile -Force
        }

        $metricsArgs = @(
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-File', (Join-Path $PSScriptRoot 'collect-metrics.ps1'),
            '-AppPid', $AppPid,
            '-OutFile', (Join-Path $generated 'metrics.csv'),
            '-DurationSeconds', '900',
            '-StopFile', $metricsStopFile
        )

        $metricsProcess = Start-Process -FilePath 'powershell' `
            -ArgumentList $metricsArgs `
            -PassThru `
            -WindowStyle Hidden
    }

    $harnessArgs = @(
        $harnessDll,
        '--scenario', $Scenario,
        '--repeat', $Repeat.ToString(),
        '--context', (Join-Path $generated 'context.json'),
        '--out', (Join-Path $generated 'results.json'),
        '--search-concurrency', $SearchConcurrency.ToString(),
        '--slots-concurrency', $SlotsConcurrency.ToString(),
        '--read-seconds', $ReadSeconds.ToString()
    )

    & dotnet @harnessArgs
    $exitCode = $LASTEXITCODE

    if (-not $SkipMetrics -and $null -ne $metricsProcess) {
        New-Item -ItemType File -Force -Path $metricsStopFile | Out-Null
        $metricsProcess.WaitForExit(5000) | Out-Null

        if (-not $metricsProcess.HasExited) {
            Stop-Process -Id $metricsProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }

    if ($Scenario -eq 'all' -or $Scenario -eq 'booking') {
        $manifest = Get-Content (Join-Path $generated 'manifest.json') -Raw | ConvertFrom-Json
        $contended = $manifest.contendedBooking
        $lines = @()
        $allOne = $true

        for ($run = 1; $run -le $Repeat; $run++) {
            $slot = $contended.slots[$run]
            $start = [DateTimeOffset]::Parse($slot.startAt).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ')
            $count = Invoke-Sql @"
SELECT count(*)
FROM bookings
WHERE barber_profile_id = '$($contended.barberId)'::uuid
  AND start_at = '$start'::timestamptz
  AND status IN (1, 2);
"@

            $count = [int]($count | Select-Object -First 1)
            $allOne = $allOne -and ($count -eq 1)
            $lines += "run $run slot $start active_rows=$count"
        }

        $total = Invoke-Sql @"
SELECT count(*)
FROM bookings
WHERE idempotency_key LIKE 'load-contended-%';
"@
        $total = [int]($total | Select-Object -First 1)
        $lines += "active_rows_with_harness_keys=$total (expected $($Repeat + 1): 1 warm-up + $Repeat measured)"

        if ($total -ne $Repeat + 1) {
            $allOne = $false
        }

        $lines | Set-Content -Encoding utf8 (Join-Path $generated 'booking-verification.txt')

        if (-not $allOne) {
            Write-Host 'booking database verification FAILED'
            $exitCode = 3
        }
        else {
            Write-Host 'booking database verification passed: exactly one active row per contended slot'
        }
    }

    Write-Host ''
    Write-Host "results:   $(Join-Path $generated 'results.json')"
    Write-Host "manifest:  $(Join-Path $generated 'manifest.json')"
    Write-Host "metrics:   $(Join-Path $generated 'metrics.csv')"
}
finally {
    if ($null -ne $metricsProcess) {
        if (-not $metricsProcess.HasExited) {
            New-Item -ItemType File -Force -Path $metricsStopFile | Out-Null
            $metricsProcess.WaitForExit(3000) | Out-Null
            Stop-Process -Id $metricsProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }

    if ($null -ne $startedApp -and -not $startedApp.HasExited) {
        Stop-Process -Id $startedApp.Id -Force -ErrorAction SilentlyContinue
    }
}

exit $exitCode
