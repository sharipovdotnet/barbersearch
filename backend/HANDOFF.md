# HANDOFF — следующая сессия

> Прочитай `AGENT.md` §15, §16 и §17 перед изменениями.
> Дата: 2026-10-26. Ветка: `features/issue-6`. Git root: `D:\Projects\barbersearch`
> (workspace — `backend/`). Срез R-01–R-54 **закоммичен** девятью коммитами поверх `9736b85`
> (merge PR #5) и запушен в `origin/features/issue-6`; рабочее дерево чистое. 10 проектов,
> сборка Debug/Release — 0/0, тесты 184 + 212 + 71 + 12 зелёные в обоих конфигурациях,
> деплой-смоук — 104 проверки. Перечень коммитов и доказательства — §0.1.

## 0. Где мы сейчас

Аутентификация, авторизация по владению, refresh-токены, security stamp, read-эндпоинты,
**booking under contention + idempotency**, **review → rating recalculation**, **search-план**,
**load test** и **деплой-срез** готовы. Фактический выкат в прод не выполнялся.

Сборка: **10 проектов, 0 ошибок, 0 предупреждений** (`TreatWarningsAsErrors` + анализаторы).
Тесты: **184 domain + 212 application + 71 infrastructure + 12 integration**, все зелёные
(`dotnet test` с `-p:BaseOutputPath` в `%TEMP%`, `AGENT.md` §5.2).
API: **13 контроллеров,
81 endpoints (73 доменных + 8 auth)**; поиск, карточка, слоты и список отзывов читаются
анонимно (`reads` 120/мин на IP, R-25), остальное закрыто; единственный новый эндпоинт
контракта — админская очередь `GET api/reviews/moderation` (R-35); также добавлены
`/health/live` и `/health/ready`. Baseline и узкие места — `LOADTEST.md`;
кэш не добавлялся. Runbook деплоя — `DEPLOY.md`.

Что сделано:

1. **Аутентификация** — cookie + JWT, политики `AdminOnly`/`BarberOnly`/`VerifiedEmail`,
   antiforgery, rate limiting, включая logout (§17).
2. **Владение** — проверки в обработчиках после загрузки агрегата: 29 обработчиков профиля
   барбера и его детей, 5 обработчиков броней, `UpdateReview`, 3 обработчика пользователей;
   admin обходит проверку. Матрица — `AGENT.md` §17.8.
3. **Refresh-токены** — таблица `refresh_tokens` (миграция `AddRefreshTokens`), в БД только
   SHA-256, ротация с `revoked_at` + `replaced_by`, reuse-detection с отзывом всей цепочки,
   HttpOnly-cookie для браузера и тело для внешнего клиента, отзыв при logout / смене пароля /
   смене роли / деактивации (§17.9). Фоновая уборка (R-50) удаляет строки, истёкшие более
   30 дней назад: прогон сразу на старте и далее каждые 60 минут, индекс
   `ix_refresh_tokens_expires_at`.
4. **Security stamp** — `User.SecurityStamp`, `RotateSecurityStamp()` в `ChangePasswordHash` /
   `ChangeRole` / `Deactivate`; claim `security_stamp` в JWT и cookie; scoped
   `SecurityStampValidator`; миграция `AddSecurityStamp` (§17.10).
5. **Read-эндпоинты (этап 2)** — `GET api/barbers`, `GET api/barbers/{id}`,
   `GET api/barbers/{id}/slots`, `GET api/bookings`, `GET api/reviews`: keyset-пагинация,
   response-DTO, `AsNoTracking`, проекции без Include коллекций; индексы `AddReadIndexes` (§16).
6. **Booking under contention + idempotency (этап 3):**
   - `POST api/bookings` при пересечении активных броней ловит SQLSTATE `23P01` из
     `ex_bookings_no_overlap` и отвечает `409 booking.add.overlap`, а не `500`.
   - Unique-индекс `ux_bookings_user_id_idempotency_key`; повтор с тем же ключом возвращает
     существующую бронь, проигравшая конкурентная вставка перечитывается по ключу.
   - Цена берётся из `BarberService.Price`; `barberServiceId` проверяется узким чтением и
     должен принадлежать профилю и быть активным.
   - `EndAt` по-прежнему приходит от клиента; расчёт от `DurationMinutes` сознательно не
     менялся.
7. **Review → rating recalculation (этап 4, 2026-10-19)** — `ReviewRatingRecalculator`,
   только опубликованные отзывы, транзакция `IUnitOfWork`, `SELECT … FOR UPDATE` против
   lost update; с R-35 (2026-10-24) создание отзыв не пересчитывает — только публикация,
   скрытие и правка опубликованного; подробности — `AGENT.md` §12.2 и §15.5.
8. **Тесты** — `Application.UnitTests`: **212** `[Fact]`; `Domain.UnitTests` — **184**;
   `Infrastructure.UnitTests` — **71**; `IntegrationTests` — **11**.
9. **Смоук рейтинга и read-эндпоинтов** — на локальной PostgreSQL; результаты в
   `AGENT.md` §17.6 и §16.
10. **Документация** — `AGENT.md` §5.3, §8, §12.2, §15, §16, §17; этот файл.
11. **Load test (2026-10-19):** отдельная БД `barbersearch_load` (12 000 барберов, 1.2M броней,
    80k отзывов), harness `load/`, отчёт `LOADTEST.md`. Baseline: search p95 180.5–202.4 ms,
    slots p95 13.0–13.6 ms, contended booking p95 250.9–298.4 ms, review publish/hide p95
    85.5–92.0 ms. Error rate 0.
12. **Исправлен дефект contended booking** — advisory lock на барбера + маппинг `40P01` в
    `409 booking.add.deadlock`; подробности — `LOADTEST.md` §6.
13. **Same-key booking regression (2026-10-20)** — сценарий `booking-idempotency` в harness,
    минимальный CI-прогон `load/ci/booking-idempotency.ps1`.
14. **Search plan (2026-10-20)** — additive-миграция `AddSearchKeysetIndex`, expression-индекс
    `ix_barber_profiles_search_keyset`; search p95 29.8–40.6 ms на 8 × 20 s (было
    170.6–202.8 ms). LINQ и порядок не менялись.
15. **Follow-up load test (2026-10-20, `Repeat=5`)** — search p95 28.5–31.5 ms; slots p95
    10.2–11.6; booking-idempotency p95 51.2–52.2; review publish/hide p95 82.4–98.3;
    contended booking p95 257.5–304.0 ms (один прогон на 4 ms выше бюджета 300, корректность
    прежняя: `1 × 201 / 49 × 409`). Кэш не добавлялся.
16. **Деплой-срез (2026-10-21):**
    - **Data Protection** — `PersistKeysToFileSystem` на путь из
      `DataProtection:KeysPath`/`DataProtection__KeysPath`, `SetApplicationName("BarberSearch")`,
      fail-fast, если путь не задан или каталог не пишется. Опционально ключи шифруются
      PKCS#12-сертификатом из секрет-стора. Dev-путь — `backend/.data-protection-keys`
      (git-ignored).
    - **Auth signing key** — `Auth:SigningKey` обязателен во всех окружениях и не генерируется;
      fail-fast при отсутствии/пустом/коротком (< 32) ключе. Добавлена ротация:
      `Auth:PreviousSigningKeys` принимаются при проверке, новые токены подписываются текущим;
      `kid` выводится из ключа. Семантика refresh/security stamp/ownership не менялась.
    - **Forwarded headers** — `UseForwardedHeaders` с `X-Forwarded-For` и `X-Forwarded-Proto`,
      только явные `KnownProxies`/`KnownNetworks`, middleware до HTTPS-redirect, rate limiter
      и auth. `X-Forwarded-Host` не включён.
    - **CORS/cookie** — явные `Cors:AllowedOrigins` без `*`, `AllowCredentials`; cross-site
      SPA — `SameSite=None` + `Secure` + `HttpOnly` + `Path=/`, same-site — `Lax`. Явно задан
      `Path=/` для auth/antiforgery cookie.
    - **Health checks** — `/health/live` (процесс) и `/health/ready` (PostgreSQL через
      `CanConnectAsync`), оба анонимные; readiness возвращает `503` при недоступной БД.
    - **Startup validation** — единый `StartupSettings.Load`: строка подключения, signing key,
      DP path, CORS, forwarded headers, SameSite, `AllowedHosts` (в Production — непустой список
      без `*`); без секретов в сообщениях. Graceful shutdown через `HostOptions.ShutdownTimeout`
      (`Deployment:ShutdownTimeoutSeconds`).
    - **Design-time factory** — `dotnet ef` больше не запускает `Program.cs`; читает
      `ConnectionStrings__Postgres`, поэтому миграции не требуют signing key и DP path.
    - **Смоук** — `deploy/smoke.ps1`: после R-47 в сценарии 104 проверки (+`allowed-hosts-wildcard`),
      но полный прогон на момент R-50 не повторялся; последние зелёные доказательства — 103
      проверки в `deploy/generated/smoke.txt` (git-ignored). Проверены cookie-вход, переживание
      рестарта, общий key ring двух инстансов, ротация/повтор refresh, logout, смена пароля,
      бронь, rate limit по XFF, игнорирование XFF недоверенного прокси, HTTPS-redirect, HSTS,
      CORS-preflight с credentials, атрибуты `__Host-`/`SameSite=None`, health endpoints и
      fail-fast на некорректных конфигурациях (теперь семь, включая wildcard `AllowedHosts`).
17. **R-36–R-40 (2026-10-24):** интеграционные тесты `tests/BarberSearch.IntegrationTests`
    (Testcontainers `postgres:18` + `WebApplicationFactory`, 6 сценариев: бронь/конфликт/отмена,
    keyset-поиск на границе рейтинга, `SetPrimarySalon`, конкурентные брони, refresh-reuse,
    health; позже добавлены host filtering (R-47), logout rate limit (R-48), атрибуты
    refresh-cookie (R-49) и уборка refresh-токенов (R-50) — всего 10 тестов);
    `Directory.Build.props` + `global.json` + `.editorconfig` + CI
    (`.github/workflows/backend-ci.yml`) + `Dockerfile`/`.dockerignore`; удалены дублирующие
    индексы `ix_barber_profiles_search` и `ix_barber_profiles_is_active_is_verified_rating`
    (остался `ix_barber_profiles_search_keyset`, миграция `DropRedundantBarberProfileIndexes`);
    `Failure.Errors` кэширует `ReadOnlyCollection`; `Error` — `init`-свойства (работает `with`),
    `ErrorType.NONE` удалён, маппинг `ToStatusCode` явный.
18. **R-41–R-50 (2026-10-25):** гигиена S7 и доступ. `Entity` реализует
    `IEquatable<Entity>` (R-41); 201 остаётся без `Location` — осознанное решение
    с доказательством по восьми create-эндпоинтам (R-42); `FindBarberProfileSalon` отвечает
    `BarberSalonResponse`, маппинг ушёл из контроллера в обработчик (R-43); удалён дублирующий
    маршрут `PUT …/photos/{photoId}/order` — порядок фото меняется только через агрегат
    (`…/sort-order`), `PortfolioPhoto.Reorder` стал `internal` (R-44, breaking change API);
    шаг сетки слотов по умолчанию вынесен в `Constants.DEFAULT_SLOT_DURATION_MINUTES` (R-45);
    строка подключения убрана из `appsettings.json` в user secrets/env (R-46);
    `AllowedHosts` без wildcard, Production fail-fast и `HostFilteringTests` (R-47); logout
    ограничен политикой `auth` (R-48); `Path=/` refresh-cookie сознательно принят из-за
    `__Host-`, `RefreshCookieTests` (R-49); фоновая уборка истёкших refresh-токенов,
    `ix_refresh_tokens_expires_at` (R-50). Эндпоинтов стало 81 (73 доменных + 8 auth).
    Тесты: 184 domain + 212 application + 71 infrastructure + 10 integration.
19. **R-51 (2026-10-25):** документация. `AGENT.md` сокращён с 209 135 до 170 583 байт
    (2442 → 2107 строк): история прогонов §5.3 заменена baseline 2026-10-25, §8 переписан в
    таблицу «слой → статус» (10 проектов, 0/0, 13 контроллеров / 81 endpoint, миграции/16 таблиц),
    §9.1–§9.12 свёрнуты в абзац (кроме §9.3/§9.7, на которые ссылается §15.2), добавлены
    оглавление и политика документации §13. Разбиение
    файла по слоям **сознательно отклонено**: ~70 ссылок `§N.M`, авто-загрузка агентом,
    §10/§13 не должны стать опциональными; XML-доки исходников не переписывались.
20. **R-52 (2026-10-25):** `GET api/reviews` требует `barberProfileId` уже на model binding.
    `[FromQuery, BindRequired]` в `ReviewsController.Get` отдаёт `400 ValidationProblemDetails`
    с ошибкой на поле `barberProfileId`, до обработчика запрос не доходит; явный пустой GUID
    по-прежнему отклоняет `GetReviewsHandler` доменным `review.getReviews.barberProfileId`
    (defense-in-depth, unit-тест сохранён). Новый `ReviewsBindingTests` (три запроса в одном
    сценарии) — red-green. Тесты: 184 domain + 212 application + 71 infrastructure +
    11 integration — зелёные; Debug/Release — 0/0. Детали — `CODE-REVIEW.md` R-52.
21. **R-53 (2026-10-25):** перенос `UseRateLimiter` выше auth **отклонён**: глобальный лимит
    записи партиционируется по claim `sub`, а до `UseAuthentication` его нет — лимит 30/мин на
    пользователя (R-22) стал бы общим на IP. Цена аутентификации отбрасываемых запросов принята.
    Порядок `UseAuthentication → UseAuthorization → UseRateLimiter` зафиксирован комментарием
    в `Program.cs`, XML-`<remarks>` `RateLimitSettings` и интеграционным `RateLimitOrderTests`
    (30 запросов A исчерпывают его квоту, 31-й — `429`, запрос B — `400` со своей партицией).
    Red-green: с лимитером выше auth тест падает на 27-м запросе A (`429`). Тесты: 184 + 212 +
    71 + 12 — зелёные; Debug/Release — 0/0. Детали — `CODE-REVIEW.md` R-53.
22. **R-54 (2026-10-25):** все **10** project-записей `BarberSearch.sln` переведены с legacy
    C# project type GUID `{FAE04EC0-…}` на SDK-style `{9A19103F-16F7-4668-BE54-9A1E7A4F7556}`;
    solution folder `tests` (`{2150E333-…}`) не тронут, project GUID'ы, пути, конфигурации
    и порядок строк прежние, кодировка та же (ASCII + CRLF, без BOM) — diff ровно 10/10.
    Вторая половина пункта, «`.http` с `/weatherforecast`», уже была неверна: в файле
    0 вхождений, R-52 добавил пример `GET api/reviews`; `.http` не менялся. `dotnet sln list` —
    10 проектов, Debug/Release 0/0, тесты 184 + 212 + 71 + 12 — зелёные.
    Детали — `CODE-REVIEW.md` R-54.

### 0.1. Состояние git и верификация 2026-10-26

До 2026-10-26 срез R-01–R-54 существовал только в рабочем дереве: reflog `features/issue-6`
не содержал ни одного коммита, `origin` стоял на merge PR #5. Теперь он зафиксирован
девятью коммитами (в порядке применения):

| Коммит | Содержимое |
|---|---|
| `4663ee6` | `chore(build)`: `Directory.Build.props`, `global.json`, `.editorconfig`, CI, `Dockerfile`, `.dockerignore`, правка `.sln` (R-37, R-54) |
| `7c7083c` | `refactor(shared,domain)`: `Failure`/`Error`, `UrlValidator`, валидация входа, `Entity : IEquatable` (R-10, R-23, R-24, R-39–R-41) |
| `576cda3` | `feat(application)`: `ICurrentUser`, ownership, идемпотентность, модерация, политика пароля, `ApiErrorCodes` (R-05…R-35) |
| `622125a` | `feat(infrastructure)`: 8 миграций, `audit_log`, `xmin`, узкие чтения, `lock_timeout` (R-05, R-11, R-12, R-16, R-19, R-34) |
| `6c2e7ad` | `feat(web)`: auth/security stamp, rate limits, ProblemDetails, сжатие и таймауты, startup validation, уборка refresh-токенов (R-13, R-14, R-21…R-29, R-32, R-35, R-45…R-50) |
| `1055420` | `test(integration)`: Testcontainers-набор, 12 сценариев (R-36) |
| `ece7c58` | `test(deploy,load)`: `smoke.ps1`, `find-dangling-references.sql`, harness (R-47, R-50) |
| `0024b46` | `docs`: `CODE-REVIEW.md`, `AGENT.md`, этот файл, `DEPLOY.md`, `LOADTEST.md` (R-51, R-52) |
| `46b63d3` | `fix(load)`: сиды пишут `failed_login_attempts` |

Проверено 2026-10-26 на этом HEAD:

- сборка `BarberSearch.sln` — 10 проектов, **Debug и Release: 0 ошибок / 0 предупреждений**
  (`CI=true`, то есть с `ContinuousIntegrationBuild`);
- тесты — **184 domain + 212 application + 71 infrastructure + 12 integration**, все зелёные
  в Debug и в Release (`-p:BaseOutputPath` в `%TEMP%`, см. §3);
- `dotnet ef migrations has-pending-model-changes` — «No changes have been made to the model
  since the last migration»;
- `deploy/smoke.ps1` — **PASS, 104 проверки**: миграции на чистой БД, cookie/JWT, ротация
  refresh, rate limit по `XFF`, health checks и семь fail-fast сценариев.

Найдено и исправлено этой верификацией: `load/seed/minimal.sql` и `load/seed/seed.sql` не
заполняли `failed_login_attempts`, который после `AddUserLockout` (R-21) стал `NOT NULL` без
default — смоук падал на `FAIL minimal seed failed`. Вывод: полный прогон смоука нужно
повторять после каждого среза, меняющего схему, — он ловит расхождение сидов со схемой, а
`dotnet test` этого не видит.

Локальная БД `barbersearch` отстаёт на 8 миграций (применено 7 из 15 на 2026-10-26): перед
локальным запуском нужен `dotnet ef database update` (§3). Смоук и интеграционные тесты это
не затрагивает — они работают на своих базах. База load-тестов `barbersearch_load` отстаёт
сильнее (5 из 15): перед прогоном `load/run.ps1` её нужно пересоздать и засеять заново по
`load/README.md` — старый сид от 2026-10-20 не знает `failed_login_attempts` (тот же дефект,
что исправлен в `fix(load)`).

## 1. Следующая задача — фактический выкат

1. **Выкат по `DEPLOY.md`:** секрет-стор (`Auth__SigningKey`, `ConnectionStrings__Postgres`,
   `DataProtection__CertificatePassword` при шифровании), постоянный том для key ring,
   `ForwardedHeaders` с адресами своих прокси, `Cors:AllowedOrigins` только с реальными
   origin'ами, `Auth__CookieSameSite` по схеме SPA, probes на `/health/live`/`/health/ready`,
   `terminationGracePeriodSeconds` больше `ShutdownTimeoutSeconds`.
2. **Booking headroom (опционально, отдельно):** contended p95 257.5–304.0 ms при бюджете 300;
   один прогон вышел на 4 ms. Причина — advisory lock, а не план. Следующий шаг — slot-claim
   (уникальная строка/индекс) или fast-path, а не кэш. Поведение текущего correctness-fix
   не переделывать без отдельного среза и регрессии.
3. **Кэш** — не начинать: измерения 2026-10-20 не дают основания. Search проходит с запасом
   ≈5×, слоты и review-write имеют запас, booking ограничен корректностью. Если будущие
   измерения всё же потребуют кэша, это отдельный срез с инвалидацией рейтинга.

## 2. Открытые дефекты и техдолг

Закрыто 2026-10-21 (R-01, R-02 из `CODE-REVIEW.md`): `Salon.TimeZoneId` проверяется при
создании/смене адреса (`salon.timeZoneId.unknown`), неразрешимый пояс на чтении слотов даёт
`409 barberSlots.timeZone.*` и error-лог вместо `500`. Ссылочная целостность: узкие
`ExistsAsync` в обработчиках салона/услуги/специализации/избранного и проверка пары
`(barber_profile_id, salon_id)` в `CreateBooking`; миграция `AddCrossAggregateForeignKeys`
добавила пять `NOT VALID` FK, исторические висячие строки ищет
`deploy/find-dangling-references.sql`. Тесты: 142 domain + 110 application.

Закрыто 2026-10-21 (R-03): email нормализуется в домене (`Trim().ToLowerInvariant()`),
проверяется через `MailAddress.TryCreate` с границами 3..254 и доменом с точкой;
`users.email` переведён в `citext` (регистронезависимая уникальность) с check-ограничением
длины, миграция `NormalizeUserEmail` приводит исторические строки к `lower(btrim(email))`
и останавливается с понятным сообщением при дублях; `UserRepository` нормализует входной
адрес. Тесты: 147 domain + 113 application.

Закрыто 2026-10-22 (R-04): у создания брони появилось окно допустимости.
`CreateBookingHandler` (`TimeProvider`) отвечает `400 booking.create.startAt.inPast`, если
`StartAt` раньше «сейчас − 5 минут», и `400 booking.create.startAt.tooFar`, если позже
«сейчас + 90 дней»; проверка стоит после ключа идемпотентности, поэтому повтор по ключу
возвращает бронь и после её начала. `Booking.Create` ограничивает длительность 1440 минутами
(`400 booking.create.period.tooLong`). Константы — `BOOKING_HORIZON_DAYS`,
`BOOKING_START_TOLERANCE_MINUTES`, `MAX_BOOKING_DURATION_MINUTES` в `Domain/Constants.cs`;
`deploy/smoke.ps1` бронирует 30–79 дней вперёд. Контракт не менялся, `EndAt` по-прежнему
приходит от клиента, а сверка его длительности с `BarberService.DurationMinutes` — отдельный
срез (§2.3). Тесты: 149 domain + 118 application.

Закрыто 2026-10-22 (R-05): у всех 14 сущностей агрегатов появился токен конкурентности
`xmin` (семь корней + семь дочерних строк; корневого токена мало, потому что `UPDATE` ребёнка
не трогает корень). `RepositoryBase.SaveChangesAsync` маппит `DbUpdateConcurrencyException`
в `409 {errorCode}.concurrencyConflict` (`FailureProblemDetails` уже умеет 409), а
`GetForRatingUpdateAsync` читает `xmin` явно, иначе `SELECT * … FOR UPDATE` не отдавал
системную колонку. Миграция `AddOptimisticConcurrency` по DDL пустая — `xmin` существует в
каждой таблице PostgreSQL; меняется только snapshot модели. `RefreshToken` без токена:
его гонку закрыл R-07 (2026-10-23, §2.2). Новый проект `tests/BarberSearch.Infrastructure.UnitTests` — 3 теста
(модель, исключение `RefreshToken`, маппинг 409); `BookingConcurrencyTests` — 2 теста на
`Cancel`/`Complete` по устаревшему снимку. Реальная гонка двух `DbContext` проверена на
локальной PostgreSQL 16.3 и на пустой БД со всеми миграциями: первый `SaveChanges` проходит,
второй получает `DbUpdateConcurrencyException`, репозиторий отдаёт
`409 booking.update.concurrencyConflict`; `GetForRatingUpdateAsync` + `UpdateRating`
сохраняются штатно. Тесты: 149 domain + 120 application + 3 infrastructure.

Закрыто 2026-10-22 (R-06): частичный уникальный индекс `ux_barber_salons_primary_per_profile`
заменён миграцией `DeferrablePrimarySalonConstraint` на частичное exclusion-ограничение
`ex_barber_salons_primary_per_profile` (`EXCLUDE USING btree (barber_profile_id WITH =)
WHERE (is_primary) DEFERRABLE INITIALLY DEFERRED`). `UNIQUE … DEFERRABLE` невозможен: PostgreSQL
не поддерживает частичные UNIQUE-ограничения, а `DEFERRABLE` есть только у constraint.
`SetBarberProfilePrimarySalonHandler` и домен не менялись — `SaveChanges` остался один, а
ограничение проверяется в `COMMIT`. EF-модель больше не объявляет этот индекс (частичное
exclusion-ограничение в ней не выражается), DDL — в миграции, `Down` возвращает старый индекс.
Отложенная проверка срабатывает в `COMMIT`, где Npgsql бросает `PostgresException` напрямую
(не `DbUpdateException`), поэтому `RepositoryBase.SaveChangesAsync` ловит и её и отдаёт
`409 {errorCode}.overlap`; это воспроизводит новый infrastructure-тест без базы.
Проверено на локальной PostgreSQL 16: переключение основного салона через реальный обработчик и
EF (A → B → A) проходит трижды; `AddBarberProfileSalon` с `IsPrimary = true` в `IUnitOfWork`
вставляет новый салон и снимает старый флаг; со старым индексом третье переключение падает
`barberProfile.update.uniqueViolation` (409) — воспроизведение дефекта до правки.
Тесты: 149 domain + 120 application + 4 infrastructure.

Закрыто 2026-10-23 (R-08): смена пароля, смена роли и деактивация пользователя пишут агрегат
и отзывают все refresh-токены в одной транзакции `IUnitOfWork.ExecuteAsync` (PBKDF2 — до
открытия транзакции). Если отзыв вернул `Failure` или бросил исключение, откатываются и
`PasswordHash`/`Role`/`IsActive`, и `security_stamp`, поэтому смена пароля «из-за кражи» не
может оставить живую refresh-сессию. Тесты: 125 application (+4: `FakeUnitOfWork` со снимками
`FakeUserRepository`/`FakeRefreshTokenRepository` моделирует откат); на локальной PostgreSQL 16
проверены успешный коммит, откат при таймауте отзыва и откат смены роли/деактивации.

Закрыто 2026-10-23 (R-09): ключ идемпотентности нормализуется и проверяется до запроса в
репозиторий. Домен получил `Booking.NormalizeIdempotencyKey` (trim) и
`Booking.ValidateIdempotencyKey` (пустой → `booking.create.idempotencyKey`, длиннее 128 →
`booking.create.idempotencyKey.tooLong`); `Booking.Create` использует их же и хранит
нормализованный ключ. `CreateBookingHandler` больше не возвращает клиенту инфраструктурный код
`booking.getIdempotencyKey.idempotencyKey`: пустой/пробельный или слишком длинный ключ — это
`400` с доменным кодом и без похода в БД. Тесты: 150 domain (+1) + 128 application (+3).

Закрыто 2026-10-23 (R-10): правила пароля вынесены в `IPasswordPolicy`/`PasswordPolicy`
(Application, singleton): 12..128 символов, встроенный словарь SecLists top-10k (embedded
resource, регистронезависимо), запрет совпадения с email/local-part/именем/фамилией. Проверка
идёт до PBKDF2 в `register`, admin `POST api/users` и `change-password`; в `login` добавлен
только потолок 128 без композиционных правил. Коды — `auth.password.*`; старый
`auth.changePassword.newPassword` унифицирован в `auth.password.required`. Тесты:
150 domain + 146 application (+18) + 4 infrastructure.

Закрыто 2026-10-23 (R-11): `IUserRepository.GetSecurityStampAsync` читает только
`is_active, security_stamp` (`AsNoTracking`, без `Include(FavoriteBarbers)` и без агрегата);
проверка claim'а вынесена в `ValidateSecurityStampQuery`/Handler, Web `SecurityStampValidator`
стал тонким адаптером. Round-trip в БД сохранён осознанно — немедленный отзыв важнее экономии;
кэш/TTL и проверка «раз в N минут» отклонены. Тесты: 150 domain + 151 application (+5) +
4 infrastructure.

Закрыто 2026-10-23 (R-13): в `appsettings.json` EF Core переведён в `Warning`
(`Microsoft.EntityFrameworkCore` и `.Database.Command`), поэтому SQL-команды с параметрами —
email, `password_hash`, idempotency-ключи, тексты отзывов — больше не попадают в прод-лог;
`Default` и `Microsoft.AspNetCore` не менялись. В `appsettings.Development.json` обе категории
явно возвращены в `Information`; `EnableSensitiveDataLogging` и принудительных фильтров в коде
нет. Новый `LoggingConfigurationTests` (2 теста) читает сами файлы веб-слоя; тесты:
150 domain + 151 application + 6 infrastructure.

Закрыто 2026-10-23 (R-14): включено сжатие ответов и 30-секундный таймаут запроса.
`UseResponseCompression` стоит сразу после forwarded headers и до `UseExceptionHandler`,
чтобы сжимались и ProblemDetails, и Swagger UI; allowlist точный — JSON,
`application/problem+json`, XML, JavaScript/CSS и текст, без изображений/шрифтов/архивов,
`EnableForHttps = true`. `UseRequestTimeouts` покрывает бизнес-путь, а health-эндпоинты
помечены `WithRequestTimeout(Timeout.InfiniteTimeSpan)`: readiness отдаёт `503` по своему
таймауту БД, а не `504`. Новые тесты — `HttpPipelineTests` (4), всего
150 domain + 151 application + 10 infrastructure; вручную проверены health, `br`-сжатие
`/openapi/v1.json` и отсутствие сжатия PNG.

Закрыто 2026-10-23 (R-12): полный агрегат профиля больше не грузится ни на одном write-пути.
`IBarberProfileRepository` получил узкие чтения (`GetRootByIdAsync`, `GetWithSalonsAsync`,
`GetWithSalonsAndSchedulesAsync`, `GetWithServicesAsync`, `GetWithPhotosAsync`,
`GetWithSpecializationsAsync`, `GetWithTimeOffsAsync`, `GetOwnerIdAsync`, `GetSalonAsync`);
`GetByIdAsync`/`GetByUserIdAsync` и `FullAggregate()` удалены, 31 write-обработчик и
`FindBarberProfileSalon` переведены на чтение ровно нужной коллекции. Коды/семантика
404/403/409 и `xmin` не менялись; изменения по-прежнему идут через `UpdateAsync`. Тесты:
150 domain + 165 application (+14) + 10 infrastructure; на локальной PostgreSQL 16.3
подтверждено по одному SQL на каждое узкое чтение и сохранение без затрагивания
незагруженных коллекций.

Закрыто 2026-10-23 (R-15): создание брони больше не открывает транзакцию в репозитории.
`BookingRepository.AddAsync` — только `Add` + `SaveChanges` + отцепление брони при `Failure`
или исключении; транзакционная граница принадлежит `IUnitOfWork`. Advisory lock вынесен
в узкий `IBookingRepository.AcquireBarberLockAsync`, и `CreateBookingHandler` берёт его первым
действием внутри `IUnitOfWork.ExecuteAsync` — до проверок ссылок; блокировка живёт до коммита,
а вложенной транзакции нет. Коды `409` (`overlap`/`uniqueViolation`) не менялись. Тесты:
150 domain + 168 application (+3) + 12 infrastructure (+2); на локальной PostgreSQL 16
в отдельной БД проверены отсутствие раннего коммита, удержание lock до коммита, откат при
overlap/повторе ключа и при исключении после вставки, после чего БД удалена.

Закрыто 2026-10-23 (R-16): отзыв refresh-токенов сырым SQL больше не оставляет в change tracker
устаревшие копии с `RevokedAt = null`. `RepositoryBase.DetachTracked` снимает только сущности
своего типа: `RevokeChainAsync` — все отслеживаемые токены (состав цепочки в памяти неизвестен),
`RevokeAllForUserAsync` — только токены пользователя; `RotateAsync` не менялся, его собственное
отцепление `current` сохранено. Тесты: 150 domain + 168 application + 14 infrastructure (+2,
`RefreshTokenTrackerTests`); на локальной PostgreSQL 16 в `barbersearch_verify_R16` подтверждено,
что повторный `SaveChanges` не возвращает `revoked_at` в `NULL` (без очистки дефект
воспроизводится), БД удалена.

Закрыто 2026-10-23 (R-17): неудачный `SaveChanges` больше не оставляет сущности в change tracker.
`RepositoryBase.SaveChangesAsync` при маппируемой ошибке (`DbUpdateException` /
`DbUpdateConcurrencyException` / `PostgresException`) очищает трекер через `ChangeTracker.Clear()`
и только затем возвращает `Failure`; контракт «после `Failure` контекст пуст, агрегат нужно
перечитать» зафиксирован в XML-доке. Коды ошибок не менялись, явное отцепление брони в
`BookingRepository.AddAsync` осталось для немаппируемых исключений. Тесты: 150 domain +
168 application + 17 infrastructure (+3, `RepositorySaveFailureTests`); на локальной
PostgreSQL 16 в `barbersearch_verify_R17` дубликат citext-имени внутри `IUnitOfWork` дал
`specialization.add.uniqueViolation`, трекер опустел, транзакция откатилась, следующая запись
в том же контексте прошла; БД удалена.

Закрыто 2026-10-23 (R-18): ожидание advisory-блокировки барбера ограничено 2 секундами.
`BookingRepository.AcquireBarberLockAsync` перед `pg_advisory_xact_lock` выполняет
`SET LOCAL lock_timeout = '2s'` в транзакции `IUnitOfWork`, а SQLSTATE `55P03` маппится в
`409 booking.add.lockTimeout` (общий маппинг `RepositoryBase`); обработчик логирует warning и
не доходит до проверок ссылок и вставки. Для команд Npgsql задан `CommandTimeout(30)` в
`UseNpgsql`, readiness с `DatabaseTimeoutSeconds` (5 с) отменяется раньше и не конфликтует.
Тесты: 150 domain + 169 application (+1) + 19 infrastructure (+2); на локальной PostgreSQL 16
в `barbersearch_verify_R18` проверены истечение `lock_timeout` под удерживаемой блокировкой,
откат и успешное взятие после освобождения, БД удалена.

Закрыто 2026-10-23 (R-19): сводка рейтинга читается одним агрегирующим запросом.
`ReviewRepository.GetRatingSummaryAsync` больше не делает `CountAsync` + `AverageAsync`, а получает
`COUNT` и `AVG` одним SQL; пустая выборка даёт `(null, 0)`, округление остаётся в
`BarberProfile.UpdateRating`. Под блокировкой профиля теперь три команды вместо четырёх. Тесты:
150 domain + 171 application (+2) + 20 infrastructure (+1); на локальной PostgreSQL 16 в
`barbersearch_verify_R19` подтверждены один SQL на сводку (4.666…/3 и `null`/0 для пустого
набора) и три команды полного пересчёта, БД удалена.

Закрыто 2026-10-23 (R-20): контракт услуг разведён по сущностям. В поиске и слотах параметр
`serviceId` переименован в `hairServiceId` (это `hair_services.id`), в брони остался
`barberServiceId` (`barber_services.id`); ответ слотов аддитивно получил `barberServiceId` и
`hairServiceId` — пару, по которой посчитана `durationMinutes` (`null` без фильтра), а в карточке
безликое `id` услуги переименовано в `barberServiceId`. Это breaking change контракта: обновлены
`BarberSearch.Web.http` и load harness, `deploy/smoke.ps1` эти эндпоинты не вызывал. Тесты:
150 domain + 172 application (+1) + 21 infrastructure (+1); на локальной PostgreSQL 16 в
`barbersearch_verify_R20` проверены ids и длительность 45 минут у слота, карточка и поиск по
`hairServiceId`, неизвестная услуга — 404/0; БД удалена.

Закрыто 2026-10-23 (R-21): вход защищён на уровне аккаунта, а не только по IP. У `User` появились
`FailedLoginAttempts`, `LastFailedLoginAt`, `LockoutEnd`, `IsLockedOut(now)` (строго
`LockoutEnd > now`), `RegisterFailedLogin(now)` (пауза > 15 минут начинает счёт заново; счётчик
ограничен `MAX + 10`; метод всегда успешен) и идемпотентный `ResetFailedLogin()`. Константы —
`MAX_FAILED_LOGIN_ATTEMPTS = 5`, `LOGIN_FAILURE_RESET_MINUTES = 15`,
`LOGIN_LOCKOUT_BASE_MINUTES = 1`, `LOGIN_LOCKOUT_MAX_MINUTES = 60`; задержка
`1 * 2^(attempts − 5)` минут с потолком 60 (5-я — 1 мин, 6-я — 2, 7-я — 4, …, 11-я — 60).
Миграция `AddUserLockout`: `users.failed_login_attempts` (`integer NOT NULL DEFAULT 0`, default
снят), `users.last_failed_login_at`/`users.lockout_end` (`timestamptz NULL`), check
`ck_users_failed_login_attempts`. `LoginUserHandler` берёт «сейчас» из `TimeProvider`: неверный
пароль на незаблокированном аккаунте инкрементирует счётчик и сохраняет (ошибку сохранения только
логирует, клиенту всё равно 401 `auth.login.invalidCredentials`); неверный пароль на
заблокированном — тот же 401 без роста счётчика и продления блокировки; верный пароль на
заблокированном — `401 auth.login.lockedOut` (код в `ApiErrorCodes.Auth.LockedOut`) без записи
входа; верный на незаблокированном — перехеширование при необходимости, `ResetFailedLogin()`,
`RecordLogin()`, `UpdateAsync`. `auth.login.lockedOut` отдаётся только при верном пароле, поэтому
enumeration-атакой существование адреса не выяснить. Уведомление владельцу не реализовано — в
проекте нет канала уведомлений; rate limit по `email` не добавлялся — распределённый перебор
останавливает блокировка аккаунта, лимит по IP остаётся (R-22/R-25 — отдельно). Тесты:
181 domain (+7) + 196 application (+5, `LoginLockoutTests`) + 33 infrastructure; при временно
убранной проверке `IsLockedOut` падает ровно тест блокировки (4 passed, 1 failed) — проверено и
восстановлено. На локальной PostgreSQL 16.3 в `barbersearch_verify_R21` (11 миграций) реальные
`UserRepository`/`UnitOfWork`/`LoginUserHandler`: 5 неверных паролей → `failed_login_attempts = 5`,
`lockout_end` заполнен, `last_login_at` пуст; верный пароль → `401 auth.login.lockedOut` без
изменения строки; после SQL-правки `lockout_end` в прошлое верный пароль прошёл, счётчик и
`lockout_end`/`last_failed_login_at` обнулены, `last_login_at` заполнен; БД и временный проект
удалены. Сборка — 0 ошибок, 0 предупреждений.

Закрыто 2026-10-23 (R-30): после отката `IUnitOfWork` change tracker больше не оставляет
изменённые сущности. `UnitOfWork.ExecuteCoreAsync` держит флаг `committed` и очищает трекер в
`finally` при `Failure` и при исключении из операции/коммита; при успешном коммите трекер не
трогается. При исключении явного второго отката нет: транзакция откатывается при dispose внутри
`await using`, после чего `finally` очищает трекер, а исключение уходит наружу; `IUnitOfWork` и
публичные контракты не менялись. Тесты: 150 domain + 172 application + 24 infrastructure (+3,
`UnitOfWorkTrackerTests`; фейковый `IDbContextTransactionManager` через `ReplaceService`, без
PostgreSQL). На локальной PostgreSQL 16 в `barbersearch_verify_R30` операция с добавлением брони
и `Failure` оставила 0 строк в трекере и 0 строк в БД, следующая операция в том же контексте
закоммитила ровно одну другую бронь; БД удалена.

Закрыто 2026-10-23 (R-29): `ICurrentUser` больше не бросает на анонимном запросе. Бросающее
свойство `Guid UserId` заменено на `bool TryGetUserId(out Guid userId)`; `HttpContextCurrentUser`
без `HttpContext`/claim'а `sub` (или с непарсящимся claim'ом) возвращает `false`, а не исключение.
В Application добавлен `RequireUserId()` → `Result<Guid, Failure>` с `401 auth.unauthorized`;
`EnsureCanManage`/`EnsureIsBarberOfAsync` для анонима тоже дают `401`, для владельца/админа
поведение не изменилось. Прямые чтения `_currentUser.UserId` заменены на `RequireUserId()` в
`CreateBooking`, `CancelBooking`, `CreateReview`, `CreateBarberProfile` и `GetBookings`; публичные
HTTP-контракты и коды существующих сценариев не менялись. `FakeCurrentUser.Anonymous()` создаёт
анонимное состояние. Тесты: 150 domain + 178 application (+6,
`CurrentUserAuthorizationTests`) + 27 infrastructure (+3, `HttpContextCurrentUserTests`); без
правки новые тесты падают (проверено временным возвратом броска и подстановкой `Guid.Empty`).
Сборка — 0 ошибок, 0 предупреждений.

