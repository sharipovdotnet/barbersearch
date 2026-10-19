# HANDOFF — следующая сессия

> Прочитай этот файл и `AGENT.md` §17 перед любыми изменениями.
> Дата: 2026-10-19. Ветка: `features/issue-4`. Git root: `D:\Projects\barbersearch`
> (workspace — `backend/`, репозиторий на уровень выше).

## 0. Где мы сейчас

Аутентификация закоммичена (`9c6c75f feat(auth): add cookie and JWT authentication`).
Поверх неё идёт **авторизация по владению** — шаги 1–3 из плана сделаны, но **не закоммичены**;
рабочее дерево содержит их целиком. Сборка: 7 проектов, 0 ошибок, 0 предупреждений.

Сделано в этой сессии:

1. **Фундамент.** `ICurrentUser` (`UserId`, `IsAdmin`) в Application; `HttpContextCurrentUser`
   в Web; `EnsureCanManage(ownerId)` → `403 auth.forbidden`; `IsOwnedByAsync` в
   `IBarberProfileRepository` (один `EXISTS`-запрос, без загрузки агрегата); DI-регистрация.
2. **Создание.** `UserId` убран из `CreateBookingRequest`, `CreateReviewRequest`,
   `BarberProfileCreateRequest`. Обработчики берут автора из `ICurrentUser`. Отзыв можно
   оставить только по **своей завершённой** брони (`403` чужому, `409` если не `Completed`).
   `POST api/barber-profiles` закрыт политикой `BarberOnly`.
3. **Каталог.** `Salons`, `HairServices`, `Specializations` — классовый
   `[Authorize(Policy = AdminOnly)]`, все 13 действий, включая создание.

### 0.1 Что изменено в рабочем дереве

Новые файлы:

```
BarberSearch/src/BarberSearch.Application/Abstractions/ICurrentUser.cs
BarberSearch/src/BarberSearch.Application/Common/CurrentUserExtensions.cs
BarberSearch/src/BarberSearch.Web/Auth/HttpContextCurrentUser.cs
```

Изменены: `IBarberProfileRepository.cs`, `BarberProfileRepository.cs`,
`CreateBookingHandler.cs`, `CreateReviewHandler.cs`, `CreateReviewCommand.cs`,
`CreateBarberProfileHandler.cs`, три Request-контракта, `BarberProfilesController.cs`,
`ReviewsController.cs`, `SalonsController.cs`, `HairServicesController.cs`,
`SpecializationsController.cs`, `DependencyInjection.cs`, `BarberSearch.Web.http`,
`BarberProfileTests.cs`.

## 1. Решения по доступу (зафиксированы 2026-10-19)

| Вопрос | Решение |
|---|---|
| `Publish`/`Hide` отзыва | только admin |
| `Cancel` брони | клиент и барбер (admin обходит) |
| Каталог `Salons`/`HairServices`/`Specializations` | только admin, включая создание |
| Создание профиля барбера | только роль `Barber` |

Отдельно: создание отзыва **не** обходит проверку для admin — иначе появились бы фиктивные
отзывы. Автор отзыва всегда клиент брони.

## 2. Матрица доступа

| Ресурс | Создание | Изменение | Только admin |
|---|---|---|---|
| `Salons`, `HairServices`, `Specializations` | admin | admin | create / update / activate |
| `BarberProfile` | роль `Barber`, `UserId = sub` | владелец профиля | `Verify`, `Unverify`, `ChangeGrade`, `UpdateRating` |
| Дети профиля (фото, услуги, салоны, график, отпуска) | владелец профиля | владелец профиля | — |
| `Booking` | клиент, `UserId = sub` | `Confirm`/`Complete`/`NoShow`/`BarberNote` — барбер; `Cancel` — клиент или барбер | — (admin обходит) |
| `Review` | клиент по своей завершённой брони | автор | `Publish`/`Hide` |
| `User` | `register` публично / admin | сам; роль — admin | роль, активация, верификация |

Правила: `404` — ресурса нет; `403` — ресурс есть, но чужой; аноним получает `401` на политике.
Проверка владения идёт **в обработчике, после загрузки агрегата**, а не в
`IAuthorizationHandler`: обработчик уже загрузил агрегат, и проверка не стоит второго запроса.
`admin` обходит проверку через `ICurrentUser.IsAdmin` (claim роли, поэтому после смены роли
старый токен живёт до истечения — известный долг §17.7 п.8).

## 3. Следующая задача: шаг 4 — профиль барбера и его дети

Всё, что осталось в этом шаге, — 30 обработчиков. После `GetByIdAsync` добавить:

```csharp
var profile = profileResult.Value;

var allowed = _currentUser.EnsureCanManage(profile.UserId);
if (allowed.IsFailure)
{
    return allowed.Error;
}
```

и инжектировать `ICurrentUser` в конструктор. Обработчики уже грузят агрегат — кроме
`CreateBarberProfileHandler` и `CreateBookingHandler`, которые закрыты в шаге 2.

- **Только admin (атрибут на действии, обработчик не трогаем):**
  `PUT {id}/grade`, `PUT {id}/rating`, `POST {id}/verification`, `DELETE {id}/verification`.
- **Владелец или admin (17 действий `BarberProfilesController`):** `Update`, `Activate`,
  `Deactivate`, `AddPhoto`, `RemovePhoto`, `ReorderPhoto`, `AddSalon`, `RemoveSalon`,
  `SetPrimarySalon`, `FindSalon` (чтение), `AddService`, `RemoveService`,
  `AddSpecialization`, `RemoveSpecialization`, `AddTimeOff`, `RemoveTimeOff`.
