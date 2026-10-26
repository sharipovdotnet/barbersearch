# DEPLOY — деплой-срез BarberSearch

> Дата: 2026-10-21. Ветка: `features/issue-5`. Срез закрывает эксплуатационные риски
> контейнера и обратного прокси: Data Protection, ключ подписи и его ротацию,
> `ForwardedHeaders`, CORS/cookie, health checks, валидацию конфигурации и graceful shutdown.
> Фактический выкат в прод этим срезом **не выполняется**.

## 1. Что обязательно задать

Секреты в образ и в git не попадают. Значения ниже приходят из секрет-стора или
переменных окружения; `appsettings.json` содержит только несекретные значения по умолчанию.

| Переменная | Обязательна | Назначение |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | да | `Production` вне локальной разработки. В `Development` включаются dev-ключи и Swagger. |
| `ASPNETCORE_URLS` | да | Адрес Kestrel, например `http://0.0.0.0:8080`. |
| `AllowedHosts` | да вне локальной разработки | Публичные имена хостов через `;`, без схемы, порта и пути, например `api.example.com;www.example.com`. В `appsettings.json` по умолчанию только `localhost;127.0.0.1`. `*` и пустой список роняют старт в `Production`; неизвестный `Host` получает `400`. |
| `ConnectionStrings__Postgres` | да | Строка подключения PostgreSQL. |
| `Auth__SigningKey` | да | Текущий ключ подписи JWT, не короче 32 символов. Пустое/короткое значение — падение на старте. |
| `Auth__PreviousSigningKeys__0`, `__1`, … | при ротации | Предыдущие ключи, которые ещё принимаются при проверке. Новые токены подписываются только текущим. |
| `DataProtection__KeysPath` | да | Каталог key ring на постоянном томе. В образе ключей быть не должно. |
| `DataProtection__CertificatePath` | нет | PKCS#12-сертификат для шифрования ключей на диске. Пароль — `DataProtection__CertificatePassword` из секрет-стора. |
| `Auth__CookieSameSite` | да для cross-site SPA | `Lax` (same-site), `Strict` или `None` (cross-site). `None` требует `Cors__AllowedOrigins` и принудительно включает `Secure`. |
| `Cors__AllowedOrigins__0`, `__1`, … | при cross-site SPA | Явные origins, например `https://app.example.com`. `*` запрещён: политика включает credentials. |
| `ForwardedHeaders__Enabled` | при работе за прокси | `true`, если перед API стоит доверенный прокси/балансировщик. |
| `ForwardedHeaders__KnownProxies__0`, … | при `Enabled=true` | IP-адреса своих прокси. Без них старт падает. |
| `ForwardedHeaders__KnownNetworks__0`, … | при `Enabled=true` | CIDR-сети прокси, например `10.0.0.0/8`. |
| `ForwardedHeaders__ForwardLimit` | нет | Сколько прокси в цепочке обрабатывать; по умолчанию `1`. |
| `HealthChecks__DatabaseTimeoutSeconds` | нет | Таймаут readiness-проверки БД; по умолчанию `5`. |
| `Deployment__ShutdownTimeoutSeconds` | нет | Сколько ждать in-flight запросы при остановке; по умолчанию `30`. |
| `RefreshTokens__CleanupIntervalMinutes` | нет | Интервал фоновой уборки истёкших refresh-токенов в минутах, `1..1440`; по умолчанию `60`. |
| `ASPNETCORE_HTTPS_PORT` | только если API сам делает redirect | Порт для `UseHttpsRedirection`. За прокси с `X-Forwarded-Proto: https` redirect не нужен. |

`Auth__SigningKey` и `DataProtection__CertificatePassword` **никогда** не логируются и не
пишутся в `appsettings*.json`. Ключ подписи не генерируется приложением: без него старт
падает в любом окружении.

### 1.1 Локальная разработка

`appsettings.json` намеренно не содержит рабочей строки подключения: `ConnectionStrings:Postgres`
там пустая, чтобы пароль не попадал в git. Секреты задаются один раз из каталога Web-проекта
(`UserSecretsId` уже прописан в `BarberSearch.Web.csproj`, поэтому `dotnet user-secrets`
работает без отдельной инициализации):

```powershell
cd BarberSearch/src/BarberSearch.Web
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=barbersearch;Username=postgres;Password=postgres"
dotnet user-secrets set "Auth:SigningKey" "<at least 32 chars>"
```

