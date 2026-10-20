# HANDOFF — следующая сессия

> Прочитай `AGENT.md` §15, §16 и §17 перед изменениями.
> Дата: 2026-10-20. Ветка: `features/issue-4`. Git root: `D:\Projects\barbersearch`
> (workspace — `backend/`). HEAD этой сессии — `docs(load): refresh baseline after search fix`;
> перед ним `test(load): support five repeats for review scenarios`,
> `perf(search): make keyset order use the search index` и
> `test(load): cover same-key booking idempotency`.
> `origin/features/issue-4` запушен.

## 0. Где мы сейчас

Аутентификация, авторизация по владению, refresh-токены, security stamp, read-эндпоинты,
**booking under contention + idempotency**, **review → rating recalculation**, **search-план** и
**load test** готовы. Same-key регрессия закрыта harness-сценарием и минимальным CI-прогоном;
search переведён на expression-индекс и перемерен 8 прогонами и полным `Repeat=5`. Сборка:
**8 проектов, 0 ошибок, 0 предупреждений**. Тесты: **139 domain + 100 application**, все
зелёные через reflection-раннер (`AGENT.md` §5.3). API: **13 контроллеров, 81 endpoint
(73 доменных + 8 auth)**; новых эндпоинтов и изменений контрактов в этом срезе нет.
Baseline и узкие места — `LOADTEST.md`; кэш не добавлялся.

Что сделано:

1. **Аутентификация** — cookie + JWT, политики `AdminOnly`/`BarberOnly`/`VerifiedEmail`,
   antiforgery, rate limiting (§17).
2. **Владение** — проверки в обработчиках после загрузки агрегата: 29 обработчиков профиля
   барбера и его детей, 5 обработчиков броней, `UpdateReview`, 3 обработчика пользователей;
   admin обходит проверку. Матрица — `AGENT.md` §17.8.
3. **Refresh-токены** — таблица `refresh_tokens` (миграция `AddRefreshTokens`), в БД только
   SHA-256, ротация с `revoked_at` + `replaced_by`, reuse-detection с отзывом всей цепочки,
   HttpOnly-cookie для браузера и тело для внешнего клиента, отзыв при logout / смене пароля /
   смене роли / деактивации (§17.9).
4. **Security stamp** — `User.SecurityStamp`, `RotateSecurityStamp()` в `ChangePasswordHash` /
   `ChangeRole` / `Deactivate`; claim `security_stamp` в JWT и cookie; scoped
   `SecurityStampValidator`; миграция `AddSecurityStamp` (§17.10).
5. **Read-эндпоинты (этап 2)** — `GET api/barbers`, `GET api/barbers/{id}`,
   `GET api/barbers/{id}/slots`, `GET api/bookings`, `GET api/reviews`: keyset-пагинация,
   response-DTO, `AsNoTracking`, проекции без Include коллекций; индексы `AddReadIndexes` (§16).
6. **Booking under contention + idempotency (этап 3):**
   - `POST api/bookings` при пересечении активных броней ловит SQLSTATE `23P01` из
     `ex_bookings_no_overlap` и отвечает `409 booking.add.overlap`, а не `500`. Предварительная
     `HasActiveOverlapAsync` в create не используется — источник истины БД.
   - Unique-индекс `ux_bookings_user_id_idempotency_key` защищает пару
     `(user_id, idempotency_key)`. Обработчик сначала читает бронь по ключу; при `23505`/`23P01`
     на вставке перечитывает её и возвращает id победителя; если перечитать не удалось —
     `409 booking.create.idempotencyKeyConflict`. `BookingRepository.AddAsync` снимает неудачную
     вставку с change tracker, чтобы повторное чтение работало на чистом состоянии.
   - Цена берётся из `BarberService.Price`; `CreateBookingRequest.Price` оставлен для
     совместимости и игнорируется.
   - `barberServiceId` проверяется узким чтением `IBarberProfileRepository.GetServiceAsync`:
     услуга должна принадлежать `barberProfileId` и быть активной, иначе `404`.
   - `EndAt` по-прежнему приходит от клиента; расчёт от `DurationMinutes` в этом срезе
     сознательно не менялся.