- **Дети:** `PortfolioPhotosController` — 4, `BarberServicesController` — 4,
  `BarberSalonsController` — 3, `WorkingSchedulesController` — 2. Везде тот же паттерн.

Замечания:

- `FindSalon` — чтение; пока закрываем владельцем/admin, публичная карточка появится позже.
- `Activate`/`Deactivate` по матрице — владелец; если решим, что деактивация только admin,
  это одно слово в матрице и один атрибут.
- `UpdateRating` — вычисляемое значение, публичный эндпоинт вообще лишний; пока оставляем
  admin, кандидат на удаление.

## 4. Оставшиеся шаги

5. **Брони (6 обработчиков).** `Confirm`, `Complete`, `MarkNoShow`, `AddBarberNote` — барбер
   профиля из брони (`booking.BarberProfileId` + `IBarberProfileRepository.IsOwnedByAsync`);
   `Cancel` — клиент (`booking.UserId`) или барбер; admin обходит.
6. **Отзывы (4).** `Update` — автор или admin; `Publish`/`Hide` — `[Authorize(Policy = AdminOnly)]`.
   `Create` уже закрыт в шаге 2.
7. **Пользователи (3) и чистка.** `UpdateProfile`, `AddFavorite`, `RemoveFavorite` — проверка
   внутри обработчика; убрать `IsCurrentUser`/`IsAdmin` из `ApiController.cs` и вызовы из
   `UsersController`.
8. **Тесты.** Новый `tests/BarberSearch.Application.UnitTests` (в `.sln` добавлять вручную,
   `AGENT.md` §5.1): фейковые репозитории + `ICurrentUser`; 15–20 тестов на `403`/`404`/admin/
   владельца, авторство из токена, отзыв только по завершённой брони. Плюс смоук на живой
   PostgreSQL двумя пользователями и, опционально, reflection-тест на `AdminOnly` у каталога.
9. **Документация и коммит.** Закрыть §17.7 п.2, добавить §17.8 с матрицей, обновить §8/§16;
   коммит в стиле `feat(auth): enforce resource ownership`.

## 5. Критерии готовности всей задачи

- Ни один обработчик, меняющий ресурс, не читает `UserId` из тела.
- Каталог пишется только admin.
- Чужой `barberProfileId`/`bookingId`/`reviewId` → `403`, несуществующий → `404`, аноним → `401`.
- Сборка 0/0, тесты в обычном shell зелёные, смоук на двух пользователях зелёный.

## 6. Как запускать и проверять

```powershell
# сборка (в workspace-write падает без диагностики — нужен danger-full-access, AGENT.md §5.2)
dotnet build BarberSearch/BarberSearch.sln

# запуск: content root = каталог проекта, EventLog приглушён
cd BarberSearch/src/BarberSearch.Web
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Logging__EventLog__LogLevel__Default='None'
$env:ASPNETCORE_URLS='http://127.0.0.1:5243'
dotnet bin/Debug/net10.0/BarberSearch.Web.dll
```

`dotnet test` в этом харнессе падает на `testhost` (`Win32Exception 5`, AGENT.md §5.2) —
прогонять в обычном shell. Приложению нужен доступ на запись в
`%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`, иначе cookie-вход падает.

## 7. После ownership

1. **Refresh-токены** (0,5–1 день): таблица `refresh_tokens` (`id`, `user_id`, `token_hash`,
   `expires_at`, `revoked_at`, `replaced_by`), ротация при каждом refresh, детект повторного
   использования, отзыв при logout/смене пароля/роли/деактивации. Access — 15 минут,
   refresh — 7–30 дней, в БД только SHA-256.
2. **Read-эндпоинты** — API пока write-only.
3. **Деплой:** персистентные ключи Data Protection, `Auth__SigningKey` в секрет-сторе,
   `UseForwardedHeaders`, `Cors:AllowedOrigins` + `SameSite=None`.

## 8. Технический долг auth (AGENT.md §17.7)

1. Refresh-токены отсутствуют.
2. Владение: шаги 1–3 готовы, шаг 4 (профиль и дети) — следующий.
3. Ключи Data Protection не персистентны.
4. CORS для cross-site SPA не настроен (`Cors:AllowedOrigins` пуст).
5. Rate limiting за прокси без `UseForwardedHeaders` делит лимит на всех.
6. Подтверждение email/телефона административное.
7. `POST api/users/{id}/logins` оставлен только для admin.
8. `GET api/auth/me` и `IsAdmin` читают claims: после смены роли старый токен отдаёт старую
   роль до истечения (15 минут JWT / 8 часов cookie). Лечится security stamp'ом позже.
9. `CreateBookingRequest.Price` приходит от клиента — цену надо брать из услуги барбера
   (отдельная задача, не владение).
10. `barberServiceId` не проверяется на принадлежность профилю (AGENT.md §16.3).

## 9. Промпт для новой сессии

```text
Прочитай backend/HANDOFF.md и AGENT.md §17. Продолжаем авторизацию по владению с шага 4:
проверки владельца профиля барбера и его дочерних сущностей (30 обработчиков), четыре
admin-only действия (Verify/Unverify/ChangeGrade/UpdateRating). Изменения шагов 1–3 лежат
в рабочем дереве, не закоммичены. Сборка — через danger-full-access.
```