Альтернатива — переменные окружения процесса: `ConnectionStrings__Postgres` и
`Auth__SigningKey`. Их используют деплой, smoke и load-тесты, поэтому менять
`appsettings*.json` под конкретную машину не нужно. Значения из user secrets и переменных
окружения не коммитьте и не выводите в логи.

## 2. Тома и Data Protection

- Смонтируйте постоянный том и задайте `DataProtection__KeysPath` (например,
  `/var/lib/barbersearch/data-protection`). Каталог должен быть **записываемым**:
  приложение проверяет это на старте.
- Все инстансы API используют **один** каталог и одно имя приложения
  (`SetApplicationName("BarberSearch")`). Тогда cookie и antiforgery, выданные одним
  инстансом, принимаются другим, а сессии переживают перезапуск.
- Не кладите каталог внутрь образа и не публикуйте его. В репозитории он тоже не нужен:
  dev-путь `../../../.data-protection-keys` игнорируется git.
- Ключи на диске по умолчанию не шифруются: защита — ACL тома. Если требуется шифрование,
  задайте `DataProtection__CertificatePath` и `DataProtection__CertificatePassword`;
  сертификат хранится в секрет-сторе, а не в репозитории. Смена такого сертификата требует
  отдельного плана: старые ключи, зашифрованные прежним сертификатом, должны оставаться
  читаемыми, пока живы выданные cookie/antiforgery-токены.
- Резервная копия key ring — вместе с БД: без ключей пользователи потеряют сессии.
  Восстановление key ring на новом инстансе допустимо только до истечения старых cookie.

## 3. Прокси и HTTPS

Прокси должен:

1. Принимать TLS и передавать `X-Forwarded-Proto: https`.
2. Передавать `X-Forwarded-For` с адресом клиента (не подменять его клиентским значением).
3. **Удалять** входящие `X-Forwarded-*` от клиента перед добавлением своих.
4. Сохранять исходный заголовок `Host` клиента: приложение принимает только хосты из
   `AllowedHosts`, а неизвестный `Host` получает `400`, поэтому подмена на адрес upstream
   сделает API недоступным.

Приложение:

- обрабатывает только `X-Forwarded-For` и `X-Forwarded-Proto`; `X-Forwarded-Host`
  намеренно не включён;
- принимает только хосты из `AllowedHosts`: запрос с неизвестным `Host` получает `400`
  ещё до маршрутизации и аутентификации;
- доверяет заголовкам только от `KnownProxies`/`KnownNetworks`; по умолчанию список пуст,
  поэтому заголовки от произвольного клиента игнорируются;
- ставит `UseForwardedHeaders` до HTTPS-redirect, rate limiter и auth;
- вне `Development` включает HSTS. Для `localhost`, `127.0.0.1` и `[::1]` ASP.NET Core
  HSTS не отдаёт — это ожидаемо, проверяйте на реальном hostname;
- за прокси с корректным `X-Forwarded-Proto: https` `UseHttpsRedirection` не срабатывает,
  поэтому цикла redirect нет.

Если API сам принимает TLS (без прокси), задайте `ASPNETCORE_HTTPS_PORT` и сертификат
Kestrel. В этом режиме forwarded headers можно не включать.

## 4. CORS и cookie

| Сценарий | CORS | `Auth__CookieSameSite` | Cookie |
|---|---|---|---|
| SPA на том же сайте | можно не включать | `Lax` (или `Strict`) | обычные, `Secure` вне Development |
| SPA на другом сайте | `Cors__AllowedOrigins__0=…` | `None` | `Secure`, `SameSite=None`, `HttpOnly`, `Path=/` |

- CORS-политика всегда включает `AllowCredentials`; поэтому wildcard `*` запрещён и
  отклоняется на старте. Перечисляйте только реальные origins, без пути и завершающего `/`.
- В Production cookie называются `__Host-barbersearch` и `__Host-barbersearch-refresh`.
  Префикс `__Host-` требует `Secure`, `Path=/` и отсутствия `Domain`.
- Refresh-cookie — `HttpOnly`, `Path=/`, срок `Auth:RefreshTokenDays`; access-cookie —
  `HttpOnly`, `Path=/`, `Auth:CookieHours`. Antiforgery-cookie тоже `HttpOnly` и следует
  политике `SameSite`/`Secure`.