7. **Review → rating recalculation (этап 4, 2026-10-19):**
   - `ReviewRatingRecalculator` (Application) пересчитывает `BarberProfile.Rating` /
     `ReviewsCount` по сводке `IReviewRepository.GetRatingSummaryAsync`: только
     `IsPublished = true`, скрытые не входят ни в count, ни в average. Среднее округляет домен
     (`BarberProfile.UpdateRating`), SQL и Application правило не дублируют.
   - Триггеры: `CreateReview` (отзыв сразу опубликован), `UpdateReview` (опубликованного;
     правка скрытого сводку не меняет), `PublishReview`, `HideReview`. `DeleteReview`-эндпоинта
     нет, контракт не расширяли.
   - Атомарность: `IUnitOfWork` (Application) + `UnitOfWork` над `ApplicationDbContext`
     (Infrastructure) — изменение отзыва и сохранение профиля в одной транзакции; `Failure`
     и исключение откатывают обе записи.
   - Защита от lost update: `IBarberProfileRepository.GetForRatingUpdateAsync` читает только
     корень профиля через `SELECT … FOR UPDATE` (без пяти коллекций) до чтения сводки. Два
     параллельных `Publish`/`Hide` сериализуются, второй читает уже закоммиченное состояние.
   - Read-пути по-прежнему `AsNoTracking`; запись профиля идёт с отслеживанием.
8. **Тесты** — `Application.UnitTests`: +8 на пересчёт рейтинга (создание, правка
   опубликованного, публикация скрытого, скрытие опубликованного, правка скрытого, скрытие
   последнего, округление `4.666… → 4.67`, откат транзакции при ошибке сохранения профиля),
   всего **100**. `GetRatingSummaryAsync` в `FakeReviewRepository` считает по `_reviews`;
   `FakeUnitOfWork` восстанавливает снимки фейковых репозиториев при откате.
   `Domain.UnitTests` — **139** без изменений.
9. **Смоук на локальной PostgreSQL 16.3** (2026-10-19): профиль `Ivan Berlin` с двумя
   опубликованными отзывами (`4.50 / 2`):
   - создание отзыва `3` → `4.00 / 3` в карточке и поиске;
   - скрытие → `4.50 / 2`; публикация обратно → `4.00 / 3`;
   - правка опубликованного на `1` → `3.33 / 3` (округление);
   - скрытие всех → `rating = null`, `reviewsCount = 0` в карточке и поиске;
   - 3 параллельных `Publish` → `3.33 / 3`, затем 3 параллельных `Hide` → `null / 0`;
     в БД `0` опубликованных. Lost update не воспроизвёлся.
10. **Документация** — `AGENT.md` §5.3, §8, §12.2, §15.9, §16, §17.8; этот файл.
11. **Load test (2026-10-19):** отдельная БД `barbersearch_load` (12 000 барберов, 1.2M броней,
    80k отзывов), harness `load/`, отчёт `LOADTEST.md`. Baseline: search p95 180.5–202.4 ms
    (худший прогон на 2.4 ms выше бюджета 200), p99 < 270 ms; slots p95 13.0–13.6 ms; contended
    booking p95 250.9–298.4 ms при бюджете 300, ровно `1 × 201 / 49 × 409`, одна активная строка
    в БД; review publish/hide p95 85.5–92.0 ms, create p95 220.3–265.6 ms. Error rate 0.
    Lost update рейтинга не воспроизведён (SQL-сверка совпала).
12. **Исправлен дефект contended booking:** 50 параллельных INSERT на один слот могли дать
    `40P01` (deadlock внутри GiST exclusion check) и 500, а под нагрузкой — ни одного `201`.
    `BookingRepository.AddAsync` сериализует вставки одного барбера advisory lock-ом; `40P01`
    дополнительно маппится в `409 booking.add.deadlock`. Подробности — `LOADTEST.md` §6.