Закрыто 2026-10-23 (R-26): создание профиля барбера сразу привязывает его к текущему
пользователю. `CreateBarberProfileHandler` в одной транзакции `IUnitOfWork` загружает `User`,
проверяет отсутствие профиля (`409 barberProfile.create.alreadyExists`, без опоры на
`ux_barber_profiles_user_id`), сохраняет профиль, вызывает `User.LinkBarberProfile` и
`UpdateAsync`; `Failure` любого шага откатывает и профиль, и привязку. Идентификатор берётся
из `RequireUserId()` (R-29, аноним → `401 auth.unauthorized`), публичный HTTP-контракт
(`201` + id профиля) не менялся. Связь в БД (`users.barber_profile_id` ↔
`barber_profiles.user_id`) заполняется сразу, но claim `barber_profile_id` и `/api/auth/me`
для уже выданного JWT/cookie обновятся только при следующем входе или refresh — это
ограничение JWT, не дефект. Тесты: 150 domain + 183 application (+5,
`CreateBarberProfileTests`) + 27 infrastructure; на локальной PostgreSQL 16 в
`barbersearch_verify_R26` проверены привязка с обеих сторон, `409` на повтор и откат профиля
при принудительном `Failure` в `UpdateAsync`; БД удалена.

Закрыто 2026-10-23 (R-27): политика часов зафиксирована. `JwtTokenIssuer` и
`MarkBookingNoShowHandler` получают `TimeProvider` в конструктор (DI регистрирует singleton),
`AuthController` — через `[FromServices]`; оттуда считаются `exp` и `iat` access-токена,
`ExpiresUtc` cookie входа и refresh, остаток refresh-срока в `TokenResponse` и сравнение
`StartAt` с «сейчас». Домен (`Entity.CreatedAt/UpdatedAt`, `User.RecordLogin`) сознательно
остался на системных часах — это технические метки; политика записана в XML-комментариях и
`AGENT.md` §7.6. Тесты: 150 domain + 186 application (+3, `MarkBookingNoShowTests`) +
29 infrastructure (+2, `JwtTokenIssuerTests`); при возврате `DateTime.UtcNow` падают оба
JWT-теста, при возврате `DateTimeOffset.UtcNow` в обработчик — два теста неявки (проверено и
восстановлено). Сборка — 0 ошибок, 0 предупреждений.