- Refresh-cookie намеренно остаётся с `Path=/` (R-49): этот путь требует префикс `__Host-`,
  и сузить его до `/api/auth`, сохранив префикс, нельзя. Принятая цена — cookie уходит на
  каждый запрос того же origin; границей доверия `Path` не является, а `HttpOnly` + `Secure` +
  `SameSite` не дают прочитать её скриптам и утечь токену в сторонний CDN или логи (в проекте
  таких нет). Пересмотреть, если публичные пути переедут на отдельный origin/CDN или `__Host-`
  будет снят по другой причине: тогда `Path=/api/auth` (или отдельный cookie-хост) вместе с
  миграцией имени.
- `Auth:CookieSameSite=None` без `Cors:AllowedOrigins` — ошибка старта: cross-site SPA без
  CORS всё равно не сможет вызвать API.
- Не расширяйте CORS «на всякий случай»: каждый лишний origin — это ещё один сайт, которому
  браузер разрешает credentials-запросы.

## 5. Health checks и graceful shutdown

| Проверка | Что означает | Ответ |
|---|---|---|
| `GET /health/live` | процесс жив, БД не проверяется | `200` + JSON `{"status":"Healthy","checks":[]}` |
| `GET /health/ready` | PostgreSQL доступна (`CanConnectAsync`) | `200` Healthy / `503` Unhealthy |

Оба эндпоинта анонимные, не требуют cookie и не попадают под fallback-политику.
Readiness-проверка ограничена `HealthChecks__DatabaseTimeoutSeconds` и не выводит детали
исключения (в строке подключения может быть пароль).

```bash
curl -fsS http://127.0.0.1:8080/health/live
curl -fsS http://127.0.0.1:8080/health/ready
```

Kestrel при остановке перестаёт принимать новые соединения и ждёт in-flight запросы не
дольше `Deployment__ShutdownTimeoutSeconds`. В Kubernetes `terminationGracePeriodSeconds`
должен быть **больше** этого таймаута (например, 45 при 30), иначе pod добьют раньше, чем
запросы завершатся. Liveness/readiness probes направляйте на `/health/live` и
`/health/ready`; readiness снимает pod с балансировщика, liveness перезапускает только
мёртвый процесс.

### 5.1 Уборка refresh-токенов (R-50)

Истёкшие refresh-токены удаляет фоновый сервис приложения: первый прогон — сразу при старте,
далее каждые `RefreshTokens:CleanupIntervalMinutes` минут (по умолчанию 60, допустимо 1..1440).
Удаляются строки, истёкшие более 30 дней назад (`RefreshToken.MAX_LIFETIME_DAYS`): при таком
запасе потомки строки уже истекли, и `ON DELETE SET NULL` не обнуляет ссылку живого токена.
Ручных действий не требуется. Несколько инстансов могут убирать одновременно — `DELETE`
идемпотентен; отдельного cron/DB job нет, поэтому пока все инстансы остановлены, уборка
не выполняется и догоняет при следующем старте.

## 6. Ротация ключа подписи

Новые access-токены подписываются только `Auth__SigningKey`. Предыдущие ключи нужны, чтобы
дожить уже выданные токены (по умолчанию 15 минут + `ClockSkew` 30 секунд).

1. Сгенерируйте новый ключ (≥ 32 символов) в секрет-сторе.
2. Задеплойте все инстансы с:
   - `Auth__SigningKey=<new>`
   - `Auth__PreviousSigningKeys__0=<old>`
3. Убедитесь, что все инстансы поднялись и `/health/ready` зелёный. В это время новые
   токены идут с новым `kid`, старые принимаются по предыдущему ключу.
4. Через 15–20 минут (после истечения access-токенов) удалите
   `Auth__PreviousSigningKeys__0` и задеплойте снова.
5. Refresh-токены и cookie от ключа подписи не зависят, поэтому их сроки на ротацию не
   влияют; security stamp и ownership не меняются.

Data Protection key ring ротируется автоматически (новый ключ раз в 90 дней); старые ключи
остаются для расшифровки. Не удаляйте их вручную, пока живы cookie/antiforgery-токены,
выданные с их использованием.

## 7. Проверка перед выкатом

```powershell
# сборка и тесты — AGENT.md §5.2/§5.3, danger-full-access
dotnet build BarberSearch/BarberSearch.sln
# тесты: ожидается 184 domain + 212 application + 71 infrastructure + 10 integration

# миграции: design-time factory читает ConnectionStrings__Postgres
$env:ConnectionStrings__Postgres = 'Host=127.0.0.1;Port=5432;Database=barbersearch;Username=postgres;Password=postgres'
dotnet ef database update --project BarberSearch/src/BarberSearch.Infrastructure.Postgresql `
  --startup-project BarberSearch/src/BarberSearch.Web

