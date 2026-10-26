# HARDCODED-LITERALS-TASKS — план из 8 отдельных сессий

> Handoff-файл к аудиту захардкоженных литералов от 2026-10-26.
> Каждая сессия выполняет **ровно один** раздел `S*` и не трогает остальные.
> Ветка: `features/issue-7`, baseline чистый (`git status --short` пуст).
> Рабочий каталог: `D:\Projects\barbersearch\backend`.

## Общие правила для всех сессий

1. Перед изменениями прочитать `AGENT.md` (особенно §10 «Code conventions») и `HANDOFF.md` §2.
2. Не коммитить, не пушить, не создавать PR. Все изменения остаются в рабочем дереве.
3. Не менять публичное поведение API, коды ответов, JSON-контракты и схему БД, если это прямо не требуется заданием.
4. Не редактировать `Migrations/*` вручную. Если меняется EF-модель — отдельно сообщить об этом.
5. Комментарии и XML-доки — по-русски, сообщения ошибок — по-английски (AGENT.md §10.7–10.8).
6. Константы именовать `SCREAMING_SNAKE_CASE`, если это доменные константы (`Constants`), иначе — по стилю соседних классов.
7. После изменений обязательно:
   ```powershell
   dotnet build BarberSearch/BarberSearch.sln --nologo
   ```
   Ожидается **0 errors / 0 warnings**. Тесты — по возможности:
   ```powershell
   dotnet test tests/<Project> --nologo --verbosity quiet `
     "-p:BaseOutputPath=$env:TEMP\bs-tests\<Project>\"
   ```
   Если тесты в этой сессии не запускаются — честно указать это в отчёте.
8. В отчёте перечислить: какие файлы изменены, что именно сделано, результаты build/test, риски и всё, что осталось за рамками.
9. Если задача требует решения, которое меняет поведение, — остановиться и описать варианты, не выбирать молча.

---

## S1. Убрать дефолтные пароли и абсолютные пути из dev-инфраструктуры

**Цель:** в репозитории не должно оставаться готовых паролей и путей, привязанных к конкретной машине.

**Файлы:**
- `docker-compose.yml`
- `deploy/smoke.ps1`
- `load/run.ps1`
- `load/ci/booking-idempotency.ps1`
- `load/collect-metrics.ps1`
- `BarberSearch/src/BarberSearch.Web/BarberSearch.Web.http`
- `DEPLOY.md`, `LOADTEST.md`, `AGENT.md` (только команды/примеры, если меняется поведение)

**Что сделать:**
1. `docker-compose.yml`: убрать дефолт `postgres` у `POSTGRES_PASSWORD` — использовать обязательную переменную:
   `${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}`. `POSTGRES_USER`, `POSTGRES_DB`, `POSTGRES_PORT` можно оставить с несекретными дефолтами.
   Добавить `.env.example` с плейсхолдерами, а сам `.env` добавить в `.gitignore`.
2. `deploy/smoke.ps1`, `load/run.ps1`, `load/ci/booking-idempotency.ps1`, `load/collect-metrics.ps1`:
   - пароль БД и тестовых пользователей брать из параметра/переменной окружения (`PGPASSWORD`, `BARBERSEARCH_DB_PASSWORD` или `-DbPassword`), **без дефолта `postgres`**;
   - хост, порт, пользователь, имя БД — параметры с текущими значениями по умолчанию;
   - `psql` искать через параметр `-Psql` с дефолтом `psql` (PATH), а не `C:\Program Files\PostgreSQL\16\...`;
   - не логировать пароль.
3. `BarberSearch.Web.http`: заменить пример пароля `Secret123!` на плейсхолдер (`{{password}}` + комментарий, что значение задаётся локально), не хранить рабочий пароль.
4. Обновить документацию: команды `docker compose` должны требовать `POSTGRES_PASSWORD`; примеры `psql` — без абсолютного пути.

**Критерии приёмки:**
- `git grep -n "Password=postgres"` не находит совпадений в `deploy/` и `load/`;
- `git grep -n "PGPASSWORD = 'postgres'\|Smoke!Passw0rd\|LoadTest!Passw0rd\|Secret123!"` пусто;
- `git grep -n "C:\\\\Program Files\\\\PostgreSQL"` пусто;
- `docker compose config` с заданным `POSTGRES_PASSWORD` валиден, без него — понятная ошибка;
- build зелёный.

**Вне рамок:** боевые секреты, `appsettings`, `Auth__SigningKey`, CI.

---

## S2. Auth-константы: cookie, CSRF-заголовок, Bearer, claim-значения

**Цель:** убрать строковые литералы аутентификации из Web-слоя и свести их в один набор констант.

**Файлы:**
- `BarberSearch/src/BarberSearch.Web/Auth/AuthenticationExtensions.cs`
- `BarberSearch/src/BarberSearch.Web/Auth/RefreshTokenCookie.cs`
- `BarberSearch/src/BarberSearch.Web/Auth/ClaimsPrincipalFactory.cs`
- `BarberSearch/src/BarberSearch.Web/Auth/AuthorizationExtensions.cs`
- `BarberSearch/src/BarberSearch.Web/Auth/AuthProblemDetails.cs`
- `BarberSearch/src/BarberSearch.Web/Auth/JwtTokenIssuer.cs`
- `BarberSearch/src/BarberSearch.Web/Auth/JwtSigningKeys.cs`
- `BarberSearch/src/BarberSearch.Web/Controllers/AuthController.cs`
- `BarberSearch/src/BarberSearch.Web/Common/FailureProblemDetails.cs`

**Что сделать:**
1. Добавить статический класс `AuthConstants` (рядом с `AuthOptions`) с константами:
   - `DEVELOPMENT_SESSION_COOKIE = "barbersearch.auth"`, `PRODUCTION_SESSION_COOKIE = "__Host-barbersearch"`;
   - `CSRF_HEADER_NAME = "X-CSRF-TOKEN"`, `COOKIE_PATH = "/"`;
   - `BEARER_PREFIX = "Bearer "`, `BEARER_SCHEME = "Bearer"`;
   - `SMART_SCHEME_DISPLAY_NAME = "Bearer or Cookie"`;
   - `AUTH_REQUIRED_MESSAGE`, `ACCESS_DENIED_MESSAGE` (единые тексты 401/403).
   Имена refresh-cookie уже есть в `RefreshTokenCookie` — переиспользовать их, не дублировать.
2. `AuthenticationExtensions`:
   - использовать константы для имени cookie, CSRF-заголовка, пути, `BearerPrefix`, `WWW-Authenticate`, display name;
   - `ClockSkew = TimeSpan.FromSeconds(30)` пока **не трогать** — это S4.
3. `AuthController`: заменить приватный `BearerPrefix` на общую константу.
4. `JwtTokenIssuer`: `"Bearer"` в `TokenResponse` — на `AuthConstants.BEARER_SCHEME` (или `JwtBearerDefaults.AuthenticationScheme`, если так чище).
5. `ClaimsPrincipalFactory` и `AuthorizationExtensions`: значения `"true"`/`"false"` — в константы `ClaimValues.True`/`False` (или `AuthConstants`).
6. `AuthProblemDetails` и `FailureProblemDetails`: тексты 401/403 свести к общим константам, если они совпадают по смыслу; не менять HTTP-статусы.
7. `JwtSigningKeys`: вынести `16` (длина `kid`) в `private const int KEY_ID_LENGTH = 16`.
8. Обновить `RefreshCookieTests`, если он ссылается на имена cookie: значения не меняются, но лучше использовать константы.

**Критерии приёмки:**
- в `Web/Auth` и `AuthController` не осталось литералов `"barbersearch.auth"`, `"__Host-barbersearch"`, `"X-CSRF-TOKEN"`, `"Bearer "`, `"WWW-Authenticate"`, `"true"`/`"false"` (кроме констант);
- поведение и имена cookie/заголовков не изменились;
- build и `RefreshCookieTests`/`JwtTokenIssuerTests` зелёные.

**Вне рамок:** `ClockSkew`, лимиты, `appsettings`.

---

## S3. `[EnableRateLimiting("auth")]` → константа

**Цель:** убрать шесть литералов `"auth"` из атрибутов.

**Файл:** `BarberSearch/src/BarberSearch.Web/Controllers/AuthController.cs`.

**Что сделать:**
1. Заменить `[EnableRateLimiting("auth")]` на `[EnableRateLimiting(RateLimitSettings.AuthPolicyName)]` во всех шести местах (строки ~53, 89, 130, 195, 288, 336).
2. Добавить `using BarberSearch.Web.Deployment;`, если его нет.
3. Проверить, что `RateLimitSettings.AuthPolicyName` — `public const string`, пригодный для атрибута.

**Критерии приёмки:**
- `git grep -n 'EnableRateLimiting("auth")'` пусто;
- build зелёный; `RateLimitOrderTests`/`LogoutRateLimitTests` не падают (если запускаются).

**Вне рамок:** значения лимитов, окно, политика `reads`.

---

## S4. Один источник дефолтов конфигурации + ClockSkew + ключи конфигурации

**Цель:** убрать дублирование значений между `AuthOptions` и `appsettings.json`, вынести `ClockSkew` и имена ключей.

**Файлы:**
- `BarberSearch/src/BarberSearch.Web/Auth/AuthOptions.cs`
- `BarberSearch/src/BarberSearch.Web/Auth/AuthenticationExtensions.cs`
- `BarberSearch/src/BarberSearch.Web/appsettings.json`
- `BarberSearch/src/BarberSearch.Web/Deployment/StartupSettings.cs`
- `BarberSearch/src/BarberSearch.Web/ApplicationDbContextFactory.cs`
- `tests/BarberSearch.Infrastructure.UnitTests/StartupSettingsTests.cs` (если нужно)

**Что сделать:**
1. Выбрать **один** источник истины для `Issuer`, `Audience`, `AccessTokenMinutes`, `RefreshTokenDays`, `CookieHours`, `CookieSameSite`.
   Предпочтительно: значения живут в `appsettings.json`, а `AuthOptions` не содержит дублирующих инициализаторов. `AuthOptions.FromConfiguration` должен требовать секцию `Auth` и падать с понятным сообщением, если значение отсутствует/пустое. Обновить тесты, которые создают `AuthOptions` напрямую, если они рассчитывают на дефолты.
   Альтернатива (если так меньше ломается): оставить дефолты в `AuthOptions`, а из `appsettings.json` убрать дублирующие значения. Выбор обосновать в отчёте.
2. `ClockSkew`: добавить `AuthOptions.ClockSkewSeconds` (по умолчанию 30, диапазон, например, `0..300`), значение — в `appsettings.json`; в `AuthenticationExtensions` использовать `TimeSpan.FromSeconds(options.ClockSkewSeconds)`.
3. `StartupSettings`: вынести ключи `"Deployment:ShutdownTimeoutSeconds"`, `"HealthChecks:DatabaseTimeoutSeconds"`, `"RefreshTokens:CleanupIntervalMinutes"` в приватные константы (или общий `ConfigurationKeys`), чтобы не дублировать строки в коде и сообщениях.
4. `ApplicationDbContextFactory`: имя переменной окружения строить из `DependencyInjection.POSTGRES_CONNECTION_STRING_NAME`:
   `"ConnectionStrings__" + BarberSearch.Web.DependencyInjection.POSTGRES_CONNECTION_STRING_NAME`.
5. Обновить `DEPLOY.md`, если добавляется новая настройка `Auth:ClockSkewSeconds`.

**Критерии приёмки:**
- одно и то же значение не задано одновременно в `AuthOptions` и `appsettings.json`;
- `ClockSkew` настраивается и валидируется;
- build и `StartupSettingsTests`/`JwtTokenIssuerTests` зелёные;
- `appsettings.json` остаётся валидным JSON.

**Вне рамок:** лимиты rate limiting, таймаут запроса, SQL.

---

## S5. SQL check-ограничения из enum'ов и констант

**Цель:** числовые значения в `HasCheckConstraint`/`HasFilter` не должны быть магическими и не должны расходиться с enum'ами.

**Файлы:**
- `BarberSearch/src/BarberSearch.Infrastructure.Postgresql/Configurations/BookingConfiguration.cs`
- `.../HairServiceConfiguration.cs`
- `.../UserConfiguration.cs`
- `.../WorkingScheduleConfiguration.cs`
- `.../BarberProfileConfiguration.cs`
- `.../RefreshTokenConfiguration.cs`
- при необходимости — новый helper в `Configurations/` и константы в `BarberSearch.Domain/Constants.cs`

**Что сделать:**
1. Добавить helper (например, `CheckConstraintSql`):
   - `EnumRange<TEnum>(string column)` → `"column BETWEEN {min} AND {max}"` по `Enum.GetValues<TEnum>()`;
   - `EnumIn<TEnum>(string column)` → `"column IN (1, 2, ...)"`;
   - `DecimalRange(string column, decimal min, decimal max)` → формат `0.00`, чтобы сохранить текущий SQL.
2. Заменить:
   - `BookingConfiguration`: `status IN (1, 2, 3, 4, 5)` → `EnumIn<BookingStatus>("status")`; `status IN (1, 2)` → активные статусы (Pending, Confirmed) через helper/константу;
   - `HairServiceConfiguration`: `category IN (1, 2, 3, 4, 5, 99)` → `EnumIn<ServiceCategory>("category")`;
   - `UserConfiguration`: `role IN (1, 2, 3)` → `EnumIn<UserRole>("role")`;
   - `WorkingScheduleConfiguration`: `day_of_week BETWEEN 0 AND 6` → `EnumRange<DayOfWeek>("day_of_week")`;
   - `BarberProfileConfiguration`: `grade BETWEEN 0 AND 10` → `EnumRange<Grade>("grade")`; `rating BETWEEN 1.00 AND 5.00` → `DecimalRange("rating", Constants.MIN_RATING, Constants.MAX_RATING)`;
   - `RefreshTokenConfiguration`: `char_length(token_hash) = 64` → `Constants`/`RefreshToken.HASH_LENGTH` (уже есть `RefreshToken.HASH_LENGTH`, использовать его).
3. `HasPrecision(10, 2)` и `HasPrecision(3, 2)` — вынести в именованные константы (`MONEY_PRECISION`, `MONEY_SCALE`, `RATING_PRECISION`, `RATING_SCALE`), если это не раздувает код.
4. **Критично:** сгенерированный SQL должен остаться **посимвольно тем же**. Проверить:
   ```powershell
   $env:ConnectionStrings__Postgres='Host=127.0.0.1;Port=59999;Database=x;Username=x;Password=x'
   dotnet ef migrations has-pending-model-changes `
     --project BarberSearch/src/BarberSearch.Infrastructure.Postgresql `
     --startup-project BarberSearch/src/BarberSearch.Web
   ```
   Ожидается «No changes have been made». Если EF видит изменение — **не создавать миграцию молча**, а сообщить, какая строка изменилась, и остановиться.
5. Если для `DayOfWeek`/`Grade` нужны min/max — использовать `Enum.GetValues`, не хардкодить 0/10/6 повторно.

**Критерии приёмки:**
- в конфигурациях не осталось `IN (1, 2, ...)` и `BETWEEN 0 AND ...` с числами, кроме сгенерированных helper'ом;
- `has-pending-model-changes` — без изменений;
- build и `BarberProfileIndexModelTests`/`ReviewModerationIndexModelTests`/`ConcurrencyModelTests` зелёные.

**Вне рамок:** сами миграции, exclusion-ограничения, `citext`.

---

## S6. Формат курсора: версия и число частей

**Цель:** убрать дубли `"1"` и `3` из трёх классов курсоров.

**Файлы:**
- `BarberSearch/src/BarberSearch.Application/Common/CursorCodec.cs`
- `.../BarberSearchCursor.cs`
- `.../BookingCursor.cs`
- `.../ReviewCursor.cs`

**Что сделать:**
1. В `CursorCodec` добавить:
   - `public const string VERSION = "1";`
   - `public const int PART_COUNT = 3;`
   - при желании `GUID_FORMAT = "D"`, `SEPARATOR = '|'`, `BASE64_BLOCK = 4`.
2. В `Encode()` каждого курсора использовать `CursorCodec.VERSION`.
3. В `Decode()` — `CursorCodec.Decode(cursor, CursorCodec.PART_COUNT, ...)` и сравнивать `parts[0] != CursorCodec.VERSION`.
4. `"D"` в `Guid.TryParseExact`/`ToString` заменить на общую константу, если это не ухудшает читаемость.
5. Формат курсора не менять: старые курсоры должны продолжать читаться.

**Критерии приёмки:**
- в трёх классах нет литералов `"1"` и `3` в роли версии/числа частей;
- `CursorTests` зелёные; build зелёный.

**Вне рамок:** коды ошибок курсора (`*.cursor.invalid`) — это S7.

---

## S7. Дедуп повторяющихся кодов ошибок и сообщений

**Цель:** убрать дубли там, где один и тот же текст/код уже встречается несколько раз; не переписывать все 141 код.

**Файлы (ориентировочно):**
- `BarberSearch/src/BarberSearch.Domain/BarberProfiles/BarberProfile.cs`
- `BarberSearch/src/BarberSearch.Domain/Salons/Salon.cs`
- `BarberSearch/src/BarberSearch.Domain/Common/` (новый класс сообщений/кодов, если нужен)
- `BarberSearch/src/BarberSearch.Application/PortfolioPhotos/*`
- `BarberSearch/src/BarberSearch.Application/BarberServices/*`
- `BarberSearch/src/BarberSearch.Application/BarberProfiles/GetBarberProfile/GetBarberProfileHandler.cs`
- `.../GetBarberSlots/GetBarberSlotsHandler.cs`
- `.../Reviews/GetReviews/GetReviewsHandler.cs`
- `BarberSearch/src/BarberSearch.Application/Common/{CursorCodec,BarberSearchCursor,BookingCursor,ReviewCursor}.cs`
- `BarberSearch/src/BarberSearch.Infrastructure.Postgresql/Repositories/{BarberProfileRepository,BookingRepository,ReviewRepository}.cs`
- `BarberSearch/src/BarberSearch.Domain/Users/User.cs`

**Что сделать:**
1. Найти повторы скриптом:
   ```powershell
   # все строковые литералы, встречающиеся >1 раза, с файлами
   ```
   Ориентир из аудита: 29 уникальных повторяющихся сообщений (43 лишних вхождения) и 12 повторяющихся кодов.
2. Для каждого повторяющегося **сообщения** завести константу в том слое, откуда оно родом:
   - доменные сообщения (`"Price cannot be negative."`, `"The specified photo..."`, `"The barber in the specified salon does not work."`) — в `BarberSearch.Domain` (например, `Common/DomainMessages.cs`);
   - application-сообщения (`"The pagination cursor is invalid."`, `"Page limit must be positive."`) — в `Application/Common` (например, `Messages.cs`), инфраструктура может ссылаться на Application;
   - если сообщение нужно и Domain, и Application, и Infrastructure — допустим `Shared` (все слои его видят).
3. Для повторяющихся **кодов** (`salon.city`, `salon.address`, `salon.websiteUrl`, `salon.timeZoneId`, `salon.country`, `barberSearch.cursor.invalid`, `booking.cursor.invalid`, `review.cursor.invalid`, `auth.refresh.missing`, `auth.refresh.invalid`, `auth.refresh.reused`, `barberProfile.findSalon.notFound`) — константы рядом с соответствующим агрегатом/классом.
   `barberProfile.findSalon.notFound` уже есть в `ApiErrorCodes.FindSalonNotFound`, но Domain не может ссылаться на Contracts; оставить доменную константу и добавить тест, что значение совпадает с реестром.
4. Добавить тест `ApiErrorCodesTests` (или новый), который проверяет: публичные коды из реестра не встречаются строковыми литералами вне `ApiErrorCodes`, кроме известного доменного исключения `barberProfile.findSalon.notFound`. Если тест сложно сделать надёжно — хотя бы зафиксировать список исключений.
5. Не менять тексты и коды: значения должны остаться прежними, меняется только способ их записи.

**Критерии приёмки:**
- повторяющиеся сообщения/коды не дублируются (проверить скриптом, что осталось только по одному вхождению каждого значения);
- публичные коды не изменились;
- build и `ApiErrorCodesTests`, `BarberProfileReadTests`, `BarberSlotsTests`, `ReviewReadTests` зелёные.

**Вне рамок:** все 141 уникальный код, локализация, тексты сообщений.

---

## S8. Логи без `R-50` + fallback-коды `Shared/Error.cs`

**Цель:** убрать внутренние трекерные номера из runtime-логов и привести fallback-коды ошибок к единому формату.

**Файлы:**
- `BarberSearch/src/BarberSearch.Web/Auth/RefreshTokenCleanupService.cs`
- `BarberSearch/src/Shared/Error.cs`
- `tests/BarberSearch.Application.UnitTests/ErrorTests.cs`

**Что сделать:**
1. В `RefreshTokenCleanupService` убрать ` (R-50)` из трёх шаблонов логов (строки ~167, ~177, ~183). Ссылки `R-50` в XML-доках/комментариях оставить.
2. В `Shared/Error.cs` завести `FallbackErrorCodes` (или `ErrorCodes`) с константами:
   - `NOT_FOUND = "error.notFound"`,
   - `VALIDATION = "error.validation"`,
   - `CONFLICT = "error.conflict"`,
   - `FAILURE = "error.failure"`,
   - `UNAUTHORIZED = "error.unauthorized"`,
   - `FORBIDDEN = "error.forbidden"`.
   Использовать их в `code ?? ...`.
3. Обновить `ErrorTests` под новые значения. Это осознанное изменение fallback-кодов (они не входят в `ApiErrorCodes` и срабатывают только при `code == null`); зафиксировать это в отчёте.
4. Проверить, что `Error` по-прежнему `record`, а `ErrorType` не меняется.

**Критерии приёмки:**
- `git grep -n "(R-50)"` находит только комментарии/доки, не строки логов;
- fallback-коды имеют формат `{aggregate}.{detail}` и не пересекаются с `ApiErrorCodes`;
- `ErrorTests` зелёные, build зелёный.

**Вне рамок:** остальные сообщения, `ApiErrorCodes`, коды домена.

---

## Порядок выполнения

S1 → S2 → S3 → S4 → S5 → S6 → S7 → S8. Каждая сессия начинается с чистого `git status` (кроме изменений предыдущих сессий — они остаются в рабочем дереве) и заканчивается отчётом. Следующая сессия не должна переделывать предыдущую.