Закрыто 2026-10-23 (R-28): публичные коды ошибок вынесены в
`BarberSearch.Contracts/Common/ApiErrorCodes` (`Auth`, `Booking`, `BarberProfile`, `User`,
`Salon`, `HairService`, `Specialization`, `Review`, `RefreshToken`, `Common`); для ошибок
сохранения — префиксы операций и фабрики суффиксов (`uniqueViolation`, `overlap`, `deadlock`,
`lockTimeout`, `foreignKeyViolation`, `checkViolation`, `notNullViolation`,
`concurrencyConflict`), `internal.database` — единственный внутренний код для 500.
`RepositoryBase`, 8 репозиториев, `CurrentUserExtensions`, `GetBookingsHandler`,
`CreateReviewHandler` и фейки используют константы; из публичных кодов убраны `getById`,
`getByEmail`, `getByHash`, `getCardById`, `getIdempotencyKey`, `getPaged`, `getPage`,
`getPublishedPage`, `getByCity`, `getByCategory`, `getByName`, `emailExists`,
`getActiveByBarber`, `hasActiveOverlap`: бронь/профиль/пользователь/отзыв/салон/услуга/
специализация теперь дают `{aggregate}.notFound`, пагинация —
`{aggregate}.paging.page` / `{aggregate}.paging.pageSize`, валидация — `*.invalid`,
`booking.page.limit`, `review.page.limit`, `booking.activePeriod.invalid`,
`booking.overlap.period`. `CreateBarberProfileHandler`
маппит `barberProfile.add.uniqueViolation` в тот же `409 barberProfile.create.alreadyExists`,
что и предполётная проверка. Это сознательный breaking change контракта перед продым.
Тесты: 174 domain + 191 application (+5: 4 `ApiErrorCodesTests` и 1 на гонку create) +
33 infrastructure (+1, `internal.database`); негативный прогон с временным
`booking.getById.notFound` даёт 1 failed, после восстановления — 191/191. Сборка — 0 ошибок,
0 предупреждений.