# R-02: FK добавлены как NOT VALID. Проверить старые висячие ссылки; после очистки
# выполнить VALIDATE CONSTRAINT из конца скрипта.
& 'C:\Program Files\PostgreSQL\16\bin\psql.exe' -h 127.0.0.1 -p 5432 -U postgres `
  -d barbersearch -f deploy/find-dangling-references.sql
```

Локальный smoke (отдельная БД `barbersearch_deploy_smoke`, API запускается и
останавливается скриптом):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File deploy/smoke.ps1
```

Скрипт проверяет cookie-вход, переживание рестарта, ротацию refresh, logout, смену пароля,
создание брони, rate limit по `X-Forwarded-For`, доверие только своим прокси, HTTPS-redirect,
HSTS, CORS-preflight с credentials, атрибуты `SameSite=None; Secure; HttpOnly; Path=/`,
`/health/live`/`/health/ready` (включая недоступную БД), host filtering (неизвестный `Host`
→ `400`) и fail-fast на неверной конфигурации (включая wildcard `AllowedHosts`).
Доказательства — `deploy/generated/smoke.txt` (git-ignored).

Минимальный ручной чек-лист после выката:

1. `GET /health/live` и `GET /health/ready` — `200`.
2. `GET /api/auth/csrf`, `POST /api/auth/login/cookie` — cookie с ожидаемыми атрибутами.
3. Перезапустить один pod; старый cookie всё ещё даёт `GET /api/auth/me` → `200`.
4. Сделать 11 неудачных логинов с одного реального IP за минуту — 11-й `429`.
5. Preflight `OPTIONS` с `Origin` SPA и `Access-Control-Request-Method` — `204` и
   `Access-Control-Allow-Credentials: true`.
6. `POST /api/bookings` с cookie-сессией — `201`.

## 8. Границы среза

- Фактический выкат в прод, настройка конкретного прокси/оркестратора и секрет-стора —
  вне этого среза; здесь только конфигурация, код и проверки.
- Booking headroom (`p95` 257.5–304.0 ms при бюджете 300) остаётся отдельной задачей
  slot-claim/fast-path; correctness-фикс не переделывался.
- Кэш/Redis/output caching не добавлялись: измерения 2026-10-20 не дают основания.
- Открытые дефекты вне Web-слоя перечислены в `HANDOFF.md` §2; дублирование reorder фото
  закрыто 2026-10-25 (R-44) и в деплой-срез не входит.

## 9. Сборка и запуск контейнера (R-37)

Образ API собирается из `backend/` (контекст — `backend`, Dockerfile — `backend/Dockerfile`):

```powershell
cd backend
docker build -t barbersearch-api:local -f Dockerfile .
docker run --rm -p 18080:8080 `
  -e Auth__SigningKey='<32+ символов>' `
  -e DataProtection__KeysPath=/tmp/keys `
  -e ConnectionStrings__Postgres='Host=host.docker.internal;Port=5432;Database=barbersearch;Username=postgres;Password=postgres' `
  barbersearch-api:local
curl.exe -fsS http://127.0.0.1:18080/health/live
```

- Multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0` собирает и публикует
  `BarberSearch/src/BarberSearch.Web` в Release, в рантайм попадает только
  `mcr.microsoft.com/dotnet/aspnet:10.0` с published-файлами.
- Процесс запускается от непривилегированного `$APP_UID` (uid 1654), не от root; `EXPOSE 8080`,
  точка входа — `dotnet BarberSearch.Web.dll` (без apphost). Адрес Kestrel задаёт
  `ASPNETCORE_URLS` (в образе `http://+:8080`).
- В образ не попадают ключи Data Protection, тесты, `bin/obj` и результаты smoke-прогонов —
  это исключает `backend/.dockerignore`. Key ring монтируется постоянным томом, путь задаётся
  `DataProtection__KeysPath`.
- Обязательные переменные — как в §1: минимум `Auth__SigningKey` (≥ 32 символов),
  `ConnectionStrings__Postgres` и `DataProtection__KeysPath`. Без доступной БД
  `GET /health/live` отвечает `200`, а `GET /health/ready` — `503` (это ожидаемо).
- CI — `.github/workflows/backend-ci.yml` в корне репозитория: restore, Release-сборка,
  unit-тесты (domain/application/infrastructure) и интеграционные тесты на `ubuntu-latest`
  (Testcontainers, Docker доступен). Секреты не нужны. Workflow в этом срезе на GitHub
  **не запускался**; локально проверены только YAML, пути и версии.