13. **Same-key booking regression (2026-10-20):** в `load/harness` добавлен сценарий
    `booking-idempotency`: warm-up, 10 параллельных POST с одним ключом → `10 × 201`, один id,
    одна строка; последовательный повтор → тот же id; отмена → `204`; новая бронь на тот же слот
    с другим ключом → `201`. `run.ps1` проверяет SQL-ом одну строку на ключ, отмену победителя и
    одну активную бронь на каждом слоте. Добавлен минимальный CI-прогон
    `load/ci/booking-idempotency.ps1` + `load/seed/minimal.sql` (1 барбер, 1 услуга, 1 салон,
    1 слот, отдельная `*_ci` БД, без 1.2M seed). Unit-тесты не менялись.
14. **Search plan (2026-10-20):** additive-миграция `AddSearchKeysetIndex` создаёт
    `ix_barber_profiles_search_keyset` на `(((rating IS NULL)), rating DESC, id) WHERE is_active
    AND is_verified`. LINQ, cursor-предикат и порядок null-рейтингов не менялись; старый
    `ix_barber_profiles_search` оставлен. `EXPLAIN`: date-only Seq Scan + Sort 75.6–87.3 ms →
    Index Scan 3.6–13.8 ms; без фильтров 8.2–9.5 → 0.05–0.13 ms. Search p95 29.8–40.6 ms на
    8 × 20 s (было 170.6–202.8 ms, худший прогон FAIL).
15. **Follow-up load test (2026-10-20, `Repeat=5`):** search p95 28.5–31.5 ms, p99 32.3–36.3,
    ошибок 0; slots p95 10.2–11.6; booking-idempotency p95 51.2–52.2; review publish/hide p95
    82.4–98.3; review create p95 189.3–269.8. Contended booking p95 257.5–304.0 ms — один прогон
    на 4 ms выше бюджета 300, корректность прежняя (`1 × 201 / 49 × 409`, одна активная строка
    на слот). SQL-сверка рейтингов по 12 профилям — 0 расхождений. Кэш не добавлялся.

## 1. Следующая задача — деплой; кэш измерениями не оправдан

1. **Деплой:** персистентные ключи Data Protection, `Auth__SigningKey` в секрет-сторе,
   `UseForwardedHeaders`, `Cors:AllowedOrigins` + `SameSite=None`. Это следующий срез.
2. **Booking headroom (опционально, отдельно):** contended p95 257.5–304.0 ms при бюджете 300;
   один прогон вышел на 4 ms. Причина — advisory lock, а не план. Если нужен запас, следующий
   шаг — slot-claim (уникальная строка/индекс) или fast-path, а не кэш. Поведение текущего
   correctness-fix не переделывать без отдельного среза и регрессии.
3. **Кэш** — не начинать: измерения 2026-10-20 не дают основания. Search проходит с запасом
   ≈5×, слоты и review-write имеют запас, booking ограничен корректностью. Если будущие
   измерения всё же потребуют кэша, это отдельный срез с инвалидацией рейтинга.

## 2. Открытые дефекты и техдолг

### 2.1 Вне Web-слоя (найдено на read-этапе, не исправлено)

Новых дефектов в срезе 2026-10-20 не найдено: search-план и same-key пробел закрыты,
SQL-сверка броней и рейтингов сошлась. Открытые пункты ниже остаются с read-этапа.

1. **`PUT api/barber-profiles/{id}/salons/{salonId}/primary` отвечает 409**, если другой салон
   профиля уже основной: EF Core отправляет `is_primary = true` раньше `false`, и filtered unique
   индекс `ux_barber_salons_primary_per_profile` отклоняет промежуточное состояние. Нужно
   `DEFERRABLE`-ограничение миграцией, два `SaveChanges` или явный порядок.
2. **Переупорядочивание фото дублировано:** `BarberProfile.ReorderPhoto` и
   `PortfolioPhoto.Reorder` — два обработчика одного эффекта, оба доступны по API. Один нужно
   убрать.

### 2.2 Auth (`AGENT.md` §17.7)