Закрыто 2026-10-23 (R-23): enum'ы в JSON больше не принимаются числами, а коллекции `Language` и
`PaymentMethod` проверяются доменом. `JsonStringEnumConverter(allowIntegerValues: false)` вынесен
в `Web/Common/JsonSettings.Configure` и вызывается из `AddJsonOptions`; `"paymentMethods": [999]`
отклоняется с `JsonException` до обработчика, строковые имена работают как прежде.
`BarberProfile.Create`/`UpdateProfile` проверяют каждый элемент через `Enum.IsDefined` в общей
`ValidateInput`: коды `barberProfile.languages.unknown` / `barberProfile.paymentMethods.unknown`,
`InvalidField` — `languages` / `paymentMethods`, ошибки накапливаются вместе с остальными, а
проверка стоит до мутации, поэтому неизвестное значение не попадает в агрегат. `null`-список
по-прежнему допустим (`Create` → `[Language.Ru]`), пустой — тоже. Схема БД не менялась и DB-check
на массивы не добавлялся: прямой SQL в обход API такие числа всё ещё может записать.
Тесты: 158 domain (+8) + 186 application + 32 infrastructure (+3, `JsonSettingsTests`); с временно
возвращённым parameterless-конвертером JSON-тест падает (1 failed / 31 passed), после
восстановления 32/32. Сборка — 0 ошибок, 0 предупреждений.

