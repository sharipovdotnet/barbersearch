# HANDOFF — следующая сессия

> Прочитай `AGENT.md` §15, §16 и §17.10 перед изменениями.
> Дата: 2026-10-19. Ветка: `features/issue-4`. Git root: `D:\Projects\barbersearch`
> (workspace — `backend/`). HEAD этой сессии — `feat(auth): add security stamp`;
> перед ним `c7bd27f feat(auth): add refresh tokens`, `origin/features/issue-4` запушен.

## 0. Где мы сейчас

Аутентификация, **авторизация по владению**, **refresh-токены** и **security stamp** готовы.
Сборка: **8 проектов, 0 ошибок, 0 предупреждений**.
Тесты: **139 domain + 40 application**, все зелёные через reflection-раннер (`AGENT.md` §5.3).

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
4. **Security stamp (эта сессия)** — `User.SecurityStamp`, `RotateSecurityStamp()` в
   `ChangePasswordHash` / `ChangeRole` / `Deactivate`; claim `security_stamp` в JWT и cookie;
   scoped `SecurityStampValidator` в `OnTokenValidated` / `OnValidatePrincipal`; миграция
   `AddSecurityStamp` (backfill `gen_random_uuid()::text`, CHECK на непустоту, DEFAULT снят).
   Смена пароля/роли и деактивация теперь отзывают уже выданный access-токен и cookie, а не
   только refresh. Механика и ограничения — `AGENT.md` §17.10.
5. **Тесты** — `tests/BarberSearch.Domain.UnitTests`: +9 на stamp (всего 139);
   `tests/BarberSearch.Application.UnitTests`: 40 без изменений.
6. **Смоук security stamp** на локальной PostgreSQL (2026-10-19, три пользователя, admin через
   SQL): login → change-password → старые Bearer/cookie/refresh `401`, новый login `200`;
   смена роли и деактивация — то же; `[AllowAnonymous]` со старым Bearer отвечает `200`;
   повторная деактивация stamp не меняет. Подробности — `AGENT.md` §17.6.

## 1. Следующая задача — read-эндпоинты (этап 2)

API пока почти write-only: один query-обработчик (`FindBarberProfileSalonQuery`). Каждый пункт —
рабочий вертикальный срез: **Contracts → Application → Infrastructure → Controller → тесты →
смоук**.

1. **`GET api/barbers`** — фильтры `city`, `specializationId`, `serviceId`, `date`;
   keyset-пагинация (не `OFFSET`), курсор opaque (base64), стабильный порядок
   `rating DESC NULLS LAST, id`; ответ `PagedResponse<BarberSearchItemResponse>` с `items`
   и `nextCursor`.
2. **`GET api/barbers/{id}`** — карточка: профиль, услуги, салоны, специализации, рейтинг;
   `404`, если профиля нет.
3. **`GET api/barbers/{id}/slots?date=&serviceId=&salonId=`** — свободные слоты из
   `WorkingSchedule` и `TimeOff` минус активные `Booking`; границы дня и таймзона — явно,
   без неявного UTC-сдвига.
4. **`GET api/bookings`** — только свои брони; admin видит все; пагинация; `404`/`403` по
   правилам §17.8.
5. **`GET api/reviews?barberProfileId=`** — только опубликованные; пагинация; без навигации
   в циклические графы.

Правила реализации:

- никаких `Include` коллекций в поиске — проекция сразу в DTO через `Select`;
- `AsNoTracking()` на всех read-путях;
- доменные сущности наружу не сериализуются — только response-DTO в `Contracts`;
- проверить индексы §15.2, недостающие добавить миграцией;
- **не использовать `dotnet sln add`** (§5.1);
- если семантика не задана (например, показывать ли неподтверждённых барберов или как сортировать
  null-рейтинг) — задать **один короткий вопрос**, не выдумывать.

Тесты и проверка:

- `Application.UnitTests`: query-обработчики на фейковых репозиториях (пустой результат,
  фильтры, границы пагинации, `404`);
- смоук на локальной PostgreSQL на заполненных данных: поиск с фильтрами, карточка, слоты,
  свои/чужие брони, отзывы;
- сборка 8 проектов, 0 ошибок, 0 предупреждений.

Документация: `AGENT.md` §8, §16 и этот файл.

Коммиты:

- `feat(api): add barber search and profile read endpoints`
- `feat(api): add slots, bookings and reviews read endpoints`

## 2. Технический долг auth (`AGENT.md` §17.7)