1. Фоновой уборки истёкших refresh-токенов нет (таблица растёт).
2. Две одновременные ротации одного refresh-токена не сериализованы на уровне БД
   (окно `SELECT`→`UPDATE`); при необходимости — `xmin` или условный `UPDATE`.
3. Ключи Data Protection не персистентны: в контейнере без тома сессии умрут после рестарта.
4. CORS для cross-site SPA не настроен (`Cors:AllowedOrigins` пуст).
5. Rate limiting за прокси без `UseForwardedHeaders` делит лимит на всех.
6. Подтверждение email/телефона административное.
7. `POST api/users/{id}/logins` оставлен только для admin.
8. `logout` stamp не ротирует, поэтому предъявленный access-токен доживает до 15 минут;
   перехеширование пароля при входе ротирует stamp и завершает другие сессии; валидатор тянет
   `favorite_barbers` через `GetByIdAsync`.

### 2.3 Booking create — осознанно вне среза

- `EndAt` не выводится из `BarberService.DurationMinutes`: клиент по-прежнему присылает конец
  интервала. Если это меняется, нужен отдельный законченный срез с тестами и документацией;
  цена и принадлежность услуги уже серверные.

### 2.4 Rating recalculation — сознательные ограничения

- Пересчёт синхронный и в транзакции с изменением отзыва. Это правильно для консистентности,
  но добавляет `SELECT … FOR UPDATE` + count/avg + UPDATE в write-путь отзыва; на load test
  смотреть, не становится ли публикация узким местом.
- Ручной `PUT api/barber-profiles/{id}/rating` (`UpdateBarberProfileRating`) остаётся override:
  следующее изменение отзыва перезапишет его фактической сводкой.
- Кэш карточки/поиска ещё не появился, поэтому инвалидация не нужна; при введении кэша
  изменение рейтинга должно её учитывать.
- Load test подтвердил: publish/hide p95 82.4–98.3 ms, create p95 189.3–269.8 ms при бюджетах
  500/1000 ms; `SELECT … FOR UPDATE` сериализует конкурентов, lost update не воспроизведён
  (независимая SQL-сверка по 12 профилям совпала). Пересчёт не является узким местом на этом
  объёме.

### 2.5 Load test — что осталось за кадром

1. **Search — закрыт 2026-10-20.** Expression-индекс `ix_barber_profiles_search_keyset` даёт
   Index Scan вместо Seq Scan + Sort: date-only 75.6–87.3 → 3.6–13.8 ms, search p95 29.8–40.6 ms
   на 8 × 20 s и 28.5–31.5 ms в `Repeat=5`. Кэш не нужен.
2. **Booking**: p95 257.5–304.0 ms при бюджете 300 — один прогон вышел на 4 ms. Почти весь запас
   съеден advisory lock-ом, который сериализует вставки одного барбера. Если нужен запас,
   следующий шаг — slot-claim (уникальная строка/индекс) или fast-path, а не кэш.
3. **Инструменты**: `pg_stat_statements` не включён (`shared_preload_libraries` пуст; включение
   требует рестарта общего локального инстанса, поэтому не делалось), `dotnet-counters` не
   установился (SSL/package source mapping), CPU PostgreSQL не читается через `Get-Process`
   (access denied). Поэтому GC/thread-pool и DB CPU не измерены; в отчёте это отмечено явно.
   Npgsql pool: максимум 51 backend-соединение при пуле 100, исчерпания не было.
4. **Стенд**: API, PostgreSQL и load generator на одной машине (24 logical CPU, 16 GB);
   `synchronous_commit=on`. Абсолютные цифры не переносятся на прод без поправки.

## 3. Как запускать и проверять

```powershell
# сборка (в workspace-write падает без диагностики — нужен danger-full-access, AGENT.md §5.2)
dotnet build BarberSearch/BarberSearch.sln

# тесты: dotnet test в харнессе блокирован (testhost), порядок обхода — AGENT.md §5.3
#   собрать тестовый проект, затем reflection-раннер из %TEMP%;
#   после прогона удалить раннер и временные скрипты

# миграции (InitialCreate / AddRefreshTokens / AddSecurityStamp / AddReadIndexes /
# AddSearchKeysetIndex применены; новых миграций в следующем срезе нет)
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet ef database update --project BarberSearch/src/BarberSearch.Infrastructure.Postgresql `
  --startup-project BarberSearch/src/BarberSearch.Web