Закрыто 2026-10-23 (R-24): URL-поля домена валидируются одним `Domain/Common/UrlValidator`
(internal static). Значение тримится; пустое необязательное поле становится `null`; проверяются
абсолютный URI, схема только `http`/`https` (без учёта регистра), непустой хост, запрет
`userinfo` и длина `<= Constants.MAX_URL_LENGTH` (2048). Валидатор возвращает `Error?` с
переданными кодом и `InvalidField`, агрегаты копят ошибки в `DomainErrors.Collect()`.
Применено в `BarberProfile.AddPhoto` (`barberProfile.addPhoto.url` — обязателен,
`.thumbnailUrl`, `.description` <= 2000), `PortfolioPhoto.UpdateDescription`
(`portfolioPhoto.updateDescription.description`), `Salon.Create`/`UpdateContactInfo`
(`salon.websiteUrl`) и `User.UpdateProfile` (`user.updateProfile.avatarUrl`); url/thumbnail/
website/avatar хранятся обрезанными, `description` не тримится. Схема: `HasMaxLength(2048)` у
`portfolio_photos.url`, `thumbnail_url`, `salons.website_url`, `users.avatar_url` и
`HasMaxLength(2000)` у `portfolio_photos.description`; миграция `AddUrlLengthLimits`
(`text` → `varchar(n)`, `Down` — `text`) проверена на PostgreSQL 16.3 и на контейнерной
PostgreSQL 18 в `barbersearch_verify_R24`: пять колонок получили ожидаемые длины, URL 2048
вставляется, 2049 отклоняется `value too long for type character varying(2048)`,
`description` 2001 — `character varying(2000)`, check-ограничения на месте. Тесты: 174 domain
(+16) + 186 application + 32 infrastructure; с отключённым валидатором падают 2 новых теста.
Сборка — 0 ошибок, 0 предупреждений.

Закрыто 2026-10-23 (R-22): запись ограничена 30 запросами в минуту, а не только auth. Настройка
вынесена в `Web/Deployment/RateLimitSettings` (`Configure`, `AuthPartition`/`WritePartition`,
`AuthPartitionKey`/`WritePartitionKey`, `IsWriteMethod`, `OnRejected`), `Program.cs` вызывает
`AddRateLimiter(RateLimitSettings.Configure)`. Политика `auth` не изменилась — 10/мин на IP из
`Connection.RemoteIpAddress` после `UseForwardedHeaders`. Запись закрыта глобальным
`PartitionedRateLimiter`, без атрибутов на ~60 действиях: 30/мин на claim `sub`, без него на IP,
без IP — `unknown`; окно 1 минута, `QueueLimit = 0`; `GET`/`HEAD`/`OPTIONS`/`TRACE` получают
`GetNoLimiter`. `UseRateLimiter` стоит после `UseAuthentication`/`UseAuthorization`, поэтому `sub`
доступен. Ответ `429` — `ProblemDetails` с `Retry-After` (секунды до сброса fixed window) и
`errorCodes: ["rateLimit.exceeded"]` (`ApiErrorCodes.Common.RateLimitExceeded`), а не пустое тело.
На auth-эндпоинтах действуют оба лимита, побеждает более строгий 10/мин. Тесты: 181 domain +
196 application + 40 infrastructure (+7, `RateLimitSettingsTests`); негативный прогон с временно
убранным `options.OnRejected = OnRejected` — 1 failed / 6 passed, после восстановления 40/40.
Ручной HTTP-прогон не выполнялся; сборка — 0 ошибок, 0 предупреждений.

Закрыто 2026-10-23 (R-25): витрина читается анонимно. `[AllowAnonymous]` и
`[EnableRateLimiting("reads")]` стоят на `GET api/barbers`, `GET api/barbers/{id}`,
`GET api/barbers/{id}/slots` и `GET api/reviews`; `GET api/bookings`, создание/правка отзывов,
публикация/скрытие и все write-эндпоинты остались закрыты (fallback-политика и `AdminOnly`
не менялись). В `Web/Deployment/RateLimitSettings` добавлена политика `reads` — 120 запросов
в минуту на `RemoteIpAddress` после `UseForwardedHeaders`, окно 1 минута, `QueueLimit = 0`,
без `sub`; зарегистрирована в `Configure` рядом с `auth`, отказ — прежний `429` ProblemDetails
с `Retry-After`. `AuthPartitionKey` делегирует новому `IpPartitionKey`, поведение `auth` не
изменилось. PII в ответах не появилось: поиск/карточка/слоты фильтруют активных и
подтверждённых барберов и активные салоны, отзывы — только опубликованные. `Cache-Control`
сознательно не добавлялся: слоты меняются после создания брони, CDN/edge-кэша нет; с edge-кэшем
это отдельный срез с инвалидацией по броням. Тесты: infrastructure 40 → **43**
(`AnonymousReadEndpointTests` — metadata четырёх анонимных маршрутов через собранный
`WebApplication`, `GET api/bookings`/`POST api/reviews` без `IAllowAnonymous`; `RateLimitSettingsTests`
— 120 проходят, 121-й отклоняется, другой IP не затронут). `EnableRateLimitingAttribute` виден
в endpoint metadata MVC, рефлексия не понадобилась. Негативный прогон: без `[AllowAnonymous]`
у `Get` падает metadata-тест (1 failed / 1 passed), после восстановления 43/43. Domain 181 и
Application 196 не менялись; сборка — 0 ошибок, 0 предупреждений; ручной HTTP-прогон не
выполнялся.

Закрыто 2026-10-24 (R-31): `JsonWebTokenHandler` больше не создаётся на каждый токен. В
`Web/Auth/JwtTokenIssuer.cs` поле `private static readonly JsonWebTokenHandler Handler = new();`,
`Issue` вызывает `Handler.CreateToken(descriptor)`; обработчик потокобезопасен, поэтому один на
процесс. Claims, `TimeProvider`, подпись, сроки и `TokenResponse` не менялись. Тест —
`JwtTokenIssuerTests.Handler_IsSingleStaticReadonlyInstance` (reflection: ровно одно приватное
статическое поле типа `JsonWebTokenHandler`, `IsStatic`/`IsInitOnly`, значение не `null`); с
временно возвращённым экземплярным полем — 1 failed / 43 passed, после восстановления 44/44.
Тесты: 181 domain + 196 application + 44 infrastructure (+1); сборка — 0 ошибок, 0 предупреждений.

Закрыто 2026-10-24 (R-32): глубина offset-пагинации не больше 10 000, keyset не вводился. В
`RepositoryBase` добавлены `internal const int MAX_PAGE_OFFSET = 10_000` и
`internal static Failure? ValidatePageOffset(...)`: `page * pageSize` считается в `long` и при
превышении даёт `400` с кодом `*.paging.depth`; helper вызывается после `ValidatePaging`,
поэтому прежние `*.paging.page` / `*.paging.pageSize` отдаются первыми. Проверка включена в
`UserRepository.GetPagedAsync`, `BarberProfileRepository.GetPagedAsync` и
`ReviewRepository.GetByBarberProfileIdAsync`; Salon/HairService/Specialization не тронуты.
Новые коды в `Contracts/Common/ApiErrorCodes.cs` — `user.paging.depth`,
`barberProfile.paging.depth`, `review.paging.depth`. Тесты — `PagingDepthTests` в
`Infrastructure.UnitTests` (7 `[Fact]`: 10 000 → `null`, 10 001 → `Failure`,
`int.MaxValue * 200` без переполнения, три проводки на неоткрытом `ApplicationDbContext` с
`page = 51, pageSize = 200`, сохранённый приоритет `user.paging.pageSize`). Негативный прогон с
отключённым helper'ом: 5 failed / 2 passed, после восстановления 7/7. Тесты: 181 domain +
196 application + **51** infrastructure (+7); сборка — 0 ошибок, 0 предупреждений.

Закрыто 2026-10-24 (R-33): `CreateBookingRequest.Price` удалён из контракта, а не помечен
`[JsonIgnore]`. В позиционном record осталось семь параметров в прежнем порядке; XML-дока
больше не обещает цену. `CreateBookingHandler` не менялся — он и раньше брал
`BarberService.Price`; в его XML-доку добавлено, что поля `price` в запросе нет. Обновлены все
call sites: `BookingCreateTests.CreateRequest`, `CurrentUserAuthorizationTests`, тело
`POST {{host}}/api/bookings` в `BarberSearch.Web.http`, `$bookingBody` в `deploy/smoke.ps1`,
`BuildBookingRequest` в `load/harness/Runner.cs`. Лишний `"price"` от старого клиента не даёт
`400`: default `JsonUnmappedMemberHandling.Skip` пропускает неизвестный член — проверено
десериализацией через MVC `JsonOptions` с конвертером из `JsonSettings` (объект создаётся,
поля читаются), обработчик цену из запроса не читает. Новый тест
`CreateBookingRequest_HasNoPriceProperty` (reflection: нет публичного свойства и параметра
конструктора `Price`); тест цены переименован в `Create_PriceComesFromBarberService`.
Негативный прогон с временно добавленным свойством `Price`: 1 failed / 0 passed, после
восстановления 197/197. Тесты: 181 domain + **197** application (+1) + 51 infrastructure;
сборка — 0 ошибок, 0 предупреждений.

