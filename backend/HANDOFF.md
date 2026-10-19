# HANDOFF — следующая сессия

> Прочитай `AGENT.md` §15, §16 и §17.10 перед изменениями.
> Дата: 2026-10-19. Ветка: `features/issue-4`. Git root: `D:\Projects\barbersearch`
> (workspace — `backend/`). HEAD этой сессии — `feat(api): add slots, bookings and reviews read
> endpoints`; перед ним `9b3c6ba feat(api): add barber search and profile read endpoints`.
> `origin/features/issue-4` запушен.

## 0. Где мы сейчас

Аутентификация, авторизация по владению, refresh-токены, security stamp и **read-эндпоинты**
готовы. Сборка: **8 проектов, 0 ошибок, 0 предупреждений**.
Тесты: **139 domain + 83 application**, все зелёные через reflection-раннер (`AGENT.md` §5.3).
API: **13 контроллеров, 81 endpoint (73 доменных + 8 auth)**.

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
5. **Read-эндпоинты (этап 2)** — пять вертикальных срезов, Contracts → Application →
   Infrastructure → Controller → тесты → смоук:
   - `GET api/barbers` — фильтры `city`, `specializationId`, `serviceId`, `date`; keyset по
     `rating DESC NULLS LAST, id`; opaque base64url-курсор; `PagedResponse<BarberSearchItemResponse>`;
     только активные и подтверждённые барберы, только активные салоны.
   - `GET api/barbers/{id}` — карточка: профиль, активные услуги, активные салоны с расписанием,
     специализации, рейтинг; `404` для отсутствующего/неактивного/неподтверждённого профиля.
   - `GET api/barbers/{id}/slots?date=&serviceId=&salonId=` — слоты из `WorkingSchedule` минус
     `TimeOff` и активные брони; границы дня в `Salon.TimeZoneId`, в ответе `timeZoneId`,
     `dayStart`/`dayEnd` в UTC; без `salonId` — основной активный салон; без `serviceId` — шаг
     30 минут, иначе длительность услуги.
   - `GET api/bookings` — свои брони; admin видит все и может фильтровать по `userId`; чужой
     `userId` для не-admin — `403`, неизвестный клиент для admin — `404`; keyset по
     `start_at DESC, id`.
   - `GET api/reviews?barberProfileId=` — только опубликованные; keyset по `created_at DESC, id`;
     ответ без навигаций в циклический граф.
6. **Индексы** — миграция `AddReadIndexes` (2026-10-19): `ix_barber_profiles_search` (partial,
   `rating DESC NULLS LAST, id`), `ix_reviews_barber_profile_id_published_created_at`,
   `ix_bookings_start_at_id`. Применена к локальной PostgreSQL 16.3.
7. **Тесты** — `Application.UnitTests`: +43 (курсоры/пагинация, поиск, карточка, слоты, брони,
   отзывы), всего 83; `Domain.UnitTests` — 139 без изменений.
8. **Смоук read-эндпоинтов** на локальной PostgreSQL: поиск с каждым фильтром и второй страницей
   по `nextCursor`, битый курсор `400`; карточка и `404`; слоты с `TimeOff`, активной и
   отменённой бронью, выходным днём и явными границами дня; свои/чужие брони, admin и `userId`,
   `404`/`403`; только опубликованные отзывы, пагинация.

## 1. Следующая задача — booking under contention и рейтинг

Read-эндпоинты закрыты; дальше по §15.9:

1. **Booking under contention + idempotency** — exclusion constraint `ex_bookings_no_overlap`
   уже есть, осталось поймать SQLSTATE `23P01` и вернуть `409`; `IdempotencyKey` уже на
   сущности и в индексе. Проверить 50 одновременных запросов на один слот: ровно один `201`,
   остальные `409`.
2. **Review → rating recalculation** в Application (§12.2): пересчитывать
   `BarberProfile.Rating` / `ReviewsCount` при публикации/скрытии/удалении отзыва.
3. **Кэш** слотов/поиска/карточки — только после load test (§15.4); сейчас кэша нет.
4. **Деплой:** персистентные ключи Data Protection, `Auth__SigningKey` в секрет-сторе,
   `UseForwardedHeaders`, `Cors:AllowedOrigins` + `SameSite=None`.

## 2. Технический долг auth (`AGENT.md` §17.7)

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
9. `CreateBookingRequest.Price` приходит от клиента — цену надо брать из услуги барбера.
10. `barberServiceId` не проверяется на принадлежность профилю (`AGENT.md` §16, issue 3).

## 3. Как запускать и проверять

```powershell
# сборка (в workspace-write падает без диагностики — нужен danger-full-access, AGENT.md §5.2)
dotnet build BarberSearch/BarberSearch.sln

# тесты: dotnet test в харнессе блокирован (testhost), порядок обхода — AGENT.md §5.3
#   собрать тестовый проект, затем reflection-раннер из %TEMP%;
#   после прогона удалить раннер и временные скрипты

# миграции (AddSecurityStamp и AddReadIndexes уже применены к локальной базе)
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

## 4. Промпт для новой сессии

```text
Прочитай backend/HANDOFF.md и AGENT.md §15, §16, §17.10. Ветка features/issue-4, HEAD —
feat(api): add slots, bookings and reviews read endpoints, рабочее дерево чистое, origin запушен.
Read-эндпоинты готовы; этап 2 закрыт, его не трогай, если не меняешь контракт осознанно.

Следующая задача: booking under contention + idempotency (поймать 23P01 и вернуть 409,
проверить 50 одновременных запросов на один слот), затем review → rating recalculation в
Application. После load test — кэш слотов/поиска/карточки; в конце — деплой (Data Protection,
Auth__SigningKey, UseForwardedHeaders, CORS + SameSite=None).

Сборка/тесты/git — через danger-full-access; тесты — reflection-раннер §5.3, после прогона
удалить раннер и временные скрипты; сборка 8 проектов, 0 ошибок, 0 предупреждений; все тесты
зелёные.
```
