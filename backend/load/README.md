# Load-test harness

Нагрузочный harness для `BarberSearch` живёт **вне** `BarberSearch.sln`, поэтому
решение остаётся из 8 проектов. Он не добавляет NuGet-зависимостей: только BCL
(`HttpClient`, `System.Text.Json`).

## Что здесь лежит

| Путь | Назначение |
|---|---|
| `seed/seed.sql` | set-based генератор данных; идемпотентный, с `load_seed_state` |
| `seed/reset-scenarios.sql` | откатывает только строки, которые меняют сценарии |
| `seed/manifest.sql` | печатает JSON-манифест: id городов, услуги, барберы, слоты |
| `harness/` | консольный .NET 10 harness (search / slots / booking / review) |
| `collect-metrics.ps1` | сэмплирует CPU/RAM приложения и `pg_stat_activity`/`pg_locks` |
| `run.ps1` | поднимает API, логинится, сбрасывает сценарии, гоняет harness, собирает метрики |

Требуется: .NET SDK 10, PostgreSQL 16.3 на `127.0.0.1:5432`, `psql` из
`C:\Program Files\PostgreSQL\16\bin`. Скрипты запускаются из PowerShell 5.1 с
`-ExecutionPolicy Bypass`.

## 1. Отдельная база

`barbersearch` (со смоук-данными) не трогаем. Все команды ниже работают только с
`barbersearch_load`.

```powershell
$psql = 'C:\Program Files\PostgreSQL\16\bin\psql.exe'
$env:PGPASSWORD = 'postgres'

# пересоздать пустую load-базу
& $psql -h 127.0.0.1 -p 5432 -U postgres -d postgres -c `
  'DROP DATABASE IF EXISTS barbersearch_load WITH (FORCE);'
& $psql -h 127.0.0.1 -p 5432 -U postgres -d postgres -c `
  'CREATE DATABASE barbersearch_load;'

# существующие миграции: InitialCreate / AddRefreshTokens / AddSecurityStamp / AddReadIndexes
dotnet ef database update `
  --project BarberSearch/src/BarberSearch.Infrastructure.Postgresql `
  --startup-project BarberSearch/src/BarberSearch.Web `
  --connection 'Host=127.0.0.1;Port=5432;Database=barbersearch_load;Username=postgres;Password=postgres'
```

`dotnet ef` и `dotnet build` в confined-сессии требуют `danger-full-access`
(`AGENT.md` §5.2).

## 2. Данные

```powershell
& $psql -h 127.0.0.1 -p 5432 -U postgres -d barbersearch_load `
  -v ON_ERROR_STOP=1 -f load/seed/seed.sql
```

Скрипт создаёт 12 000 барберов (10 000 активных и verified), 400 салонов,
168 000 строк расписания, 1 200 000 броней и 80 036 отзывов. На этой машине
заняло ~150 с. Повторный запуск не дублирует строки: каждый шаг отмечен в
`load_seed_state`, id детерминированные, вставки идут с `ON CONFLICT DO NOTHING`.
Если база уже засеяна и нужно повторить только сценарии, используйте
`seed/reset-scenarios.sql` (его вызывает `run.ps1`).

## 3. Сборка harness

```powershell
dotnet build load/harness/LoadHarness.csproj -c Release
```

Это отдельный проект, не входящий в `BarberSearch.sln`.

## 4. Запуск

Самый простой путь — `run.ps1`: он сам поднимет API на `http://127.0.0.1:5243`,
залогинит `load-admin@barbersearch.test` и `load-customer@barbersearch.test`,
сбросит сценарные строки, сгенерирует манифест, соберёт метрики и остановит API.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File load/run.ps1 `
  -Repeat 3 -ReadSeconds 15
```

Повторный запуск в течение 15 минут (токены ещё живы, лимит auth не тратится):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File load/run.ps1 `
  -Repeat 3 -ReadSeconds 15 -SkipAuth
```

Результаты: `load/generated/results.json`, `manifest.json`, `metrics.csv`,
`booking-verification.txt`, `app.out.log`. Каталог `load/generated/` в git не
попадает. `run.ps1` завершает работу с кодом `2`, если не сошлась проверка
корректности (например, рейтинг/количество отзывов не совпали), и с кодом `3`,
если в БД не ровно одна активная бронь на contended-слот.

Если API уже запущен отдельно, передайте его PID: `-AppPid <pid>`.

## 5. Что именно измеряется

- **search**: `GET api/barbers`; микс city / specializationId / serviceId / date /
  cursor / pageSize, 16 параллельных клиентов, 5 с прогрева + 3 окна по 15 с.
- **slots**: `GET api/barbers/{id}/slots`; 1 000 случайных барберов, 16 клиентов,
  прогрев + 3×15 с.
- **booking-contended**: 50 одновременных `POST api/bookings` на один слот с
  разными `idempotencyKey`; ожидается ровно `1 × 201` и `49 × 409`, без 500.
  После прогона `run.ps1` проверяет SQL-ом, что на каждом слоте ровно одна
  активная бронь.
- **review-publish-hide**: 20 одновременных `Publish`/`Hide` отзывов одного
  барбера; проверяется, что публичный список, `rating` и `reviewsCount` совпадают
  с ожидаемым набором (14 ранее скрытых отзывов), id совпадают.
- **review-create**: 50 одновременных `POST api/reviews` по завершённым броням
  одного барбера; проверяется `rating = round(sum/count, 2)` и `reviewsCount`.

Бюджеты зафиксированы до прогона: search p95 200 / p99 500 мс (§15.7), booking
p95 300 мс (§15.7), slots p95 300 / p99 800 мс, review publish/create p95 500 /
p99 1000 мс.

## 6. Ограничения стенда

- k6 не установлен; NBomber нет в NuGet-кэше, а restore с `nuget.org` в этой
  среде падает на SSL/package source mapping. Поэтому harness — свой .NET-клиент
  без новых зависимостей.
- `dotnet-counters` установить не удалось по той же причине, поэтому счётчики
  GC gen2 и thread-pool queue length не сняты. Это честно отмечено в
  `LOADTEST.md`.
- `pg_stat_statements` доступен как extension, но не включён в
  `shared_preload_libraries`; локальный общий инстанс не перезапускался.
- PostgreSQL-процессы не читаются через `Get-Process` (доступ denied), поэтому
  CPU PostgreSQL не измерен; используются `pg_stat_activity`, `pg_locks` и
  `pg_stat_database` (xact_commit, blks_hit/read, temp_bytes).
- Генератор, API и PostgreSQL живут на одной машине (24 logical CPU, 16 GB RAM),
  поэтому клиент конкурирует с сервером за CPU. Это надо учитывать при сравнении
  с продакшеном.