Закрыто 2026-10-24 (R-34): появился аудит изменений. Новая таблица `audit_log` (`id` UUIDv7,
`occurred_at`, `actor_user_id` nullable, `action` varchar(64), `entity_type` varchar(64),
`entity_id`, `payload jsonb`); индексы по `occurred_at DESC`, `(entity_type, entity_id)` и
`actor_user_id`, внешнего ключа на `users` нет — аудит переживает удаление пользователя.
Строка аудита добавляется в тот же `SaveChanges`, что и изменение: `AuditTrail.Capture`
вызывается в `RepositoryBase.SaveChangesAsync` до сохранения, поэтому `IUnitOfWork` откатывает
их вместе. Web-фильтр и EF-интерсептор отклонены: первый пишет после коммита и не видит старых
значений, второй не получает scoped `ICurrentUser` при `AddDbContextPool`. Аудируются только
`User`, `BarberProfile`, `Review` по allowlist (роль, активность, верификация, рейтинг,
публикация отзыва и т. п.); логины и `UpdatedAt`/`xmin` — нет. Payload — camelCase JSON,
`values` для created/deleted и `changes.from/to` для updated; `password`/`hash`/`stamp`/`token`/
`secret` в именах свойств редактируются в `"[redacted]"`, строки длиннее 500 символов
обрезаются. Миграция `AddAuditLog`. Тесты: `AuditTrailTests` (8) и `AuditLogModelTests` (4);
негативный прогон с отключённым `Capture` — 6 failed / 57 passed, после восстановления 63/63.
Тесты: 181 domain + 197 application + **63** infrastructure (+12); сборка — 0 ошибок,
0 предупреждений. На реальной PostgreSQL в отдельной `barbersearch_verify_r34` (все миграции)
создан пользователь и сменена роль: в `audit_log` две строки (`user.created` с email/role и
`passwordHash`/`securityStamp` = `[redacted]`; `user.updated` с `changes.role.from=Customer`,
`to=Admin`). Атомарность: изменение сохранено, операция вернула `Failure` — число строк
`audit_log` до и после 3 и 3, пользователь остался активным; БД и временный проект удалены.

Закрыто 2026-10-24 (R-35): отзывы переведены на **пре-модерацию**. `Review.Create` ставит
`IsPublished = false`, поэтому новый отзыв не меняет публичную сводку; `CreateReviewHandler`
больше не зависит от `ReviewRatingRecalculator`. Публикует и скрывает отзыв администратор
существующими `POST`/`DELETE api/reviews/{id}/publication`; рейтинг пересчитывается на
публикации, скрытии и правке опубликованного. Для поиска скрытых отзывов добавлена очередь
`GET api/reviews/moderation` (AdminOnly, keyset, `created_at ASC, id` — старые сверху) с
`GetModerationPageAsync` в репозитории, фейке и Application-обработчике; частичный индекс
`ix_reviews_moderation_created_at_id` (`is_published = false`) и миграция
`AddReviewModerationIndex`. Тесты: domain 181 (переименован `Create_StartsPendingModeration`),
application 200 (+3: `Publish_CreatedReview_UpdatesRatingAndCount`, `ReviewModerationTests` ×2),
infrastructure 64 (+1: `ReviewModerationIndexModelTests`); сборка 0/0; миграция проверена на
отдельной `barbersearch_verify_r35` (Up/Down, индекс есть/удаляется), БД удалена. Load harness
публикует созданные отзывы админом перед проверкой рейтинга, публикация вне статистики create.
Ограничение: автор не видит свой отзыв до публикации — author-facing status list follow-up.

Закрыто 2026-10-25 (R-41): `Entity` объявлен как `IEquatable<Entity>` — типизированный
`Equals(Entity?)` существовал, но контракта не было, поэтому `EqualityComparer<Entity>.Default`
и generic-код шли через `Equals(object?)` с проверкой типа. Логика сравнения по `Id` без
`GetType()` (ради прокси EF Core), «`Guid.Empty` — не идентификатор», `GetHashCode` и операторы
`==`/`!=` не менялись; XML-доки объясняют интерфейс, прокси и пустой Id. В `EntityTests`
добавлены 3 `[Fact]`: интерфейс реализован тем же методом `Equals(Entity?)`; типизированный вызов
согласован с `==`/`Equals(object)`/хешем (прокси-подтип с тем же Id, `null` → false); разные Id →
false. Тесты: **184 domain** (+3) + 212 application + 69 infrastructure + 6 integration — зелёные;
сборка Debug/Release — 0/0. Red-green: без интерфейса тестовый проект не компилируется
(CS0266 ×2), после возврата — 184/184. `CA1067` не срабатывал и до правки: `Equals(object)` уже
был переопределён.

Закрыто 2026-10-25 (R-42): 201 на восьми create-эндпоинтах остаётся без заголовка `Location` —
это сознательное решение, а не недоделка. Ни у одного из восьми ресурсов, создаваемых с 201,
нет GET по id, читаемого сразу после создания: `GET api/bookings`/`GET api/reviews` — списки,
причём новый отзыв скрыт до публикации (R-35); у `api/salons`, `api/hair-services`,
`api/specializations`, `api/users` чтения нет; `GET api/auth/me` — не ресурс по id;
единственный GET по id, `GET api/barbers/{id}`, отдаёт только `IsActive && IsVerified`
(`GetCardByIdAsync`) и сразу после создания отвечает 404. Ссылка в никуда хуже отсутствия ссылки, поэтому `Location` не ставится;
пересмотреть, когда у созданного ресурса появится GET по id (тогда `CreatedAtAction`/
`Created(uri, value)` с интеграционным тестом). XML-доки `ApiController.ToCreatedResult` и
`CreatedResourceResponse` фиксируют причину и условие пересмотра со ссылкой на R-42. Поведение
API не менялось: 201 и тело `{ id }` прежние. Тесты: 184 domain + 212 application + 69
infrastructure + 6 integration — зелёные; сборка Debug/Release — 0/0 (изменение документационное).

Закрыто 2026-10-25 (R-43): `FindBarberProfileSalon` больше не возвращает доменную сущность.
`FindBarberProfileSalonQuery : IQuery<BarberSalonResponse>`, обработчик —
`IQueryHandler<BarberSalonResponse, FindBarberProfileSalonQuery>` — собирает DTO приватным `ToResponse`;
проверка владельца (`GetOwnerIdAsync` + `EnsureCanManage`) и узкое чтение `GetSalonAsync` не менялись,
коды ошибок и JSON-контракт прежние. Из `BarberProfilesController.FindSalon` ушли ручной маппинг и
`using BarberSearch.Domain.BarberSalons` — действие стало `ToActionResult(await handler.Handle(...))`.
Тест `FindSalon_LoadsOwnerAndOneSalon` проверяет тип `BarberSalonResponse`, поля `Id`/`BarberProfileId`/
`SalonId`/`IsPrimary`, маппинг расписания (день/время/`IsDayOff`) и вызовы `["owner", "salon"]`.
Тесты: 184 domain + 212 application + 69 infrastructure + 6 integration — зелёные; сборка
Debug/Release — 0/0. Red-green: с возвращённым `IQuery<BarberSalon>` тест падает `Assert.IsType`
(expected `BarberSalonResponse`, actual `BarberSalon`), после возврата — 212/212.

Закрыто 2026-10-25 (R-44): переупорядочивание фото больше не дублируется. Удалён маршрут
`PUT api/barber-profiles/{id}/photos/{photoId}/order`, действие `PortfolioPhotosController.Reorder`,
команда/обработчик `ReorderPortfolioPhoto` и контракт `ReorderPortfolioPhotoRequest` (Scrutor-скан
сам перестал регистрировать обработчик — ручных регистраций нет). Остался путь через агрегат:
`PUT …/photos/{photoId}/sort-order` → `ReorderBarberProfilePhotoHandler` → `BarberProfile.ReorderPhoto`
(проверка принадлежности фото, `barberProfile.reorderPhoto.notFound`, `MarkUpdated`); поведение не
менялось. `PortfolioPhoto.Reorder(int)` стал `internal` — публичная точка входа одна, ребёнок
снаружи не меняется; XML-доки контроллера и метода это фиксируют. Это осознанный breaking change
API для клиентов, вызывавших `/order`; замена — `/sort-order`. Новых эндпоинтов, изменений
JSON/БД/`.sln` нет. Тесты: 184 domain + 212 application + 69 infrastructure + 6 integration —
зелёные; сборка Debug/Release — 0/0. До правки `/order` существовал (исходный код контроллера);
после в коде нет `ReorderPortfolioPhoto` и маршрута `photos/{photoId:guid}/order`, а `photo.Reorder(`
вызывается только из `BarberProfile.ReorderPhoto` — это и есть внутренняя деталь агрегата.

Закрыто 2026-10-25 (R-45): шаг сетки слотов по умолчанию больше не магическое число.
`Domain/Constants.cs` получил `DEFAULT_SLOT_DURATION_MINUTES = 30` (шаг, когда услуга не
выбрана; при выбранной услуге шаг — `BarberService.DurationMinutes`, R-20), а
`BarberProfileRepository.GetSlotContextAsync` читает его вместо `var durationMinutes = 30;`.
Поведение не изменилось: без `hairServiceId` — 30 минут, с услугой — её длительность.
Вторая половина пункта («берёт услугу по `HairServiceId`») закрыта R-20 и не переделывалась:
`BuildSlotServiceQuery` по-прежнему фильтрует `barber_profile_id` + `hair_service_id` +
`is_active` и проецирует `barberServiceId`/`hairServiceId`/`DurationMinutes` из одной строки
`barber_services`. XML-доки `BarberSlotContext`, `GetBarberSlotsQuery`, `BarberSlotsResponse`,
`IBarberProfileRepository`, `BarbersController` и `AGENT.md` §16 называют константу вместо
«30 минут» словами. Новых эндпоинтов/контрактов/миграций нет. Тесты: 184 domain + 212
application + 69 infrastructure + 6 integration — зелёные; сборка Debug/Release — 0/0.

Закрыто 2026-10-25 (R-46): рабочего пароля БД в репозитории больше нет.
`ConnectionStrings:Postgres` в `appsettings.json` оставлена пустой, в `BarberSearch.Web.csproj`
добавлен `<UserSecretsId>barbersearch-web</UserSecretsId>`, а `StartupSettings` при пустой
строке падает на старте с командой `dotnet user-secrets set "ConnectionStrings:Postgres" …`
и альтернативой `ConnectionStrings__Postgres` (значение не выводится). Локальная настройка
описана в `DEPLOY.md` §1.1. Проверено реальным процессом: без env — exit `-532462766` и новое
сообщение, с env — `GET /health/live` 200. Тесты: 184 domain + 212 application +
69 infrastructure + 6 integration — зелёные; сборка Debug/Release — 0/0.

Закрыто 2026-10-25 (R-47): `AllowedHosts` больше не wildcard. В `appsettings.json` только
`localhost;127.0.0.1`; публичные имена задаются на деплое через `AllowedHosts` (список через
`;`, без схемы/порта/пути). `StartupSettings.Load` после остальных проверок и только
в `Production` делит значение по `;` и роняет старт при пустом списке или `*`; сообщение
содержит `'*' is not allowed` и пример `AllowedHosts=api.example.com`. Development/Testing
остаются permissive. Host filtering отдаёт `400` неизвестному `Host`, поэтому прокси обязан
сохранять исходный заголовок. Тесты: `StartupSettingsTests` (Production + `*`, без БД/хоста)
и `HostFilteringTests` (localhost → 200, `evil.example.test` → 400; red-green). В
`deploy/smoke.ps1` добавлены `AllowedHosts` в managed env/`New-BaseEnvironment` и fail-fast
`allowed-hosts-wildcard`; `DEPLOY.md` §1/§3 дополнены. Тесты: 184 domain + 212 application +
70 infrastructure + 7 integration — зелёные; сборка Debug/Release — 0/0.

