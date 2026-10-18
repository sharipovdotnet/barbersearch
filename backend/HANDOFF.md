# HANDOFF — следующая сессия

> Прочитай этот файл и `AGENT.md` §17 перед любыми изменениями.
> Дата: 2026-10-19. Ветка: `features/issue-4`. Git root: `D:\Projects\barbersearch`
> (workspace — `backend/`, репозиторий на уровень выше).

## 0. Состояние рабочего дерева

Аутентификация реализована, проверена и закоммичена в `features/issue-4` (верхушка ветки,
`git log -1`). Ниже — состав этого коммита.

Изменённые файлы:

```
backend/AGENT.md
BarberSearch/src/Shared/Error.cs
BarberSearch/src/BarberSearch.Domain/Users/User.cs
BarberSearch/src/BarberSearch.Web/BarberSearch.Web.csproj
BarberSearch/src/BarberSearch.Web/Common/ApiController.cs
BarberSearch/src/BarberSearch.Web/Common/FailureProblemDetails.cs
BarberSearch/src/BarberSearch.Web/Controllers/UsersController.cs
BarberSearch/src/BarberSearch.Web/Program.cs
BarberSearch/src/BarberSearch.Web/appsettings.json
backend/tests/BarberSearch.Domain.UnitTests/UserTests.cs
```

Новые файлы:

```
BarberSearch/src/BarberSearch.Application/Users/IPasswordService.cs
BarberSearch/src/BarberSearch.Application/Users/PasswordVerification.cs
BarberSearch/src/BarberSearch.Application/Users/LoginUser/{LoginUserCommand,LoginUserHandler,AuthenticatedUser}.cs
BarberSearch/src/BarberSearch.Contracts/Auth/{Login,Register,Me,Csrf}/*.cs
BarberSearch/src/BarberSearch.Web/Auth/*.cs
BarberSearch/src/BarberSearch.Web/OpenApi/BearerSecuritySchemeTransformer.cs
BarberSearch/src/BarberSearch.Web/Controllers/AuthController.cs
```

Внешние изменения, которые не видны в git:

- В `BarberSearch.Web.csproj` добавлен `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.11`
  (тянет `Microsoft.IdentityModel.* 8.19.2` и `Microsoft.Bcl.Cryptography 10.0.2`).
- В машинном `%APPDATA%\NuGet\NuGet.Config` в `packageSourceMapping` добавлена строка
  `Microsoft.Bcl.Cryptography`. Без неё restore падает с `NU1100`. Это настройка машины,
  не репозитория.

## 1. Что уже работает

- Две схемы: cookie для браузера, JWT для мобильного/внешнего клиента; селектор `Smart`
  по заголовку `Authorization: Bearer …`.
- Эндпоинты: `POST api/auth/register|login|login/cookie|logout`, `GET api/auth/me|csrf`.
- Claims: `sub`, `email`, `role`, `email_verified`, `barber_profile_id`, `jti`;
  `MapInboundClaims = false`, `RoleClaimType = "role"`.
- Политики: `AdminOnly`, `BarberOnly`, `VerifiedEmail`; `FallbackPolicy` требует входа для всего,
  кроме `[AllowAnonymous]`.
- Пароль: `PasswordHasher<User>` (IdentityV3), rehash при `SuccessRehashNeeded`, одинаковая
  ошибка 401 на неизвестный email и неверный пароль, dummy-verify для выравнивания времени.
- Antiforgery только на cookie-ветке (`ValidateAntiforgeryHeaderAttribute`), rate limiting
  10/мин на IP для register/login.
- 401/403 middleware отдаются как `ProblemDetails`; в Swagger есть кнопка Authorize.
- Закрыты дыры: `POST api/users` и `ChangeRole` — только admin; публичная регистрация создаёт
  `Customer`; владение проверяется в `UsersController` (`IsCurrentUser`/`IsAdmin`).

Подробности — `AGENT.md` §17.

## 2. Что проверено

Сборка: 7 проектов, 0 ошибок, 0 предупреждений. Смоук на живой PostgreSQL:

```
me анонимно 401 · register 201 · login 200 · me Bearer 200 · POST api/users под Customer 403
битый JWT 401 · login/cookie 204 · me cookie 200 · logout 204 → me 401
login/cookie без CSRF 400 auth.csrf.invalid · OpenAPI securitySchemes=Bearer
12 попыток входа: 401 × 6, затем 429
```

`dotnet test` в этом харнессе по-прежнему падает на `testhost` (`Win32Exception 5`,
AGENT.md §5.2). Два новых теста на `ChangePasswordHash` добавлены и компилируются, но не
прогонялись — прогнать в обычном shell.

## 3. Следующая задача: авторизация по владению

Причина: fallback-политика проверяет только факт входа. Любой вошедший пользователь может
менять чужие ресурсы. Примеры:

- `POST api/bookings` принимает `UserId` в теле — бронь от чужого имени;
- `POST api/reviews` принимает `UserId` — отзыв от чужого имени;
- 21 действие `api/barber-profiles/{id}` — чужие фото, услуги, салоны, график, отпуска,
  `Verify`, `ChangeGrade`;
