# Minimal CI check for same-key booking idempotency.
#
# It creates an isolated `*_ci` database, applies the EF migrations, starts the API,
# registers one customer, loads one barber / service / salon / schedule
# (load/seed/minimal.sql), and runs the harness scenario `booking-idempotency`.
# No 1.2M-row seed and no dependency on barbersearch or barbersearch_load.
#
#   powershell -NoProfile -ExecutionPolicy Bypass `
#     -File load/ci/booking-idempotency.ps1
#
# Exit codes: 0 = passed, 2 = harness correctness failed, 3 = SQL verification failed.

param(
    [string]$Database = 'barbersearch_load_ci',
    [int]$Port = 5244,
    [switch]$SkipBuild,
    [switch]$KeepDatabase
)

$ErrorActionPreference = 'Stop'

# Refuse to drop a database that is not unmistakably a CI database.
if ($Database -notmatch '_ci$') {
    throw "Refusing to recreate '$Database': the name must end with '_ci'."
}

$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$generated = Join-Path $root 'load/generated/ci'
$harnessDll = Join-Path $root 'load/harness/bin/Release/net10.0/LoadHarness.dll'
$webProject = Join-Path $root 'BarberSearch/src/BarberSearch.Web'
$psql = 'C:\Program Files\PostgreSQL\16\bin\psql.exe'
$psqlArgs = @('-h', '127.0.0.1', '-p', '5432', '-U', 'postgres')
$baseUrl = "http://127.0.0.1:$Port"
$email = 'ci-customer@barbersearch.test'
$password = 'LoadTest!Passw0rd'
$barberId = '44444444-4444-4444-4444-444444444444'
$barberServiceId = '77777777-7777-7777-7777-777777777777'
$salonId = '11111111-1111-1111-1111-111111111111'

New-Item -ItemType Directory -Force -Path $generated | Out-Null
$env:PGPASSWORD = 'postgres'

function Invoke-Sql {
    param(
        [string]$Sql,
        [string]$Db = $Database
    )

    & $psql @psqlArgs -d $Db -q -tA -v ON_ERROR_STOP=1 -c $Sql
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

function Ensure-Customer {
    $body = @{
        email = $email
        password = $password
        firstName = 'CI'
        lastName = 'Customer'
    } | ConvertTo-Json

    try {
        Invoke-RestMethod -Method Post -Uri "$baseUrl/api/auth/register" `
            -ContentType 'application/json' -Body $body | Out-Null
        Write-Host "registered $email"
    }
    catch {
        $status = 0

        if ($null -ne $_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
        }

        if ($status -ne 409) {
            throw "Registration of $email failed with HTTP $status."
        }
    }
}

function Login-Customer {
    $body = @{ email = $email; password = $password } | ConvertTo-Json

    try {
        return Invoke-RestMethod -Method Post -Uri "$baseUrl/api/auth/login" `
            -ContentType 'application/json' -Body $body
    }
    catch {
        $status = 0

        if ($null -ne $_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
        }

        throw "Login of $email failed with HTTP $status."
    }
}

$app = $null
$exitCode = 0
$connection = "Host=127.0.0.1;Port=5432;Database=$Database;Username=postgres;Password=postgres"

try {
    Write-Host "recreating isolated CI database $Database..."
    & $psql @psqlArgs -d postgres -q -v ON_ERROR_STOP=1 `
        -c "DROP DATABASE IF EXISTS $Database WITH (FORCE);"
    & $psql @psqlArgs -d postgres -q -v ON_ERROR_STOP=1 `
        -c "CREATE DATABASE $Database;"

    if (-not $SkipBuild) {
        Write-Host 'building the solution and the harness...'

        & dotnet build (Join-Path $root 'BarberSearch/BarberSearch.sln') -c Debug --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw 'Solution build failed.'
        }

        & dotnet build (Join-Path $root 'load/harness/LoadHarness.csproj') -c Release --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw 'Harness build failed.'
        }
    }

    if (-not (Test-Path $harnessDll)) {
        throw "Harness is not built: $harnessDll"
    }

    Write-Host 'applying migrations...'
    $env:ConnectionStrings__Postgres = $connection
    & dotnet ef database update `
        --project (Join-Path $root 'BarberSearch/src/BarberSearch.Infrastructure.Postgresql') `
        --startup-project $webProject `
        --connection $connection | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet ef database update failed.'
    }

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Logging__EventLog__LogLevel__Default = 'None'
    ${env:Logging__LogLevel__Microsoft.EntityFrameworkCore} = 'Warning'
    $env:ASPNETCORE_URLS = $baseUrl
    $env:ConnectionStrings__Postgres = "$connection;Maximum Pool Size=20;Timeout=15;Command Timeout=60"
    $env:Auth__SigningKey = 'barbersearch-ci-booking-idempotency-signing-key-0123456789'
    $env:DataProtection__KeysPath = Join-Path $generated 'data-protection-keys'

    $app = Start-Process -FilePath 'dotnet' `
        -ArgumentList 'bin/Debug/net10.0/BarberSearch.Web.dll' `
        -WorkingDirectory $webProject `
        -PassThru `
        -RedirectStandardOutput (Join-Path $generated 'app.out.log') `
        -RedirectStandardError (Join-Path $generated 'app.err.log')

    Wait-Api

    Ensure-Customer
    $customer = Login-Customer
    $me = Invoke-RestMethod -Uri "$baseUrl/api/auth/me" `
        -Headers @{ Authorization = "Bearer $($customer.accessToken)" }

    & $psql @psqlArgs -d $Database -q -v ON_ERROR_STOP=1 `
        -f (Join-Path $root 'load/seed/minimal.sql')

    # The slot is far enough in the future to stay outside any local-day logic;
    # the booking endpoint only needs a valid start/end interval.
    $slotStart = (Get-Date).ToUniversalTime().Date.AddDays(21).AddHours(10)
    $slotEnd = $slotStart.AddMinutes(30)
    $slotStartText = $slotStart.ToString('yyyy-MM-ddTHH:mm:ssZ')
    $slotEndText = $slotEnd.ToString('yyyy-MM-ddTHH:mm:ssZ')

    $context = [ordered]@{
        baseUrl = $baseUrl
        customerToken = $customer.accessToken
        adminToken = $customer.accessToken
        customerId = $me.id
        adminId = $me.id
        manifest = [ordered]@{
            generatedAt = (Get-Date).ToUniversalTime().ToString('o')
            searchDate = $slotStart.ToString('yyyy-MM-dd')
            slotsDate = $slotStart.ToString('yyyy-MM-dd')
            cities = @()
            specializationIds = @()
            serviceIds = @()
            barbers = @()
            contentionReviewBarbers = @()
            createReviewBarbers = @()
            contendedBooking = [ordered]@{
                barberId = $barberId
                barberServiceId = $barberServiceId
                salonId = $salonId
                slots = @(
                    [ordered]@{
                        startAt = $slotStartText
                        endAt = $slotEndText
                    }
                )
            }
        }
    }

    $contextPath = Join-Path $generated 'context.json'
    $context | ConvertTo-Json -Depth 30 | Set-Content -Encoding utf8 $contextPath

    Write-Host 'running booking-idempotency harness...'
    & dotnet $harnessDll `
        --scenario booking-idempotency `
        --repeat 1 `
        --context $contextPath `
        --out (Join-Path $generated 'results.json')

    if ($LASTEXITCODE -ne 0) {
        $exitCode = 2
    }

    $sameRows = Invoke-Sql @"
