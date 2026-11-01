# Спектакли и фикстуры E2E: сброс базы `barbersearch_e2e` в известное состояние.
#
# Скрипт только обёртка: делает то же, что описано в README, и отказывается
# сеять чужую базу (гвард есть и в самом seed.sql).
#
#   npm run test:e2e:seed
#   E2E_SEED_DATABASE_URL="Host=...;Database=barbersearch_e2e;..." npm run test:e2e:seed
$ErrorActionPreference = 'Stop'

$connectionString = $env:E2E_SEED_DATABASE_URL
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    $connectionString = $env:ConnectionStrings__Postgres
}
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    $connectionString = 'Host=localhost;Port=5432;Database=barbersearch_e2e;Username=postgres;Password=postgres'
}

$settings = @{}
foreach ($part in $connectionString.Split(';')) {
    if ([string]::IsNullOrWhiteSpace($part)) { continue }
    $pair = $part.Split('=', 2)
    if ($pair.Length -eq 2) { $settings[$pair[0].Trim()] = $pair[1].Trim() }
}

$database = $settings['Database']
if ($database -ne 'barbersearch_e2e') {
    throw "refusing to seed database '$database': only barbersearch_e2e is allowed (README, E2E)"
}

$psql = (Get-Command psql -ErrorAction SilentlyContinue)?.Source
if (-not $psql) {
    $candidate = 'C:\Program Files\PostgreSQL\16\bin\psql.exe'
    if (Test-Path $candidate) {
        $psql = $candidate
    } else {
        throw 'psql not found: add PostgreSQL\bin to PATH or set PG_BIN'
    }
}

$seed = Join-Path $PSScriptRoot 'seed.sql'
if (-not (Test-Path $seed)) {
    throw "seed file not found: $seed"
}

$env:PGPASSWORD = $settings['Password']
& $psql -h $settings['Host'] -p $settings['Port'] -U $settings['Username'] -d $database `
    -v ON_ERROR_STOP=1 -f $seed

exit $LASTEXITCODE
