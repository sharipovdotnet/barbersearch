# HANDOFF — следующая сессия

> Прочитай `AGENT.md` §15, §16 и §17 перед изменениями.
> Дата: 2026-10-21. Ветка: `features/issue-5`. Git root: `D:\Projects\barbersearch`
> (workspace — `backend/`). HEAD этого среза — `docs(deploy): add deployment runbook`;
> перед ним `test(deploy): add deployment smoke` и
> `feat(deploy): harden startup for containers and proxies`.
> `origin/features/issue-5` запушен.

## 0. Где мы сейчас

Аутентификация, авторизация по владению, refresh-токены, security stamp, read-эндпоинты,
**booking under contention + idempotency**, **review → rating recalculation**, **search-план**,
**load test** и **деплой-срез** готовы. Фактический выкат в прод не выполнялся.

Сборка: **8 проектов, 0 ошибок, 0 предупреждений**. Тесты: **139 domain + 100 application**,
все зелёные через reflection-раннер (`AGENT.md` §5.3). API: **13 контроллеров, 81 endpoint
(73 доменных + 8 auth)**; новых эндпоинтов контракта нет, добавлены только
`/health/live` и `/health/ready`. Baseline и узкие места — `LOADTEST.md`; кэш не добавлялся.
Runbook деплоя — `DEPLOY.md`.

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
     `ex_bookings_no_overlap` и отвечает `409 booking.add.overlap`, а не `500`.
   - Unique-индекс `ux_bookings_user_id_idempotency_key`; повтор с тем же ключом возвращает
     существующую бронь, проигравшая конкурентная вставка перечитывается по ключу.
   - Цена берётся из `BarberService.Price`; `barberServiceId` проверяется узким чтением и
     должен принадлежать профилю и быть активным.
   - `EndAt` по-прежнему приходит от клиента; расчёт от `DurationMinutes` сознательно не
     менялся.
7. **Review → rating recalculation (этап 4, 2026-10-19)** — `ReviewRatingRecalculator`,
   только опубликованные отзывы, транзакция `IUnitOfWork`, `SELECT … FOR UPDATE` против
   lost update; подробности — `AGENT.md` §12.2 и §15.5.
8. **Тесты** — `Application.UnitTests`: **100** `[Fact]`; `Domain.UnitTests` — **139**.
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
      DP path, CORS, forwarded headers, SameSite; без секретов в сообщениях. Graceful shutdown
      через `HostOptions.ShutdownTimeout` (`Deployment:ShutdownTimeoutSeconds`).
    - **Design-time factory** — `dotnet ef` больше не запускает `Program.cs`; читает
      `ConnectionStrings__Postgres`, поэтому миграции не требуют signing key и DP path.
    - **Смоук** — `deploy/smoke.ps1`: 102 проверки, все зелёные; доказательства —
      `deploy/generated/smoke.txt` (git-ignored). Проверены cookie-вход, переживание рестарта,
      общий key ring двух инстансов, ротация/повтор refresh, logout, смена пароля, бронь,
      rate limit по XFF, игнорирование XFF недоверенного прокси, HTTPS-redirect, HSTS,
      CORS-preflight с credentials, атрибуты `__Host-`/`SameSite=None`, health endpoints и
      fail-fast на шести некорректных конфигурациях.

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
3. Подтверждение email/телефона административное.
4. `POST api/users/{id}/logins` оставлен только для admin.
5. `logout` stamp не ротирует, поэтому предъявленный access-токен доживает до 15 минут;
   перехеширование пароля при входе ротирует stamp и завершает другие сессии; валидатор тянет
   `favorite_barbers` через `GetByIdAsync`.

Закрыто 2026-10-21: ключи Data Protection персистентны и общие для инстансов; CORS для
cross-site SPA настраивается явно; forwarded headers обрабатываются только от своих прокси,
поэтому rate limit считается по реальному IP клиента.

### 2.3 Booking create — осознанно вне среза

- `EndAt` не выводится из `BarberService.DurationMinutes`: клиент по-прежнему присылает конец
  интервала. Если это меняется, нужен отдельный законченный срез с тестами и документацией;
  цена и принадлежность услуги уже серверные.

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

## 3. Как запускать и проверять

```powershell
# сборка (в workspace-write падает без диагностики — нужен danger-full-access, AGENT.md §5.2)
dotnet build BarberSearch/BarberSearch.sln

# тесты: dotnet test в харнессе блокирован (testhost), порядок обхода — AGENT.md §5.3
#   собрать тестовый проект, затем reflection-раннер из %TEMP%;
#   после прогона удалить раннер и временные скрипты
#   ожидается: domain 139 / application 100, все зелёные

# миграции: design-time factory читает ConnectionStrings__Postgres
$env:ConnectionStrings__Postgres = 'Host=127.0.0.1;Port=5432;Database=barbersearch;Username=postgres;Password=postgres'
dotnet ef database update --project BarberSearch/src/BarberSearch.Infrastructure.Postgresql `
  --startup-project BarberSearch/src/BarberSearch.Web

# запуск: content root = каталог проекта, EventLog приглушён
cd BarberSearch/src/BarberSearch.Web
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Logging__EventLog__LogLevel__Default='None'
$env:ASPNETCORE_URLS='http://127.0.0.1:5243'
$env:Auth__SigningKey='<local key, >= 32 chars, never commit>'
# DataProtection:KeysPath в Development уже задан в appsettings.Development.json
dotnet bin/Debug/net10.0/BarberSearch.Web.dll
```

`Auth__SigningKey` теперь обязателен и в Development: без него приложение падает на старте.
Ключ подписи и пароль сертификата — только из секрет-стора/переменной окружения; в
`appsettings*.json` их нет.

Локальная PostgreSQL 16.3 держит порт 5432, база `barbersearch`, строка подключения — в
`appsettings.json`. Логин/регистрация/refresh/change-password ограничены 10 попытками в
минуту на IP: смоук с несколькими пользователями легко ловит `429` — выдержи паузу между
прогонами или разбей сценарий на окна. На `POST api/bookings` и review-эндпоинтах лимита нет.

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
Прочитай backend/HANDOFF.md и AGENT.md §15, §16, §17 (особенно §17.7, §17.9, §17.10)
и backend/DEPLOY.md. Ветка features/issue-5, HEAD — docs(deploy): add deployment runbook;
перед ним test(deploy): add deployment smoke и feat(deploy): harden startup for containers
and proxies. Рабочее дерево чистое, origin запушен.

Деплой-срез закрыт: Data Protection на постоянном томе, Auth__SigningKey обязателен и
ротируется, forwarded headers только от своих прокси, явный CORS + SameSite=None для
cross-site SPA, health checks, startup validation и graceful shutdown. Смоук 102 проверки
зелёный, сборка 8/0/0, тесты 139/100. Фактический выкат в прод не выполнялся.

Не трогай search, booking correctness, rating recalculation и read-эндпоинты без
необходимости. Кэш/Redis/output caching не начинать. Booking headroom (p95 257.5–304.0
при бюджете 300) — отдельный срез slot-claim/fast-path. Открытые дефекты — §2.1 и §2.6.

Сборка/тесты/git — через danger-full-access; тесты — reflection-раннер §5.3, после прогона
удалить раннер и временные скрипты; сборка 8 проектов, 0 ошибок, 0 предупреждений; все тесты
зелёные (139 domain + 100 application).
```