# запуск: content root = каталог проекта, EventLog приглушён
cd BarberSearch/src/BarberSearch.Web
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Logging__EventLog__LogLevel__Default='None'
$env:ASPNETCORE_URLS='http://127.0.0.1:5243'
dotnet bin/Debug/net10.0/BarberSearch.Web.dll
```

Локальная PostgreSQL 16.3 держит порт 5432, база `barbersearch`, строка подключения — в
`appsettings.json`. Приложению нужен доступ на запись в `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`,
иначе cookie-вход падает. Логин/регистрация/refresh/change-password ограничены 10 попытками в
минуту на IP: смоук с несколькими пользователями легко ловит `429` — выдержи паузу между
прогонами или разбей сценарий на окна. На `POST api/bookings` и review-эндпоинтах лимита нет.

Load test (отдельная база, API сам поднимается и останавливается):

```powershell
# 1) один раз: barbersearch_load + все миграции + seed (load/README.md)
# 2) полный прогон
dotnet build load/harness/LoadHarness.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File load/run.ps1 `
  -Repeat 5 -ReadSeconds 15

# повторно, пока токены живы (15 минут), не тратя auth rate limit
powershell -NoProfile -ExecutionPolicy Bypass -File load/run.ps1 `
  -Repeat 5 -ReadSeconds 15 -SkipAuth

# только search, 8 окон по 20 с
powershell -NoProfile -ExecutionPolicy Bypass -File load/run.ps1 `
  -Scenario search -Repeat 8 -ReadSeconds 20

# same-key без 1.2M seed, отдельная *_ci БД
powershell -NoProfile -ExecutionPolicy Bypass `
  -File load/ci/booking-idempotency.ps1
```

`run.ps1` сам сбрасывает сценарные строки (`load/seed/reset-scenarios.sql`), генерирует
манифест и проверяет SQL-ом, что на contended-слоте ровно одна активная бронь, а на
idempotency-слоте — одна строка на ключ и одна активная бронь после отмены. Результаты —
`load/generated/results.json`, `metrics.csv`, `booking-verification.txt` (каталог не коммитится).
Полные команды и ограничения — `load/README.md`, отчёт — `LOADTEST.md`.

`git add`/`git commit` тоже требуют `danger-full-access`: в workspace-write падает на
`.git/index.lock` (`Permission denied`).

## 4. Промпт для новой сессии

```text
Прочитай backend/HANDOFF.md и AGENT.md §15, §16, §17. Ветка features/issue-4, HEAD —
docs(load): refresh baseline after search fix; перед ним test(load): support five repeats
for review scenarios, perf(search): make keyset order use the search index и
test(load): cover same-key booking idempotency. Рабочее дерево чистое, origin запушен.
Auth, ownership, read-эндпоинты, booking under contention (advisory lock + same-key),
rating recalculation, search-план и load test закрыты; кэш измерениями не оправдан.
Не трогай их без необходимости.

Следующая задача: деплой — персистентные ключи Data Protection, Auth__SigningKey в
секрет-сторе, UseForwardedHeaders, Cors:AllowedOrigins + SameSite=None. Кэш/Redis/output
caching не начинать. Booking headroom (p95 257.5–304.0 при бюджете 300) — отдельный
срез slot-claim/fast-path, только если понадобится; текущий correctness-fix не
переделывать. Baseline и планы — LOADTEST.md; открытые дефекты — §2.1 этого файла.

Сборка/тесты/git — через danger-full-access; тесты — reflection-раннер §5.3, после прогона
удалить раннер и временные скрипты; сборка 8 проектов, 0 ошибок, 0 предупреждений; все тесты
зелёные (139 domain + 100 application).
```