1. ~~Refresh-токенов нет~~ — сделано 2026-10-19, см. §17.9.
2. ~~Владение ресурсом~~ — сделано 2026-10-19, см. §17.8.
3. Фоновой уборки истёкших refresh-токенов нет (таблица растёт).
4. Две одновременные ротации одного refresh-токена не сериализованы на уровне БД
   (окно `SELECT`→`UPDATE`); при необходимости — `xmin` или условный `UPDATE`.
5. Ключи Data Protection не персистентны: в контейнере без тома сессии умрут после рестарта.
6. CORS для cross-site SPA не настроен (`Cors:AllowedOrigins` пуст).
7. Rate limiting за прокси без `UseForwardedHeaders` делит лимит на всех.
8. Подтверждение email/телефона административное.
9. `POST api/users/{id}/logins` оставлен только для admin.
10. ~~`IsAdmin` и `GET api/auth/me` читают claims после смены роли~~ — закрыто security stamp
    (§17.10). Осталось: `logout` stamp не ротирует, поэтому предъявленный access-токен доживает
    до 15 минут; перехеширование пароля при входе ротирует stamp и завершает другие сессии;
    валидатор тянет `favorite_barbers` через `GetByIdAsync`.
11. `CreateBookingRequest.Price` приходит от клиента — цену надо брать из услуги барбера.
12. `barberServiceId` не проверяется на принадлежность профилю (`AGENT.md` §16, issue 3).

## 3. Как запускать и проверять

```powershell
# сборка (в workspace-write падает без диагностики — нужен danger-full-access, AGENT.md §5.2)
dotnet build BarberSearch/BarberSearch.sln

# тесты: dotnet test в харнессе блокирован (testhost), порядок обхода — AGENT.md §5.3
#   собрать тестовый проект, затем reflection-раннер из %TEMP%;
#   после прогона удалить раннер и временные скрипты

# миграции (AddSecurityStamp уже применена к локальной базе)
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
прогонами (в смоуке stamp между волнами было 65 с) или разбей сценарий на окна.

`git add`/`git commit` тоже требуют `danger-full-access`: в workspace-write падает на
`.git/index.lock` (`Permission denied`).

## 4. После read-эндпоинтов

1. **Booking under contention + idempotency** — exclusion constraint уже есть, осталось
   поймать `23P01` и вернуть `409`; `IdempotencyKey` уже на сущности (§15.5).
2. **Review → rating recalculation** в Application (§12.2).
3. **Кэш** слотов/поиска/карточки — только после load test (§15.4).
4. **Деплой:** персистентные ключи Data Protection, `Auth__SigningKey` в секрет-сторе,
   `UseForwardedHeaders`, `Cors:AllowedOrigins` + `SameSite=None`.

## 5. Промпт для новой сессии

```text
Прочитай backend/HANDOFF.md и AGENT.md §15, §16, §17.10. Ветка features/issue-4, HEAD —
feat(auth): add security stamp, рабочее дерево чистое, origin запушен. Security stamp готов,
начинаем только этап 2 — read-эндпоинты.

Порядок: GET api/barbers (фильтры city/specializationId/serviceId/date, keyset-пагинация,
rating DESC NULLS LAST, id, opaque base64-курсор, PagedResponse<BarberSearchItemResponse>);
GET api/barbers/{id} (карточка, 404); GET api/barbers/{id}/slots?date=&serviceId=&salonId=
(WorkingSchedule + TimeOff минус активные Booking, границы дня и таймзона явно);
GET api/bookings (свои, admin — все, пагинация, 404/403 по §17.8);
GET api/reviews?barberProfileId= (только опубликованные, пагинация, без циклических графов).

Правила: Contracts → Application → Infrastructure → Controller → тесты → смоук; без Include
коллекций в поиске (проекция в DTO), AsNoTracking на read-путях, только response-DTO наружу,
проверить индексы §15.2 и добавить миграцией, не использовать dotnet sln add. Если семантика
не задана (неподтверждённые барберы, сортировка null-рейтинга) — один короткий вопрос.

Коммиты: feat(api): add barber search and profile read endpoints;
feat(api): add slots, bookings and reviews read endpoints.

Сборка/тесты/git — через danger-full-access; тесты — reflection-раннер §5.3, после прогона
удалить раннер и временные скрипты; сборка 8 проектов, 0 ошибок, 0 предупреждений; в конце
зелёные тесты, смоук на локальной PostgreSQL, чистое дерево и пуш ветки.
```
