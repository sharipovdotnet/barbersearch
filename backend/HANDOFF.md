# HANDOFF — следующая сессия

> Прочитай `AGENT.md` §17 (auth и владение) перед изменениями.
> Дата: 2026-10-19. Ветка: `features/issue-4`. Git root: `D:\Projects\barbersearch`
> (workspace — `backend/`).

## 0. Где мы сейчас

Аутентификация и **авторизация по владению** готовы и закоммичены
(`7f7f9ce feat(auth): enforce resource ownership`). Сборка: **8 проектов, 0 ошибок,
0 предупреждений**.
Тесты: **130 domain + 28 application**, все зелёные.

Что сделано:

1. **Аутентификация** — cookie + JWT, политики `AdminOnly`/`BarberOnly`/`VerifiedEmail`,
   antiforgery, rate limiting (§17).
2. **Владение** — проверки в обработчиках после загрузки агрегата:
   29 обработчиков профиля барбера и его детей, 5 обработчиков броней, `UpdateReview`,
   3 обработчика пользователей; admin обходит проверку. Четыре admin-действия профиля
   (`Verify`, `Unverify`, `ChangeGrade`, `UpdateRating`) и `Publish`/`Hide` отзыва закрыты
   атрибутом на действии. Матрица — `AGENT.md` §17.8.
3. **Тесты** — `tests/BarberSearch.Application.UnitTests`: фейковые репозитории + `ICurrentUser`,
   28 `[Fact]` на `403`/`404`/admin/владельца, авторство из токена, отзыв только по завершённой
   брони. Прогон — через reflection-раннер (`AGENT.md` §5.3), потому что `dotnet test` в харнессе
   падает на `testhost`. Смоук на локальной PostgreSQL двумя пользователями пройден:
   свой профиль `204`, чужой `403`, своё избранное `204`, чужое `403`, admin-действие под
   Customer `403`.

## 1. Следующая задача — refresh-токены (0,5–1 день)

Access живёт 15 минут, поэтому мобильному клиенту нужен refresh с ротацией.

**Схема.** Новая таблица `refresh_tokens`:

| Колонка | Смысл |
|---|---|
| `id` | PK (UUIDv7) |
| `user_id` | FK на `users`, каскадное удаление |
| `token_hash` | SHA-256 от токена; открытый токен в БД не хранится |
| `expires_at` | 7–30 дней |
| `revoked_at` | `null`, пока токен жив |
| `replaced_by` | FK на строку, которой токен заменён при ротации |

**Правила.**

- Ротация при каждом refresh: старый помечается `revoked_at` + `replaced_by`, выдаётся новая пара.
- Повторное использование уже заменённого токена — отзыв всей цепочки (детект кражи).
- Отзыв при `logout`, смене пароля, смене роли и деактивации пользователя.
- Ответ `POST api/auth/refresh` — новый access + новый refresh; refresh — в HttpOnly-cookie,
  чтобы браузерная ветка не хранила его в JS.
- `Auth:RefreshTokenDays` в конфигурации; access остаётся 15 минут.

## 2. Технический долг auth (`AGENT.md` §17.7)

1. Refresh-токенов нет — это следующая задача.
2. ~~Владение ресурсом~~ — сделано 2026-10-19, см. §17.8.
3. Ключи Data Protection не персистентны: в контейнере без тома сессии умрут после рестарта.
4. CORS для cross-site SPA не настроен (`Cors:AllowedOrigins` пуст).
5. Rate limiting за прокси без `UseForwardedHeaders` делит лимит на всех.
6. Подтверждение email/телефона административное.
7. `POST api/users/{id}/logins` оставлен только для admin.
8. `IsAdmin` и `GET api/auth/me` читают claims: после смены роли старый токен отдаёт старую роль
   до истечения (15 минут JWT / 8 часов cookie). Лечится security stamp'ом.
9. `CreateBookingRequest.Price` приходит от клиента — цену надо брать из услуги барбера.
10. `barberServiceId` не проверяется на принадлежность профилю (`AGENT.md` §16, issue 3).

## 3. Как запускать и проверять

```powershell
# сборка (в workspace-write падает без диагностики — нужен danger-full-access, AGENT.md §5.2)
dotnet build BarberSearch/BarberSearch.sln

# тесты: dotnet test в харнессе блокирован (testhost), порядок обхода — AGENT.md §5.3
#   собрать тестовый проект, затем reflection-раннер из %TEMP% либо обычный developer shell

# запуск: content root = каталог проекта, EventLog приглушён
cd BarberSearch/src/BarberSearch.Web
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Logging__EventLog__LogLevel__Default='None'
$env:ASPNETCORE_URLS='http://127.0.0.1:5243'
dotnet bin/Debug/net10.0/BarberSearch.Web.dll
```

Локальная PostgreSQL 16.3 держит порт 5432, база `barbersearch`, строка подключения — в
`appsettings.json`. Приложению нужен доступ на запись в `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`,
иначе cookie-вход падает. Логин/регистрация ограничены 10 попытками в минуту на IP: смоук-скрипт
с несколькими пользователями легко ловит `429` — выдержи паузу между прогонами.

`git add`/`git commit` тоже требуют `danger-full-access`: в workspace-write падает на
`.git/index.lock` (`Permission denied`).

## 4. После refresh

1. **Read-эндпоинты** — API пока write-only.
2. **Деплой:** персистентные ключи Data Protection, `Auth__SigningKey` в секрет-сторе,
   `UseForwardedHeaders`, `Cors:AllowedOrigins` + `SameSite=None`.

## 5. Промпт для новой сессии

```text
Прочитай backend/HANDOFF.md и AGENT.md §17 (особенно §17.7–§17.8). Ownership уже закоммичен
(7f7f9ce), рабочее дерево чистое. Начинаем refresh-токены.

1. Сущность + таблица refresh_tokens (id, user_id, token_hash, expires_at, revoked_at,
   replaced_by), EF-конфигурация и миграция; в БД только SHA-256, срок 7–30 дней
   (Auth:RefreshTokenDays), access остаётся 15 минут.
2. IRefreshTokenRepository в Application, реализация в Infrastructure, DI.
3. POST api/auth/refresh: ротация при каждом refresh (старый — revoked_at + replaced_by),
   повторное использование заменённого токена отзывает всю цепочку; refresh для браузера —
   в HttpOnly-cookie, для внешнего клиента — в теле ответа.
4. Отзыв при logout, смене пароля, смене роли и деактивации пользователя.
5. Тесты в tests/BarberSearch.Application.UnitTests: ротация, reuse-detection, истечение,
   отзыв; плюс смоук на локальной PostgreSQL двумя пользователями.
6. Документация (§17) и коммит feat(auth): add refresh tokens.

Сборка, тесты и git (add/commit) — через danger-full-access: workspace-write блокирует
obj/bin и .git/index.lock. dotnet test в харнессе падает на testhost — прогон через
reflection-раннер, AGENT.md §5.3.
```
