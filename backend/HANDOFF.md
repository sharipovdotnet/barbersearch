# HANDOFF — следующая сессия

> Прочитай `AGENT.md` §17 (auth, владение и refresh-токены) перед изменениями.
> Дата: 2026-10-19. Ветка: `features/issue-4`. Git root: `D:\Projects\barbersearch`
> (workspace — `backend/`).

## 0. Где мы сейчас

Аутентификация, **авторизация по владению** и **refresh-токены** готовы.
Сборка: **8 проектов, 0 ошибок, 0 предупреждений**.
Тесты: **130 domain + 40 application**, все зелёные через reflection-раннер (`AGENT.md` §5.3).
Последние коммиты: `7f7f9ce feat(auth): enforce resource ownership`,
`feat(auth): add refresh tokens` (эта сессия), `docs: prepare refresh-tokens handoff`.

Что сделано:

1. **Аутентификация** — cookie + JWT, политики `AdminOnly`/`BarberOnly`/`VerifiedEmail`,
   antiforgery, rate limiting (§17).
2. **Владение** — проверки в обработчиках после загрузки агрегата:
   29 обработчиков профиля барбера и его детей, 5 обработчиков броней, `UpdateReview`,
   3 обработчика пользователей; admin обходит проверку. Матрица — `AGENT.md` §17.8.
3. **Refresh-токены** — таблица `refresh_tokens` (миграция `AddRefreshTokens`), в БД только
   SHA-256, ротация с `revoked_at` + `replaced_by`, reuse-detection с отзывом всей цепочки,
   HttpOnly-cookie для браузера и тело для внешнего клиента, отзыв при logout / смене пароля /
   смене роли / деактивации. `POST api/auth/refresh`, `POST api/auth/change-password`.
   Детали — `AGENT.md` §17.9.
4. **Тесты** — `tests/BarberSearch.Application.UnitTests`: 28 на владение + 12 на refresh
   (ротация, reuse-detection, истечение, отзыв, границы срока, хранение только хеша).
   Смоук на локальной PostgreSQL двумя пользователями: ротация, reuse с отзывом цепочки,
   независимость второго пользователя, смена пароля, logout, cookie-ветка и повтор старой
   cookie — всё пройдено; в БД 8 строк, все `token_hash` по 64 hex.

## 1. Следующая задача — read-эндпоинты (1–2 дня)

API пока почти write-only: один query-обработчик (`FindBarberProfileSalonQuery`). Клиенту нужны
`GET api/barbers` (поиск), `GET api/barbers/{id}` (карточка), `GET api/barbers/{id}/slots`,
`GET api/bookings`, `GET api/reviews`. Это следующий крупный срез; при проектировании — §15.1–§15.6
(индексы, `AsNoTracking`, проекция в DTO, keyset-пагинация).

Параллельно — **security stamp**: после смены роли/деактивации старый access-токен живёт
15 минут, cookie — 8 часов. Refresh-токены уже отзываются, но access остаётся валидным до
истечения (`AGENT.md` §17.7 п. 8).

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
10. `IsAdmin` и `GET api/auth/me` читают claims: после смены роли старый токен отдаёт старую
    роль до истечения (15 минут JWT / 8 часов cookie). Лечится security stamp'ом.
11. `CreateBookingRequest.Price` приходит от клиента — цену надо брать из услуги барбера.
12. `barberServiceId` не проверяется на принадлежность профилю (`AGENT.md` §16, issue 3).

## 3. Как запускать и проверять

```powershell
# сборка (в workspace-write падает без диагностики — нужен danger-full-access, AGENT.md §5.2)
dotnet build BarberSearch/BarberSearch.sln

# тесты: dotnet test в харнессе блокирован (testhost), порядок обхода — AGENT.md §5.3
#   собрать тестовый проект, затем reflection-раннер из %TEMP% либо обычный developer shell

# миграция refresh-токенов (уже применена к локальной базе)
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
прогонами или разбей сценарий на окна.

`git add`/`git commit` тоже требуют `danger-full-access`: в workspace-write падает на
`.git/index.lock` (`Permission denied`).

## 4. После refresh

1. **Read-эндпоинты** — API пока write-only.
2. **Security stamp** — отзыв access-токенов при смене роли/пароля/деактивации.
3. **Уборка refresh_tokens** — фоновая задача или `DELETE ... WHERE expires_at < now()`.
4. **Деплой:** персистентные ключи Data Protection, `Auth__SigningKey` в секрет-сторе,
   `UseForwardedHeaders`, `Cors:AllowedOrigins` + `SameSite=None`.

## 5. Промпт для новой сессии

```text
Прочитай backend/HANDOFF.md и AGENT.md §17. Refresh-токены и ownership уже закоммичены,
рабочее дерево чистое. Начинаем read-эндпоинты: поиск барберов, карточка, слоты, брони,
отзывы. Плюс security stamp, чтобы смена роли/пароля/деактивации отзывала и access-токены.
Сборка и git — через danger-full-access; тесты — reflection-раннер, AGENT.md §5.3.
```
