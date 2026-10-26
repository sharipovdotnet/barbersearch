param(
    [Parameter(Mandatory = $true)][int]$AppPid,
    [Parameter(Mandatory = $true)][string]$OutFile,
    [string]$Psql = 'psql',
    [string]$DbHost = '127.0.0.1',
    [int]$DbPort = 5432,
    [string]$DbUser = 'postgres',
    [string]$DbPassword = $env:BARBERSEARCH_DB_PASSWORD,
    [string]$Database = 'barbersearch_load',
    [int]$DurationSeconds = 600,
    [int]$IntervalMs = 1000,
    [string]$StopFile = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($DbPassword)) {
    $DbPassword = $env:PGPASSWORD
}

if ([string]::IsNullOrWhiteSpace($DbPassword)) {
    throw 'The database password is required: pass -DbPassword or set BARBERSEARCH_DB_PASSWORD (PGPASSWORD is also accepted).'
}

$env:PGPASSWORD = $DbPassword
$invariant = [System.Globalization.CultureInfo]::InvariantCulture

$appProcess = Get-Process -Id $AppPid -ErrorAction SilentlyContinue
if ($null -eq $appProcess) {
    throw "Application process $AppPid is not running."
}

$previousAppCpu = $appProcess.TotalProcessorTime
$previousPostgresCpu = [TimeSpan]::Zero
$previousAt = Get-Date

$postgresProcesses = @(Get-Process postgres -ErrorAction SilentlyContinue)
foreach ($process in $postgresProcesses) {
    $previousPostgresCpu += $process.TotalProcessorTime
}

$header = 'timestamp,app_cpu_pct,app_ws_mb,pg_cpu_pct,' +
          'pg_conn_active,pg_conn_idle,pg_conn_idle_tx,pg_conn_total,pg_waiting_locks,' +
          'xact_commit,blks_hit,blks_read,temp_bytes'
Set-Content -Encoding utf8 -Path $OutFile -Value $header

$deadline = (Get-Date).AddSeconds($DurationSeconds)
$cpuCount = [Environment]::ProcessorCount

while ((Get-Date) -lt $deadline) {
    if ($StopFile -and (Test-Path $StopFile)) {
        break
    }

    $now = Get-Date
    $appProcess = Get-Process -Id $AppPid -ErrorAction SilentlyContinue
    if ($null -eq $appProcess) {
        break
    }

    $elapsedMs = [Math]::Max(1, ($now - $previousAt).TotalMilliseconds)
    $appCpuDelta = ($appProcess.TotalProcessorTime - $previousAppCpu).TotalMilliseconds
    $appCpuPct = 100.0 * $appCpuDelta / ($elapsedMs * $cpuCount)

    $postgresProcesses = @(Get-Process postgres -ErrorAction SilentlyContinue)
    $postgresCpu = [TimeSpan]::Zero
    foreach ($process in $postgresProcesses) {
        $postgresCpu += $process.TotalProcessorTime
    }

    $pgCpuDelta = ($postgresCpu - $previousPostgresCpu).TotalMilliseconds
    $pgCpuPct = 100.0 * $pgCpuDelta / ($elapsedMs * $cpuCount)

    $dbStats = & $Psql -h $DbHost -p $DbPort -U $DbUser -d $Database -q -tA -F ',' -c @"
select
    (select count(*) from pg_stat_activity
      where datname = '$Database' and state = 'active'),
    (select count(*) from pg_stat_activity
      where datname = '$Database' and state = 'idle'),
    (select count(*) from pg_stat_activity
      where datname = '$Database' and state = 'idle in transaction'),
    (select count(*) from pg_stat_activity where datname = '$Database'),
    (select count(*) from pg_locks where not granted),
    (select xact_commit from pg_stat_database where datname = '$Database'),
    (select blks_hit from pg_stat_database where datname = '$Database'),
    (select blks_read from pg_stat_database where datname = '$Database'),
    (select temp_bytes from pg_stat_database where datname = '$Database')
"@

    $values = ($dbStats | Select-Object -First 1).Split(',')
    $line = ('{0},{1},{2},{3},{4}' -f $now.ToString('O', $invariant), $appCpuPct.ToString('F2', $invariant), ($appProcess.WorkingSet64 / 1MB).ToString('F1', $invariant), $pgCpuPct.ToString('F2', $invariant), ($values -join ','))

    Add-Content -Encoding utf8 -Path $OutFile -Value $line

    $previousAppCpu = $appProcess.TotalProcessorTime
    $previousPostgresCpu = $postgresCpu
    $previousAt = $now

    Start-Sleep -Milliseconds $IntervalMs
}