SELECT count(*)
FROM bookings
WHERE idempotency_key = 'load-idem-same-r1';
"@
    $sameRows = [int]($sameRows | Select-Object -First 1)

    $afterRows = Invoke-Sql @"
SELECT count(*)
FROM bookings
WHERE idempotency_key = 'load-idem-after-cancel-r1'
  AND status IN (1, 2);
"@
    $afterRows = [int]($afterRows | Select-Object -First 1)

    $activeRows = Invoke-Sql @"
SELECT count(*)
FROM bookings
WHERE barber_profile_id = '$barberId'::uuid
  AND start_at = '$slotStartText'::timestamptz
  AND status IN (1, 2);
"@
    $activeRows = [int]($activeRows | Select-Object -First 1)

    $sameCancelledRows = Invoke-Sql @"
SELECT count(*)
FROM bookings
WHERE idempotency_key = 'load-idem-same-r1'
  AND status = 3;
"@
    $sameCancelledRows = [int]($sameCancelledRows | Select-Object -First 1)

    $verification = @(
        "same_key_rows=$sameRows (expected 1)",
        "same_key_cancelled_rows=$sameCancelledRows (expected 1)",
        "after_cancel_active_rows=$afterRows (expected 1)",
        "active_rows_on_slot=$activeRows (expected 1)"
    )
    $verification | Set-Content -Encoding utf8 (Join-Path $generated 'booking-idempotency-verification.txt')
    $verification | ForEach-Object { Write-Host $_ }

    if ($sameRows -ne 1 -or $sameCancelledRows -ne 1 -or $afterRows -ne 1 -or $activeRows -ne 1) {
        $exitCode = 3
    }
}
finally {
    if ($null -ne $app -and -not $app.HasExited) {
        Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
    }

    if (-not $KeepDatabase) {
        & $psql @psqlArgs -d postgres -q -v ON_ERROR_STOP=1 `
            -c "DROP DATABASE IF EXISTS $Database WITH (FORCE);" | Out-Null
    }
}

if ($exitCode -eq 0) {
    Write-Host 'booking-idempotency CI check: PASSED'
}
else {
    Write-Host "booking-idempotency CI check: FAILED (exit $exitCode)"
}

exit $exitCode