Закрыто 2026-10-25 (R-48): `POST api/auth/logout` ограничен политикой `auth` — 10 запросов
в минуту на IP, как login/register/refresh/change-password; `[AllowAnonymous]` сохранён,
потому что logout должен работать с истёкшей сессией и refresh-токеном в теле. Ветки
(тело/Bearer/cookie), antiforgery и коды ответов не менялись; глобальный лимит записи 30/мин
и партиции прежние. Добавлен `LogoutRateLimitTests`: 10 запросов → 204, 11-й → 429
с `Retry-After` и `rateLimit.exceeded`. Тесты: 184 domain + 212 application + 70
infrastructure + 8 integration — зелёные; сборка Debug/Release — 0/0. Red-green: без атрибута
11-й запрос возвращает 204.

Закрыто 2026-10-25 (R-49): refresh-cookie сознательно остаётся с `Path=/`. Это требование
префикса `__Host-`, поэтому сузить путь до `/api/auth`, сохранив префикс, нельзя; `Path` не
является границей доверия (тот же origin, `HttpOnly` + `Secure` + `SameSite`, сторонних
CDN/логов нет), а `__Host-` запрещает `Domain` и не даёт соседнему поддомену перекрыть
cookie. XML-`<remarks>` `RefreshTokenCookie` и `DEPLOY.md` §4 фиксируют решение и условие
пересмотра (отдельный origin/CDN или отказ от `__Host-` → `Path=/api/auth` и миграция имени).
Поведение не менялось; новый `RefreshCookieTests` проверяет по сырому `Set-Cookie` ровно
`path=/`, `httponly`, `secure`, `samesite=lax` и отсутствие `domain=`. Тесты: 184 domain +
212 application + 70 infrastructure + 9 integration — зелёные; сборка Debug/Release — 0/0.
Red-green: временный `Path = "/api/auth"` → падение на `path=/`, возврат → 9/9.

Закрыто 2026-10-25 (R-51): `AGENT.md` сокращён с 209 135 до 170 583 байт (204.2 → 166.6 КБ;
2442 → 2107 строк, −18.4%). Разбиение по слоям **сознательно отклонено**: ~70 ссылок `§N.M`
в коде/доках, `AGENT.md` авто-загружается агентом, а вынесенные §10/§13 стали бы «необязательными»;
split не устраняет причину роста. Убраны хронология и дубли: прогоны §5.3 (R-03…R-35) заменены
baseline 2026-10-25 (184/212/71/10, история — §2), §8 переписан в таблицу «слой → статус»,
§9.1–§9.12 свёрнуты в абзац (§9.3/§9.7 оставлены из-за ссылок §15.2; пункт про
`/weatherforecast` удалён), добавлены оглавление и политика документации §13. XML-доки
исходников не переписывались — отдельный срез. Тесты: 184 + 212 + 71 + 10 — зелёные;
Debug/Release — 0/0.

Закрыто 2026-10-25 (R-52): `GET api/reviews` объявляет `barberProfileId` обязательным через
`[FromQuery, BindRequired]`, поэтому отсутствие параметра отсекает model binding —
`400 ValidationProblemDetails` с ошибкой на поле `barberProfileId`, до `GetReviewsHandler`
запрос не доходит, и клиент больше не видит доменный код при непереданном параметре. Явный
пустой GUID (`00000000-…`) по-прежнему получает доменную 400 `review.getReviews.barberProfileId`:
проверка в обработчике сохранена как defense-in-depth, unit-тест остался зелёным. Новый
`ReviewsBindingTests` проверяет три состояния (нет параметра / пустой GUID / несуществующий
профиль → пустая страница) в одном сценарии; red-green: без атрибута — доменное тело с
`errors.BarberProfileId`, с атрибутом — `1/1`. `.http` дополнен примером запроса. Тесты:
184 + 212 + 71 + 11 — зелёные; Debug/Release — 0/0. Детали — `CODE-REVIEW.md` R-52.

Закрыто 2026-10-25 (R-53): `UseRateLimiter` остаётся после `UseAuthentication`/`UseAuthorization`.
Перенос выше auth сэкономил бы аутентификацию отбрасываемых запросов, но ключ партиции записи —
claim `sub` (`RateLimitSettings.WritePartitionKey`), и до auth лимит 30/мин на пользователя (R-22)
превратился бы в общий на IP: клиенты за одним NAT отбирали бы квоту друг у друга. Порядок
зафиксирован комментарием в `Program.cs`, XML-`<remarks>` `RateLimitSettings` и интеграционным
`RateLimitOrderTests`: A исчерпывает 30 запросов, 31-й получает `429`, запрос B — `400` со своей
партицией. Red-green: с временно перенесённым лимитером тест падает на 27-м запросе A; с исходным
порядком — `1/1`. Тесты: 184 + 212 + 71 + 12 — зелёные; Debug/Release — 0/0.

Закрыто 2026-10-25 (R-54): все десять project-записей `.sln` объявляют SDK-style project type
GUID `{9A19103F-16F7-4668-BE54-9A1E7A4F7556}` вместо legacy `{FAE04EC0-…}`. Заменены ровно
10 вхождений: project GUID'ы, пути, `SolutionConfigurationPlatforms`/`ProjectConfigurationPlatforms`,
`NestedProjects`, solution folder `tests` (`{2150E333-…}`) и порядок строк не менялись; кодировка
та же — ASCII + CRLF, без BOM (проверено побайтно). Diff относительно копии до правки — **10/10**
(`git diff --no-index --numstat`); относительно HEAD — 22/8, потому что 14/0 из этого файла уже
были незакоммичены прошлыми срезами (`Infrastructure.UnitTests` и `IntegrationTests`), а не
потому, что переписан весь файл. Вторая половина пункта, «`.http` с `/weatherforecast`», уже была
неверна: в `BarberSearch.Web.http` 0 вхождений `/weatherforecast`, а R-52 добавил туда пример
`GET api/reviews`; `.http` не менялся. `dotnet sln list` — те же 10 проектов; Debug/Release —
**0/0**; domain 184 + application 212 + infrastructure 71 + integration 12 — зелёные. `AGENT.md`
§5.1/§9.13 обновлены (пункт про legacy GUID из §9.13 удалён).

### 2.2 Auth (`AGENT.md` §17.7)

1. **Закрыто 2026-10-25 (R-50):** фоновая уборка истёкших refresh-токенов.
   `RefreshTokenCleanupService` (`Web/Auth`, `BackgroundService`) выполняет прогон сразу на
   старте и затем каждые `RefreshTokens:CleanupIntervalMinutes` (60 по умолчанию, 1..1440);
   `IRefreshTokenRepository.DeleteExpiredAsync(cutoff)` удаляет строки с
   `expires_at < now - 30 дней`. Граница — инвариант `replaced_by`: замена выпускается раньше
   истечения родителя и живёт не дольше 30 дней, поэтому `ON DELETE SET NULL` не обнулит
   ссылку живого токена; отозванные неистёкшие строки остаются для reuse-detection. Индекс
   `ix_refresh_tokens_expires_at` (миграция `AddRefreshTokenExpiresAtIndex`). `DbException`/
   `TimeoutException` логируются, следующий тик повторяет; несколько инстансов безопасны
   (DELETE идемпотентен). `TestApp.StartAsync` ждёт `InitialCleanupCompleted`, поэтому
   стартовый DELETE не пересекается с сидированием тестов. Тесты: 184 domain + 212 application
   + 71 infrastructure (+1) + 10 integration (+1) — зелёные; сборка Debug/Release — 0/0;
   миграция применена интеграционной фикстурой на свежей БД, pending model changes — нет.
   Детали — `CODE-REVIEW.md` R-50, `AGENT.md` §17.9.
2. **Закрыто 2026-10-23 (R-07):** две одновременные ротации одного refresh-токена
   сериализованы условным `UPDATE … WHERE revoked_at IS NULL` + проверкой `rowsAffected` в
   `RefreshTokenRepository.RotateAsync`; проигравший получает `401 auth.refresh.reused` и
   цепочку победителя не гасит. Детали — `CODE-REVIEW.md` R-07, `AGENT.md` §17.9.
3. Подтверждение email/телефона административное.
4. `POST api/users/{id}/logins` оставлен только для admin.
5. `logout` stamp не ротирует, поэтому предъявленный access-токен доживает до 15 минут;
   перехеширование пароля при входе ротирует stamp и завершает другие сессии. Остальное закрыто
   2026-10-23 (R-11): валидатор читает узкий `GetSecurityStampAsync` без `favorite_barbers`.

Закрыто 2026-10-21: ключи Data Protection персистентны и общие для инстансов; CORS для
cross-site SPA настраивается явно; forwarded headers обрабатываются только от своих прокси,
поэтому rate limit считается по реальному IP клиента.

### 2.3 Booking create — осознанно вне среза

- `EndAt` не выводится из `BarberService.DurationMinutes`: клиент по-прежнему присылает конец
  интервала. Если это меняется, нужен отдельный законченный срез с тестами и документацией;
  цена и принадлежность услуги уже серверные. Окно `StartAt` и верхняя граница длительности
  появились в R-04 (2026-10-22), но длительность всё ещё не сверяется с услугой.
- `GET api/barbers/{id}/slots` не ограничен горизонтом брони: за его пределами слоты
  показываются, а `POST api/bookings` отвечает `400 booking.create.startAt.tooFar`.
  Ограничивать ли выдачу слотов — продуктовое решение; если да, правка в
  `GetBarberSlotsHandler` (отдельный срез, тесты слотов придётся дополнить).

### 2.4 Rating recalculation — сознательные ограничения

- Пересчёт синхронный и в транзакции с изменением отзыва; `SELECT … FOR UPDATE` + count/avg +
  UPDATE в write-путь отзыва. Load test подтвердил запас: publish/hide p95 82.4–98.3 ms,
  create p95 189.3–269.8 ms при бюджетах 500/1000 ms.
- Ручной `PUT api/barber-profiles/{id}/rating` остаётся override: следующее изменение отзыва
  перезапишет его фактической сводкой.
- Кэш карточки/поиска не появился, поэтому инвалидация не нужна; при введении кэша изменение
  рейтинга должно её учитывать.

### 2.5 Load test — что осталось за кадром

1. **Search — закрыт 2026-10-20.** Expression-индекс даёт Index Scan; search p95 29.8–40.6 ms
   на 8 × 20 s и 28.5–31.5 ms в `Repeat=5`. Кэш не нужен.
2. **Booking**: p95 257.5–304.0 ms при бюджете 300 — один прогон вышел на 4 ms. Почти весь
   запас съеден advisory lock-ом. Если нужен запас — slot-claim/fast-path, не кэш.
