# Сырые запросы/ответы ручек переноса визита (issue-95) для отчёта.
#
#   powershell -NoProfile -File verification\issue-95-api-samples.ps1
#
# Использует `curl.exe` (Windows 10+): скрипт рассчитан и на Windows PowerShell 5.1.
# Сценарий работает с фикстурным клиентом `mutations`: его брони — у барбера B1,
# у которого в сиде есть рабочая неделя. Две брони перемещаются и возвращаются
# обратно; удаления брони в API нет, поэтому уборка — обратным переносом.
$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5243'
$out = Join-Path $PSScriptRoot 'issue-95-api-samples.txt'
$jar = Join-Path $env:TEMP 'issue-95-cookies.txt'
$lines = New-Object System.Collections.Generic.List[string]
function Write-Line([string]$text) { $lines.Add($text); Write-Host $text }
function Show-Json([string]$file) {
    if (-not (Test-Path $file)) { return '<no body>' }
    $raw = Get-Content $file -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) { return '<no body>' }
    try { return ($raw | ConvertFrom-Json | ConvertTo-Json -Depth 6) } catch { return $raw }
}

Remove-Item $jar -ErrorAction SilentlyContinue
$loginFile = Join-Path $env:TEMP 'issue-95-login.json'
# Тело — во временный файл: PowerShell 5.1 съедает кавычки в аргументах curl.exe.
$loginBody = Join-Path $env:TEMP 'issue-95-login-body.json'
Set-Content -Path $loginBody -Encoding ASCII -Value '{"email":"e2e-mutations@barbersearch.test","password":"e2e-password","rememberMe":false}'
$csrf = (curl.exe -s -c $jar -b $jar "$base/api/auth/csrf" | ConvertFrom-Json).token
$loginStatus = curl.exe -s -b $jar -c $jar -o $loginFile -w '%{http_code}' `
    -H 'Content-Type: application/json' -H "X-CSRF-TOKEN: $csrf" `
    --data-binary "@$loginBody" `
    "$base/api/auth/login/cookie"
Write-Line "POST /api/auth/login/cookie -> $loginStatus"
$csrf = (curl.exe -s -b $jar -c $jar "$base/api/auth/csrf" | ConvertFrom-Json).token
$me = curl.exe -s -b $jar "$base/api/auth/me" | ConvertFrom-Json
$page = curl.exe -s -b $jar "$base/api/bookings?scope=upcoming&pageSize=20&userId=$($me.id)" | ConvertFrom-Json
$source = $page.items[0]
$target = $page.items[2]
# Дата слотов: любая дата в пределах горизонта. Берём UTC-дату начала + 2 дня:
# сервер сам решает, какой локальный день салона это, и возвращает сетку на него.
$targetDate = ([DateTimeOffset]$target.startAt).UtcDateTime.AddDays(2).ToString('yyyy-MM-dd')

$slotsFile = Join-Path $env:TEMP 'issue-95-slots.json'
$slotsStatus = curl.exe -s -b $jar -o $slotsFile -w '%{http_code}' `
    "$base/api/bookings/$($target.id)/reschedule-slots?date=$targetDate"
$slots = Get-Content $slotsFile -Raw | ConvertFrom-Json
$newStartAt = $slots.slots[0].startAt

Write-Line ''
Write-Line "Бронь-цель: $($target.id), источник: $($source.id), дата слотов: $targetDate"
Write-Line ''
Write-Line "GET /api/bookings/$($target.id)/reschedule-slots?date=$targetDate -> $slotsStatus"
Write-Line (Show-Json $slotsFile)

function Post-Reschedule([string]$bookingId, [string]$startAt) {
    $file = Join-Path $env:TEMP 'issue-95-post.json'
    $bodyFile = Join-Path $env:TEMP 'issue-95-post-body.json'
    Set-Content -Path $bodyFile -Encoding ASCII -Value ('{"startAt":"' + $startAt + '"}')
    $status = curl.exe -s -b $jar -c $jar -o $file -w '%{http_code}' `
        -X POST -H 'Content-Type: application/json' -H "X-CSRF-TOKEN: $csrf" `
        --data-binary "@$bodyFile" "$base/api/bookings/$bookingId/reschedule"
    return @{ Status = $status; Body = (Show-Json $file) }
}

try {
    $move = Post-Reschedule $target.id $newStartAt
    Write-Line ''
    Write-Line "POST /api/bookings/$($target.id)/reschedule { startAt: $newStartAt } -> $($move.Status)"
    Write-Line $move.Body

    $after = curl.exe -s -b $jar "$base/api/bookings?scope=upcoming&pageSize=20&userId=$($me.id)" | ConvertFrom-Json
    $moved = $after.items | Where-Object { $_.id -eq $target.id }
    Write-Line ''
    Write-Line "GET /api/bookings после переноса: startAt=$($moved.startAt) endAt=$($moved.endAt) price=$($moved.price) status=$($moved.status)"

    $occupied = Post-Reschedule $source.id $newStartAt
    Write-Line ''
    Write-Line "POST /api/bookings/$($source.id)/reschedule на занятое время -> $($occupied.Status)"
    Write-Line $occupied.Body

    $notSlot = Post-Reschedule $source.id '2026-12-01T12:34:56+00:00'
    Write-Line ''
    Write-Line "POST /api/bookings/$($source.id)/reschedule вне сетки слотов -> $($notSlot.Status)"
    Write-Line $notSlot.Body

    $pastStart = (Get-Date).ToUniversalTime().AddMinutes(-4).ToString('yyyy-MM-ddTHH:mm:sszzz')
    $past = Post-Reschedule $source.id $pastStart
    Write-Line ''
    Write-Line "POST /api/bookings/$($source.id)/reschedule в прошлое ($pastStart) -> $($past.Status)"
    Write-Line $past.Body

    $farStart = (Get-Date).ToUniversalTime().AddDays(91).ToString('yyyy-MM-ddTHH:mm:sszzz')
    $far = Post-Reschedule $source.id $farStart
    Write-Line ''
    Write-Line "POST /api/bookings/$($source.id)/reschedule за горизонт ($farStart) -> $($far.Status)"
    Write-Line $far.Body
}
finally {
    $restore = Post-Reschedule $target.id $target.startAt
    Write-Line ''
    Write-Line "Уборка: POST /api/bookings/$($target.id)/reschedule обратно на $($target.startAt) -> $($restore.Status)"
}

Set-Content -Path $out -Value ($lines -join "`r`n") -Encoding UTF8
Write-Host "saved $out"