- `BookingsController` — подтверждение/завершение/отмена чужой брони;
- каталог `Salons`/`HairServices`/`Specializations` — создаёт и выключает кто угодно.

### 3.1 Матрица (defaults, если не поправят)

| Ресурс | Создание | Изменение | Только admin |
|---|---|---|---|
| `Salons`, `HairServices`, `Specializations` | admin | admin | activate/deactivate |
| `BarberProfile` | сам барбер (`UserId = sub`) | владелец профиля | `Verify/Unverify`, `ChangeGrade`, `UpdateRating` |
| Дети профиля: фото, услуги, салоны, график, отпуска | владелец профиля | владелец профиля | — |
| `Booking` | клиент, `UserId = sub` | подтверждение/завершение/no-show — барбер; отмена — обе стороны | — |
| `Review` | клиент по своему завершённому booking | автор | `Publish/Hide` — admin (default) |
| `User` | register (публично) / admin | сам; роль — admin | активация, верификация |

### 3.2 Реализация

1. `ICurrentUser` в `BarberSearch.Application` (`Id`, `Role`, `IsAdmin`), реализация в Web
   читает `HttpContext.User`. Проверки — в handler'ах, которые уже загружают агрегат;
   `IsCurrentUser` из `ApiController` после этого убрать.
2. Каталог — `[Authorize(Policy = AuthorizationPolicies.AdminOnly)]` на трёх контроллерах.
3. `UserId` убрать из `CreateBookingRequest`/`CreateBookingCommand`, `CreateReviewRequest`/
   `CreateReviewCommand` и `CreateBarberProfile*`: идентификатор берётся из `ICurrentUser`,
   а не из тела. Это ломает контракты и тесты — сделать осознанно.
4. Проверки владения: `profile.UserId == current.User.Id`, `booking.UserId == sub` (клиент)
   либо `profile.UserId == sub` (барбер), `review.UserId == sub`; admin обходит.
5. Тесты: чужие идентификаторы → 403 (не 404, если ресурс существует), admin-обход,
   аноним → 401.

### 3.3 Критерии готовности

- Ни один handler, меняющий ресурс, не принимает `UserId` из тела.
- Каталог пишется только admin.
- Чужой `barberProfileId`/`bookingId`/`reviewId` даёт 403 для аутентифицированного
  не-владельца и 401 для анонима.
- Сборка 0/0, смоук на два пользователя (владелец/чужой) зелёный.
- `AGENT.md` §17.7 обновлён, пункт про владение закрыт.

## 4. Решения, которые нужны до кода

1. `Publish`/`Hide` отзыва — admin (default) или автор?
2. `Cancel` брони — обе стороны (default) или только клиент?
3. Каталог `Salons`/`HairServices`/`Specializations` — только admin (default), включая создание?

## 5. После этого

1. **Refresh-токены** (0,5–1 день): таблица `refresh_tokens` (`id`, `user_id`, `token_hash`,
   `expires_at`, `revoked_at`, `replaced_by`), ротация при каждом refresh, детект повторного
   использования (отзыв всей цепочки), отзыв при logout/смене пароля/роли/деактивации.
   Access — 15 минут, refresh — 7–30 дней, в БД только SHA-256.
2. **Read-эндпоинты** — API сейчас write-only; нужны поиск, карточка барбера, слоты.
3. **Деплой**: персистентные ключи Data Protection, `Auth__SigningKey` в секрет-сторе,
   `UseForwardedHeaders` для rate limiting за прокси, `Cors:AllowedOrigins` + `SameSite=None`
   для cross-site SPA.

## 6. Как запускать и проверять

```powershell
# сборка (нужен danger-full-access: sandbox не даёт писать obj/bin)
dotnet build BarberSearch/BarberSearch.sln

# запуск: content root = каталог проекта, EventLog приглушён
cd BarberSearch/src/BarberSearch.Web
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Logging__EventLog__LogLevel__Default='None'
$env:ASPNETCORE_URLS='http://127.0.0.1:5243'
dotnet bin/Debug/net10.0/BarberSearch.Web.dll
```

Приложению нужен доступ на запись в `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`, иначе
cookie-вход падает с `UnauthorizedAccessException` (в обычном shell доступа достаточно).

## 7. Технический долг auth (AGENT.md §17.7)

1. Refresh-токены отсутствуют.
2. Владение проверено только в `UsersController`.
3. Ключи Data Protection не персистентны.
4. CORS для cross-site SPA не настроен (`Cors:AllowedOrigins` пуст).
5. Rate limiting за прокси без `UseForwardedHeaders` делит лимит на всех.
6. Подтверждение email/телефона административное.
7. `POST api/users/{id}/logins` оставлен только для admin (обычный вход пишет `LoginUserHandler`).
8. `GET api/auth/me` строит ответ из claims: после смены роли старый токен отдаёт старую роль
   до истечения срока.