3. **Инструменты**: `pg_stat_statements` не включён, `dotnet-counters` не установился,
   CPU PostgreSQL не читается через `Get-Process`. GC/thread-pool и DB CPU не измерены.
   Npgsql pool: максимум 51 backend-соединение при пуле 100, исчерпания не было.
4. **Стенд**: API, PostgreSQL и load generator на одной машине (24 logical CPU, 16 GB);
   `synchronous_commit=on`. Абсолютные цифры не переносятся на прод без поправки.

### 2.6 Deploy — ограничения среза

1. **Фактический выкат не выполнялся.** Проверены код, конфигурация и локальный смоук;
   конкретный прокси/оркестратор/секрет-стор и боевые значения — за командой эксплуатации.
2. **HSTS не отдаётся для `localhost`, `127.0.0.1` и `[::1]`** — это встроенное исключение
   ASP.NET Core. Проверять HSTS нужно на реальном hostname (в смоуке — `Host:
   api.barbersearch.test`).
3. **Смена сертификата шифрования key ring** не автоматизирована: старые ключи должны
   оставаться читаемыми, пока живы выданные cookie/antiforgery-токены. Планировать отдельно.
4. **Key ring надо бэкапить вместе с БД**: без него пользователи потеряют сессии.
5. **`AllowedHosts` задаётся на деплое** явным списком публичных имён хостов (без схемы/порта);
   в `appsettings.json` только `localhost;127.0.0.1`. В `Production` пустой список и `*`
   роняют старт, неизвестный `Host` получает `400`; прокси должен сохранять исходный `Host`.
6. **Уборка refresh-токенов — in-process hosted service**, отдельного cron/DB job нет: пока
   ни один инстанс не запущен, строки не удаляются; после старта первый прогон выполняется
   сразу. Несколько инстансов могут работать одновременно — `DELETE` идемпотентен.

## 3. Как запускать и проверять

```powershell
# сборка (в workspace-write падает без диагностики — нужен danger-full-access, AGENT.md §5.2)
dotnet build BarberSearch/BarberSearch.sln

# тесты: dotnet test с выводом в %TEMP% (иначе testhost падает; AGENT.md §5.2)
#   не используй `-q`: в SDK 10 это MSBuild question mode, а не quiet
$out = Join-Path $env:TEMP "bs-tests\<Project>"
dotnet test tests/<Project> --nologo --verbosity quiet "-p:BaseOutputPath=$out\"
#   ожидается: domain 184 / application 212 / infrastructure 71 / integration 12, все зелёные
#   интеграционным тестам нужен Docker (Testcontainers, postgres:18) или BARBERSEARCH_TEST_POSTGRES
#   reflection-раннер §5.3 — только fallback, если это перестанет работать

# миграции: design-time factory читает ConnectionStrings__Postgres
$env:ConnectionStrings__Postgres = 'Host=127.0.0.1;Port=5432;Database=barbersearch;Username=postgres;Password=postgres'
dotnet ef database update --project BarberSearch/src/BarberSearch.Infrastructure.Postgresql `
  --startup-project BarberSearch/src/BarberSearch.Web

# R-02: старые висячие ссылки (FK добавлены как NOT VALID); после очистки — VALIDATE CONSTRAINT
& 'C:\Program Files\PostgreSQL\16\bin\psql.exe' -h 127.0.0.1 -p 5432 -U postgres `
  -d barbersearch -f deploy/find-dangling-references.sql

# запуск: content root = каталог проекта, EventLog приглушён
cd BarberSearch/src/BarberSearch.Web
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Logging__EventLog__LogLevel__Default='None'
$env:ASPNETCORE_URLS='http://127.0.0.1:5243'
# один раз: строка подключения в user secrets (DEPLOY.md §1.1) либо env ниже
# dotnet user-secrets set "ConnectionStrings:Postgres" "Host=127.0.0.1;Port=5432;Database=barbersearch;Username=postgres;Password=postgres"
$env:ConnectionStrings__Postgres='Host=127.0.0.1;Port=5432;Database=barbersearch;Username=postgres;Password=postgres'
$env:Auth__SigningKey='<local key, >= 32 chars, never commit>'
# DataProtection:KeysPath в Development уже задан в appsettings.Development.json
dotnet bin/Debug/net10.0/BarberSearch.Web.dll
```

`Auth__SigningKey` и `ConnectionStrings__Postgres` обязательны и в Development: без них
приложение падает на старте. Ключ подписи, строка подключения и пароль сертификата — только
из секрет-стора, user secrets или переменной окружения; в `appsettings*.json` их нет
(`ConnectionStrings:Postgres` там намеренно пустая, R-46).

Локальная PostgreSQL 16.3 держит порт 5432, база `barbersearch`; строка подключения —
в user secrets (R-46, `DEPLOY.md` §1.1) или `ConnectionStrings__Postgres`. Логин/регистрация/
refresh/logout/change-password ограничены 10 попытками в минуту на IP: смоук с несколькими
пользователями легко ловит `429` — выдержи паузу между прогонами или разбей сценарий на окна.
На `POST api/bookings` и review-эндпоинтах действует общий лимит записи 30/мин на
пользователя (без `sub` — на IP).

Деплой-смоук (отдельная БД, API сам поднимается и останавливается):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File deploy/smoke.ps1
# доказательства: deploy/generated/smoke.txt; каталог git-ignored
```

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
Прочитай backend/HANDOFF.md и AGENT.md §15, §16, §17 (особенно §17.5, §17.7, §17.9,
§17.10) и backend/DEPLOY.md. Ветка features/issue-6, HEAD — 46b63d3; срез R-01–R-54
закоммичен девятью коммитами поверх 9736b85 (merge PR #5) и запушен, рабочее дерево чистое.
Верификация 2026-10-26 — §0.1: сборка Debug/Release 0/0, тесты 184/212/71/12, смоук 104.

Деплой-срез закрыт: Data Protection на постоянном томе, Auth__SigningKey обязателен и
ротируется, forwarded headers только от своих прокси, явный CORS + SameSite=None для
cross-site SPA, health checks, startup validation и graceful shutdown. Срезы R-01–R-50
закрыты. Из них R-05 — xmin + 409 `concurrencyConflict`, R-06 — отложенное
exclusion-ограничение основного салона, R-07 — сериализованная ротация refresh-токена,
R-08 — атомарные операции с учётными данными, R-09 — доменная проверка ключа
идемпотентности, R-10 — политика пароля, R-11 — узкий SELECT security stamp, R-12 — узкие
чтения профиля вместо полного агрегата, R-13 — EF-логи в `Warning` в проде, R-14 — сжатие
ответов и таймаут запроса, R-15 — транзакция и advisory lock создания брони принадлежат
`IUnitOfWork`, R-16 — отзыв refresh-токенов сырым SQL очищает change tracker, R-17 —
неудачный `SaveChanges` очищает change tracker перед `Failure`, R-18 — `lock_timeout`
advisory-блокировки, `55P03` → 409 `booking.add.lockTimeout` и `CommandTimeout` 30 с,
R-19 — сводка рейтинга одним агрегирующим запросом (`COUNT`/`AVG`), пустой набор →
`(null, 0)`, R-20 — `hairServiceId` в поиске/слотах против `barberServiceId` в брони и пара
идентификаторов в ответе слотов, R-21 — блокировка аккаунта после 5 неудач (1/2/4…60 минут,
сброс при успехе, `401 auth.login.lockedOut` только при верном пароле), R-22 — глобальный
лимит записи 30/мин на `sub`/IP и 429 с `Retry-After` и ProblemDetails
`rateLimit.exceeded`, R-23 — числовые enum'ы в JSON отклоняются, коллекции языков и способов
оплаты проверяются `Enum.IsDefined`, R-24 — URL фото/сайта/аватара проверяются доменом
(http/https, без userinfo, ≤ 2048) и колонки ограничены `varchar(n)`, R-25 — поиск, карточка,
слоты и список отзывов читаются анонимно с лимитом `reads` 120/мин на IP, R-26 — создание
профиля сразу привязывает его к пользователю в одной транзакции, R-27 — `JwtTokenIssuer`,
`AuthController` и маркировка no-show используют `TimeProvider` (домен — системные часы),
R-28 — публичные коды ошибок в `Contracts/Common/ApiErrorCodes`, репозитории не отдают свои
коды, 500 → `internal.database`, R-29 — `ICurrentUser` без бросающего `UserId`
(`TryGetUserId`/`RequireUserId` → `401 auth.unauthorized`), R-30 — `UnitOfWork` очищает
change tracker после отката, R-31 — один статический `JsonWebTokenHandler`, R-32 — предел
глубины offset-пагинации 10 000 (`*.paging.depth`), R-33 — `price` убран из контракта брони,
R-34 — аудит изменений в `audit_log`, R-35 — пре-модерация отзывов и админская очередь
`GET api/reviews/moderation`, R-36 — интеграционные тесты
(`tests/BarberSearch.IntegrationTests`, Testcontainers + `WebApplicationFactory`, 10 сценариев),
R-37 — `Directory.Build.props` (`TreatWarningsAsErrors` + анализаторы), `global.json`,
`.editorconfig`, CI в `.github/workflows/backend-ci.yml`, `Dockerfile`, R-38 — удалены
дублирующие индексы `barber_profiles` (остался keyset, миграция
`DropRedundantBarberProfileIndexes`), R-39 — `Failure.Errors` кэширует `ReadOnlyCollection`,
R-40 — `Error` с `init`/`with`, `ErrorType.NONE` удалён и маппинг явный, R-41 —
`Entity : IEquatable<Entity>`, R-42 — `Location` на 201 сознательно не ставится (нет
читаемого self-URI), R-43 — `FindBarberProfileSalon` отвечает DTO, R-44 — порядок фото
меняется только через агрегат (`/order` удалён), R-45 — шаг слотов в
`Constants.DEFAULT_SLOT_DURATION_MINUTES`, R-46 — `ConnectionStrings:Postgres` в
`appsettings.json` пустая и живёт в user secrets/env, R-47 — `AllowedHosts` без wildcard
(Production fail-fast, новый `HostFilteringTests`), R-48 — rate limit на `logout`,
R-49 — `Path=/` refresh-cookie сознательно принят из-за `__Host-` (+`RefreshCookieTests`),
R-50 — фоновая уборка истёкших refresh-токенов, индекс `ix_refresh_tokens_expires_at` и
миграция `AddRefreshTokenExpiresAtIndex`.
Сборка 10/0/0 (Debug и Release), тесты 184 domain + 212 application + 71 infrastructure +
12 integration — зелёные; деплой-смоук перепрогнан 2026-10-26 после R-21…R-54: 104 проверки,
PASS (найден и исправлен баг сидов, см. §0.1). Фактический выкат в прод не выполнялся.

Не трогай search, booking correctness, rating recalculation и read-эндпоинты без
необходимости. Кэш/Redis/output caching не начинать. Booking headroom (p95 257.5–304.0
при бюджете 300) — отдельный срез slot-claim/fast-path. Открытые дефекты — §2.6.

Сборка/тесты/git — через danger-full-access; тесты — `dotnet test` с
`-p:BaseOutputPath` в `%TEMP%` (§5.2), reflection-раннер §5.3 — только fallback; сборка
10 проектов, 0 ошибок, 0 предупреждений; все тесты
зелёные (184 domain + 212 application + 71 infrastructure + 12 integration).
```
