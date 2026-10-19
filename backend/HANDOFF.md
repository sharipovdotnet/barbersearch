# HANDOFF — следующая сессия

> Прочитай `AGENT.md` §15, §16 и §17 перед изменениями.
> Дата: 2026-10-19. Ветка: `features/issue-4`. Git root: `D:\Projects\barbersearch`
> (workspace — `backend/`). HEAD этой сессии —
> `test(load): add load-test harness and baseline results`; перед ним отдельный
> `fix(bookings): serialize contended inserts to avoid GiST deadlock`.
> `origin/features/issue-4` запушен.

## 0. Где мы сейчас

Аутентификация, авторизация по владению, refresh-токены, security stamp, read-эндпоинты,
**booking under contention + idempotency**, **review → rating recalculation** и **load test**
готовы. Сборка: **8 проектов, 0 ошибок, 0 предупреждений**. Тесты: **139 domain + 100
application**, все зелёные через reflection-раннер (`AGENT.md` §5.3). API: **13 контроллеров,
81 endpoint (73 доменных + 8 auth)**; новых эндпоинтов и изменений контрактов в этом срезе нет.
Baseline и узкие места — `LOADTEST.md`.

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

## 1. Следующая задача — точечная оптимизация search, затем кэш и деплой

1. **Search plan.** Измерения показали: `ORDER BY (rating IS NULL), rating DESC, id` не
   использует `ix_barber_profiles_search`; date-only запрос — Parallel Seq Scan + Sort 10 000
   строк (78.6 ms). Сделать ORDER BY совместимым с частичным индексом (или эквивалентный
   индекс/переписать date-фильтр), перемерить `load/run.ps1`. Это не кэш.
2. **Кэш** — только если после этого search всё ещё упирается в p95 200 ms. Слоты и review-write
   уже имеют запас; кэшировать их незачем. Инвалидация рейтинга при кэше карточки/поиска станет
   отдельной задачей, потому что они читают сохранённое поле.
3. **Деплой:** персистентные ключи Data Protection, `Auth__SigningKey` в секрет-сторе,
   `UseForwardedHeaders`, `Cors:AllowedOrigins` + `SameSite=None`.

## 2. Открытые дефекты и техдолг

### 2.1 Вне Web-слоя (найдено на read-этапе, не исправлено)

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
- Load test подтвердил: publish/hide p95 85.5–92.0 ms, create p95 220.3–265.6 ms при бюджетах
  500/1000 ms; `SELECT … FOR UPDATE` сериализует конкурентов, lost update не воспроизведён
  (независимая SQL-сверка совпала). Пересчёт не является узким местом на этом объёме.

### 2.5 Load test — что осталось за кадром

1. **Search**: худший прогон p95 202.4 ms при бюджете 200 (медиана 181.5). Узкое место — план:
   `ORDER BY (rating IS NULL), rating DESC, id` не использует `ix_barber_profiles_search`,
   date-only запрос делает Parallel Seq Scan + Sort 10 000 строк (78.6 ms). Это следующий срез,
   не кэш.
2. **Booking**: p95 250.9–298.4 ms при бюджете 300 — почти весь запас съеден advisory lock-ом,
   который сериализует вставки одного барбера. Если нужен запас, следующий шаг — slot-claim
   (уникальная строка/индекс) или fast-path, а не кэш.
3. **Инструменты**: `pg_stat_statements` не включён (`shared_preload_libraries` пуст),
   `dotnet-counters` не установился (SSL/package source mapping), CPU PostgreSQL не читается
   через `Get-Process` (access denied). Поэтому GC/thread-pool и DB CPU не измерены; в отчёте это
   отмечено явно. Npgsql pool: максимум 51 backend-соединение при пуле 100, исчерпания не было.
4. **Стенд**: API, PostgreSQL и load generator на одной машине (24 logical CPU, 16 GB);
   `synchronous_commit=on`. Абсолютные цифры не переносятся на прод без поправки.

## 3. Как запускать и проверять

```powershell
# сборка (в workspace-write падает без диагностики — нужен danger-full-access, AGENT.md §5.2)
dotnet build BarberSearch/BarberSearch.sln

# тесты: dotnet test в харнессе блокирован (testhost), порядок обхода — AGENT.md §5.3
#   собрать тестовый проект, затем reflection-раннер из %TEMP%;
#   после прогона удалить раннер и временные скрипты

# миграции (InitialCreate / AddRefreshTokens / AddSecurityStamp / AddReadIndexes применены;
# в этом срезе новых миграций нет)
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
# 1) один раз: barbersearch_load + существующие миграции + seed (load/README.md)
# 2) полный прогон
dotnet build load/harness/LoadHarness.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File load/run.ps1 `
  -Repeat 3 -ReadSeconds 15

# повторно в течение 15 минут, не тратя auth rate limit
powershell -NoProfile -ExecutionPolicy Bypass -File load/run.ps1 `
  -Repeat 3 -ReadSeconds 15 -SkipAuth
```

`run.ps1` сам сбрасывает сценарные строки (`load/seed/reset-scenarios.sql`), генерирует
манифест и проверяет SQL-ом, что на contended-слоте ровно одна активная бронь. Результаты —
`load/generated/results.json`, `metrics.csv`, `booking-verification.txt` (каталог не коммитится).
Полные команды и ограничения — `load/README.md`, отчёт — `LOADTEST.md`.

`git add`/`git commit` тоже требуют `danger-full-access`: в workspace-write падает на
`.git/index.lock` (`Permission denied`).

## 4. Промпт для новой сессии

```text
Прочитай backend/HANDOFF.md и AGENT.md §15, §16, §17. Ветка features/issue-4, HEAD —
test(load): add load-test harness and baseline results; перед ним отдельный
fix(bookings) про deadlock contended insert. Рабочее дерево чистое, origin запушен.
Auth, ownership, read-эндпоинты, booking under contention (включая advisory lock),
rating recalculation и load test закрыты; не трогай их без необходимости.

Следующая задача: точечная оптимизация search-плана (ORDER BY против
ix_barber_profiles_search, date-only запрос), затем повторный load test и только потом
кэш, если измерения всё ещё этого требуют; затем деплой (Data Protection, Auth__SigningKey,
UseForwardedHeaders, CORS + SameSite=None). Baseline и узкие места — LOADTEST.md.
Открытые дефекты — §2.1 этого файла.

Сборка/тесты/git — через danger-full-access; тесты — reflection-раннер §5.3, после прогона
удалить раннер и временные скрипты; сборка 8 проектов, 0 ошибок, 0 предупреждений; все тесты
зелёные.
```
