# CODE-REVIEW — BarberSearch backend

> Дата: 2026-10-21. Ветка: `features/issue-5` (по `HANDOFF.md`).
> Метод: **только чтение кода** (Web, Auth, Application, Infrastructure, Domain, EF-конфигурации,
> миграции, `appsettings.json`, тесты). Код не менялся.
> Что **не** проверялось: сборка и тесты не запускались (ограничение песочницы, `AGENT.md` §5.2),
> профилирование и SQL-планы не снимались (актуальные цифры — `LOADTEST.md`), прод-окружение
> не разворачивалось.
> Проблемы, уже зафиксированные в `HANDOFF.md`, помечены «уже известно» и не считаются новыми —
> при исправлении срезов их можно брать вместе с новыми, если они в одном файле.

---

## 0. Резюме

Кодовая база заметно выше среднего уровня: слои разведены, бизнес-правила не бросают исключений,
ошибки домена маппятся в ProblemDetails централизованно, у БД есть exclusion-ограничение,
keyset-индексы и частичные индексы, auth-контур продуман (cookie + JWT, security stamp, ротация
refresh-токенов, antiforgery, rate limit). Комментарии и `AGENT.md` подробнее большинства
промышленных репозиториев.

Основные риски сгруппированы так:

1. **Ссылочная целостность между агрегатами.** Отказ от FK осознан, но Application-слой не
   проверяет существование салона, услуги, специализации, профиля. В БД можно записать
   «висячую» ссылку, и она всплывёт в выдаче, слотах и брони.
2. **Валидация пользовательского ввода неполная там, где домен её делегировал наружу:**
   email не нормализуется и не ограничен по длине, пароль не имеет политики, URL фотографии
   не проверяется, числовые enum'ы принимаются.
3. **Стоимость запроса выше необходимой на каждом вызове:** проверка security stamp читает
   пользователя с коллекцией избранного **на каждом** аутентифицированном запросе; каждый write
   грузит полный агрегат профиля (до 7 SQL-запросов); EF пишет все команды в лог на уровне
   Information в проде.
4. **Согласованность записи.** Оптимистическая блокировка `xmin` закрыта (R-05), но часть
   операций не в транзакции, репозиторий сам управляет транзакцией, сырой SQL расходится
   с change tracker.
5. **Контракт API местами двусмыслен:** `serviceId` в поиске и слотах — это `hair_services.id`,
   а `barberServiceId` в брони — это `barber_services.id`.

Оценка: **до продакшена нужно закрыть P0 и часть P1** (пункты 1–4 выше). P2/P3 — плановый долг.

---

## 1. Легенда

| Метка | Значение |
|---|---|
| **P0** | Блокер: теряются/портятся данные, 500 на валидном запросе, дырка в безопасности |
| **P1** | Высокий: стоимость запроса, эксплуатационные риски, ломает масштабирование |
| **P2** | Средний: несогласованность, тестируемость, поддерживаемость |
| **P3** | Низкий / долг: гигиена, дублирование, документация |
| **уже известно** | Зафиксировано в `HANDOFF.md` / `AGENT.md` §9 |

---

## 2. P0 — блокеры

### R-01. Невалидный `TimeZoneId` салона отдаёт 500 на каждый запрос слотов — ✅ закрыто 2026-10-21

**Где:** `BarberSearch/src/BarberSearch.Application/BarberProfiles/GetBarberSlots/GetBarberSlotsHandler.cs:206`
(`InvalidTimeZone` → `Error.Failure` → 500), `BarberSearch/src/BarberSearch.Domain/Salons/Salon.cs:56`
и `:94` (при записи проверяется только непустая строка).

**Почему:** `Salon.TimeZoneId` — свободный текст. Валидация происходит только при чтении слотов,
и результат — `ErrorType.FAILURE`, то есть **500** на каждый запрос, а не 4xx. Одна опечатка
в админском запросе навсегда ломает слоты салона; мониторинг увидит поток 500 от валидного
клиента. Плюс IANA-идентификаторы не всегда резолвятся на Windows (`Asia/Calcutta` vs
`Asia/Kolkata`), а `FindSystemTimeZoneById` бросает.

**Как исправить:**
1. Валидировать на записи: `TimeZoneInfo.TryFindSystemTimeZoneById` в `Salon.Create` /
   `UpdateLocation` → 400 `salon.timeZoneId.unknown`.
2. На чтении оставить защиту, но маппить её в `Error.Failure` **с алертом** (данные уже
   протиснулись), а не молча в 500 — либо в 409/503, если так решат продукт/эксплуатация.
3. Разделить «нет часового пояса» и «пояс не резолвится» — сейчас оба дают один код.

**Сделано:** `Salon.Create` / `UpdateLocation` разрешают пояс через `TimeZoneInfo`
(Windows-идентификаторы нормализуются в IANA) и возвращают `salon.timeZoneId.unknown`;
`GetBarberSlotsHandler` различает `barberSlots.timeZone.missing` и `.unknown`, отвечает `409`
и пишет error-лог с `salonId` / `barberProfileId` вместо молчаливого `500`.

---

### R-02. Висячие ссылки между агрегатами: существование салона/услуги/специализации/профиля не проверяется — ✅ закрыто 2026-10-21

**Где:**
- `BarberSearch/src/BarberSearch.Application/BarberProfiles/AddBarberProfileSalon/AddBarberProfileSalonHandler.cs:49`
- `BarberSearch/src/BarberSearch.Application/BarberProfiles/AddBarberProfileService/AddBarberProfileServiceHandler.cs:49`
- `.../AddBarberProfileSpecialization/AddBarberProfileSpecializationHandler.cs`
- `BarberSearch/src/BarberSearch.Application/Users/AddUserFavorite/AddUserFavoriteHandler.cs:63`
- `BarberSearch/src/BarberSearch.Application/Bookings/CreateBooking/CreateBookingHandler.cs:108`
  (валидируются только `BarberService` и профиль; `request.SalonId` — нет)
- конфигурации: `Infrastructure.Postgresql/Configurations/BarberSalonConfiguration.cs`,
  `BookingConfiguration.cs:19` (FK нет — осознанно).

**Почему:** домен строит дочернюю сущность по произвольному GUID и не может проверить чужой
агрегат — значит, проверка обязана быть в Application, но её нет. Любой барбер/админ может
создать `barber_salons` с несуществующим `salon_id`, `barber_services` с несуществующей
`hair_service_id`, а бронь — с произвольным `salon_id`. Последствия: `/slots` → 404 «нет
активного салона», поиск по городу не находит барбера, в брони указан салон, где барбер
не работает. Данные невозможно починить, не зная исходного намерения.

**Как исправить:**
1. В каждом write-обработчике — узкий `ExistsAsync` по ссылке перед `SaveChanges` (в той же
   транзакции, что и вставка). Для брони дополнительно проверять связь
   `(barber_profile_id, salon_id)` в `barber_salons`.
2. Вернуть FK там, где это не ломает DDD-границу владения (`barber_salons.salon_id`,
   `barber_salons.barber_profile_id` уже есть, `bookings.salon_id` — под вопросом).
3. Отдельная проверка целостности (SQL-скрипт раз в сутки), пока FK нет.

**Сделано:** `AddBarberProfileSalon` / `AddBarberProfileService` / `AddBarberProfileSpecialization`
/ `AddUserFavorite` проверяют ссылку узким `ExistsAsync` и сохраняют в `IUnitOfWork`;
`CreateBookingHandler` в одной транзакции проверяет салон и пару
`(barber_profile_id, salon_id)`, а `BookingRepository.AddAsync` присоединяется к транзакции
`IUnitOfWork` вместо вложенной. Миграция `AddCrossAggregateForeignKeys` добавила пять FK
`NOT VALID` (`barber_salons → salons`, `barber_services → hair_services`,
`barber_specializations → specializations`, `favorite_barbers → barber_profiles`,
`users → barber_profiles`); `bookings.*` оставлены без FK осознанно.
`deploy/find-dangling-references.sql` находит исторические висячие строки, после очистки
ограничения валидируются вручную.

---

### R-03. Email: нет нормализации, нет ограничения длины, уникальность регистрозависимая — ✅ закрыто 2026-10-21

**Где:** `BarberSearch/src/BarberSearch.Domain/Users/User.cs:315` (проверка `Contains('@')`),
`Infrastructure.Postgresql/Configurations/UserConfiguration.cs:34` и `:52` (`text`, обычный
unique), `Application/Users/CreateUser/CreateUserHandler.cs:34` (проверка занятости
с `Trim()`), `Infrastructure.Postgresql/Repositories/UserRepository.cs:35-63` (`Trim()`).

**Почему:**
- `" User@Mail.ru"` сохранится как есть: `Create` не тримит и не нормализует регистр, а
  `EmailExistsAsync`/`GetByEmailAsync` тримят. Итог: дубликаты (`user@x` и ` User@x`),
  и аккаунт, в который невозможно войти (логин тримит входные данные).
- Регистр: `User@x.ru` и `user@x.ru` — два разных пользователя. Для сервиса с email-входом
  это дефект продукта и риск захвата «похожего» адреса.
- Длины нет ни в домене, ни в БД (`text`). Адрес > ~2.7 КБ роняет вставку btree-индекса
  (`54000 program_limit_exceeded`) → `MapDatabaseError` не знает этот код → `Error.Failure`
  → **500** вместо 400.
- `Contains('@')` пропускает `" @ "`, `"a@b@c"`, `"a@b"` с пробелами.

**Как исправить:**
1. `Email = email.Trim().ToLowerInvariant()` в домене (или `citext` + unique, тогда регистр
   решает БД; `citext` уже включён расширением).
2. `HasMaxLength(254)` + check constraint `char_length(email) BETWEEN 3 AND 254`.
3. Проверять адрес `System.Net.Mail.MailAddress.TryCreate` (не regex).
4. Уникальность — `citext` либо уникальный индекс по `lower(email)`.

**Сделано:** `User.Create` нормализует адрес (`Trim().ToLowerInvariant()` в `User.NormalizeEmail`)
и проверяет его через `MailAddress.TryCreate`, отсекая display-name и домен без точки, с
границами 3..254 символа (`Constants.MIN/MAX_EMAIL_LENGTH`). `CreateUserHandler` сначала
создаёт агрегат, а занятость проверяет уже по каноническому адресу; `UserRepository`
нормализует входной email для `GetByEmailAsync`/`EmailExistsAsync`. Колонка `users.email`
переведена в `citext` (уникальность и сравнение без учёта регистра), в модели —
`HasMaxLength(254)`, в БД — check `char_length(email) BETWEEN 3 AND 254`; миграция
`NormalizeUserEmail` приводит исторические строки к `lower(btrim(email))` и падает с
понятным сообщением, если после нормализации остаются дубли. Тесты: 147 domain +
113 application.

---

### R-04. У брони нет временных правил: прошлое, бесконечное будущее, длительность от клиента — ✅ закрыто 2026-10-22

**Где:** `BarberSearch/src/BarberSearch.Domain/Bookings/Booking.cs:43-108` (проверяется только
`endAt > startAt`), `CreateBookingHandler.cs:108`.

**Почему:** клиент задаёт `StartAt`/`EndAt` сам. Можно забронировать 2020 год, 2126 год или
окно в 1 минуту при услуге на 60 минут (цена при этом берётся серверная). `HANDOFF.md` §2.3
фиксирует, что вывод `EndAt` из `DurationMinutes` — отдельный срез, но **отсутствие окон
допустимости — это уже дефект**, а не продуктовое решение: он даёт мусор в таблице, ложную
занятость слотов и невозможность посчитать загрузку.

**Как исправить (без изменения контракта):**
1. `StartAt >= now` (с допуском на часовой пояс салона) и `StartAt <= now + Horizon`
   (30/90 дней) — константы в `Constants`.
2. `EndAt <= StartAt + MaxDurationMinutes`.
3. Длительность из `BarberService.DurationMinutes` (следующий срез; требует согласования
   контракта — см. R-20).

**Сделано:** контракт не менялся. `CreateBookingHandler` проверяет окно после ключа
идемпотентности (повтор по ключу возвращает бронь, даже если её начало прошло): `StartAt`
раньше `now − 5 минут` → `400 booking.create.startAt.inPast`, позже `now + 90 дней` →
`400 booking.create.startAt.tooFar`; граница горизонта включительная. Допуск в 5 минут
покрывает расхождение часов клиента и сервера, сравнение идёт по абсолютному моменту, поэтому
часовой пояс салона результат не меняет. `Booking.Create` ограничивает длительность:
`EndAt − StartAt > 1440 минут` → `400 booking.create.period.tooLong`. Константы
`BOOKING_HORIZON_DAYS`, `BOOKING_START_TOLERANCE_MINUTES`, `MAX_BOOKING_DURATION_MINUTES` —
в `Domain/Constants.cs`; правило «сейчас» живёт в Application по `AGENT.md` §7.6, домен знает
только верхнюю границу. `EndAt` по-прежнему приходит от клиента: вывод из
`BarberService.DurationMinutes` остаётся отдельным срезом (R-20, `HANDOFF.md` §2.3), поэтому
окно короче длительности услуги (те самые «1 минута при услуге на 60 минут») пока допустимо —
закрывать его должен срез R-20, а не R-04.
Тесты: 149 domain + 118 application (+2 на длительность и +5 на окно, включая допуск, границу
горизонта и повтор после начала брони). `deploy/smoke.ps1` теперь бронирует 30–79 дней вперёд,
чтобы оставаться внутри горизонта. Слоты за горизонтом по-прежнему отдаются — ограничение
выдачи слотов продуктово и вынесено в `HANDOFF.md` §2.3.

---

### R-05. Нет оптимистической блокировки: конкурентные изменения агрегата теряются молча — ✅ закрыто 2026-10-22

**Где:** все агрегаты (`Domain/Common/Entity.cs`), EF-конфигурации без `IsRowVersion`/`xmin`.

**Почему:** два параллельных запроса `PUT /api/barber-profiles/{id}` (или `Cancel` +
`Complete` одной брони) читают состояние и пишут «последний победил». Для профиля это потеря
правок коллекций (они заменяются целиком, `Replace(...)`), для брони — переход из
устаревшего статуса (например, `Cancel` поверх `Complete`). Domain-инварианты при этом
формально соблюдаются, потому что проверяются на устаревшем снимке.

**Как исправить:**
1. `UseXminAsConcurrencyToken()` на агрегатах (`User`, `BarberProfile`, `Booking`, `Review`,
   `Salon`, `HairService`, `Specialization`) — это Npgsql-специфика, но бесплатная.
2. Маппинг `DbUpdateConcurrencyException` → 409 `*.concurrencyConflict` в
   `RepositoryBase.SaveChangesAsync` (сейчас ловится только `DbUpdateException`).
3. Тест на конкурентную отмену/завершение брони.

**Сделано:** `ConcurrencyTokenExtensions.UseXminConcurrencyToken()` добавил теневую
`uint xmin`-свойство с `IsRowVersion()` всем сущностям агрегатов — семи корням и семи дочерним
строкам (корней из плана оказалось недостаточно: `UPDATE` ребёнка не трогает строку корня,
поэтому корневой токен не ловил конкурентную правку расписания, фото или услуги). В Npgsql 10
отдельного `UseXminAsConcurrencyToken` уже нет, поэтому используется стандартный
`IsRowVersion`: провайдер сам привязывает `uint`-свойство к системной колонке `xmin`
(`NpgsqlPostgresModelFinalizingConvention.ProcessRowVersionProperty`). Схема БД не меняется —
миграция `AddOptimisticConcurrency` пустая по DDL и только фиксирует модель в snapshot.
`RepositoryBase.SaveChangesAsync` ловит `DbUpdateConcurrencyException` до `DbUpdateException`
и возвращает `Error.Conflict("{errorCode}.concurrencyConflict")`, а `FailureProblemDetails`
отдаёт это как `409`; в сообщении — имя сущности и идентификатор, без PII.
`BarberProfileRepository.GetForRatingUpdateAsync` теперь читает `SELECT *, xmin … FOR UPDATE`:
PostgreSQL не включает системные колонки в `SELECT *`, и без явного `xmin` EF не материализовал
токен — пересчёт рейтинга падал бы в конфликт с `xmin = 0`. `RefreshToken` сознательно оставлен
без токена: его гонку закрыл R-07 условным `UPDATE`.
Тесты: 149 domain + 120 application + 3 infrastructure (новый проект
`tests/BarberSearch.Infrastructure.UnitTests`: модель содержит `xmin` у 14 сущностей агрегатов
и не содержит у `RefreshToken`; `DbUpdateConcurrencyException` маппится в
`booking.update.concurrencyConflict`). `BookingConcurrencyTests` проверяют, что `Cancel` и
`Complete` не глотают 409. Проверено на локальной PostgreSQL 16.3 и на пустой БД со всеми
миграциями: два контекста читают одну бронь, оба вызывают `Confirm`, первый `SaveChanges`
проходит, второй бросает `DbUpdateConcurrencyException`, а `BookingRepository.UpdateAsync`
возвращает `409 booking.update.concurrencyConflict`; `GetForRatingUpdateAsync` +
`UpdateRating` сохраняются штатно. Отсоединённый граф обновлять по-прежнему нельзя — исходный
`xmin` неизвестен (предупреждение в `RepositoryBase.SaveGraphAsync`).

---

### R-06. `PUT .../salons/{salonId}/primary` отвечает 409 при живом основном салоне — ✅ закрыто 2026-10-22
**уже известно** (`HANDOFF.md` §2.1.1)

**Где:** `Infrastructure.Postgresql/Configurations/BarberSalonConfiguration.cs:42-45`
(`UNIQUE (barber_profile_id) WHERE is_primary`, не `DEFERRABLE`),
`Domain/BarberProfiles/BarberProfile.cs:425`.

**Как исправить:** `ALTER TABLE ... ADD CONSTRAINT ... UNIQUE ... DEFERRABLE INITIALLY DEFERRED`
(в PostgreSQL `DEFERRABLE` возможен только для constraint, не для индекса) миграцией; либо два
`SaveChanges` внутри одной транзакции (снять признак → поставить признак). Первое чище: домен
остаётся с одним `SaveChanges`.

**Сделано:** вместо частичного уникального индекса миграция `DeferrablePrimarySalonConstraint`
создаёт частичное exclusion-ограничение
`ex_barber_salons_primary_per_profile EXCLUDE USING btree (barber_profile_id WITH =) WHERE (is_primary) DEFERRABLE INITIALLY DEFERRED`.
Чистое `UNIQUE (barber_profile_id) DEFERRABLE` не подошло: PostgreSQL не поддерживает частичные
UNIQUE-ограничения, а `WHERE is_primary` обязателен, иначе уникальным стал бы салон, а не пара
«профиль + основной салон». Exclusion-ограничение — та же уникальность по `=`, но с предикатом
и отложенной проверкой; `EXCLUDE USING btree` не требует `btree_gist`. Теперь домен и
`SetBarberProfilePrimarySalonHandler` не менялись: один `SaveChanges`, EF может отправлять
`is_primary = true` раньше `false`, а ограничение проверяется в `COMMIT`, когда промежуточное
состояние уже ушло. В EF-модели индекс больше не объявлен — Npgsql не умеет моделировать
частичные exclusion-ограничения, поэтому DDL целиком живёт в миграции, а `Down` возвращает
старый индекс. Честное нарушение инварианта (две основные строки дожили до коммита) по-прежнему
отклоняется: отложенная проверка срабатывает в `COMMIT`, где Npgsql бросает `PostgresException`
напрямую, а не `DbUpdateException`, поэтому `RepositoryBase.SaveChangesAsync` теперь ловит и её и
маппит через общий `MapPostgresError` как `23P01` → `409 {errorCode}.overlap`, не 500.
Проверено на локальной PostgreSQL 16: миграции применены на пустую БД; реальный
`SetBarberProfilePrimarySalonHandler` через EF и репозиторий трижды подряд переключил основной
салон (A → B → A) — успех; `AddBarberProfileSalon` с `IsPrimary = true` в `IUnitOfWork` вставил
новый салон и снял старый флаг — успех; на том же коде со старым индексом третье переключение
вернуло `barberProfile.update.uniqueViolation`, то есть дефект воспроизведён до правки и исчез
после. Тесты: 149 domain + 120 application + 4 infrastructure.

---

### R-07. Ротация refresh-токена не сериализована: возможны две живые цепочки — ✅ закрыто 2026-10-23
**уже известно** (`HANDOFF.md` §2.2.2)

**Где:** `Application/RefreshTokens/RefreshSession/RefreshSessionHandler.cs:59-148`,
`Infrastructure.Postgresql/Repositories/RefreshTokenRepository.cs:55-71`.

**Почему:** `SELECT` по хешу → проверка → `UPDATE revoked_at` в конце. Два параллельных запроса
с одним токеном оба проходят проверку `IsReplaced`/`IsRevoked` (оба видят `null`), оба создают
замену. В БД появляются два валидных токена, и reuse-detection перестаёт быть надёжной —
именно она защищает от кражи.

**Как исправить:** условный `UPDATE refresh_tokens SET revoked_at=..., replaced_by=...
WHERE id=@id AND revoked_at IS NULL` и проверка `rowsAffected == 1` (проигравший получает 401
`auth.refresh.reused`); альтернатива — `SELECT ... FOR UPDATE` в начале обработчика.
Тест: два параллельных refresh одного токена → ровно один успех.

**Сделано:** `RefreshTokenRepository.RotateAsync` отзывает строку условным
`UPDATE refresh_tokens SET revoked_at = @revoked_at, updated_at = @updated_at
WHERE id = @id AND revoked_at IS NULL` и проверяет `rowsAffected`: из двух параллельных
ротаций побеждает ровно одна, проигравший не вставляет замену. Отзыв, вставка замены и
`replaced_by` идут в одной явной транзакции (репозиторий открывает её сам, если снаружи
транзакции нет), поэтому цепочка либо получает и отзыв, и замену, и звено `replaced_by`,
либо не меняется вовсе. `replaced_by` пишется **вторым** `UPDATE`, уже после вставки замены:
self-FK `fk_refresh_tokens_refresh_tokens_replaced_by` не отложен, и вариант с одним
`UPDATE` падал на `23503` (`INSERT или UPDATE … нарушает ограничение внешнего ключа`).
`ExecuteUpdate` идёт мимо change tracker, поэтому `current` после условного апдейта
отсоединяется — иначе следующий `SaveChanges` повторил бы `UPDATE` уже без условия.
Проигравший получает `401 auth.refresh.reused` (код репозитория
`IRefreshTokenRepository.AlreadyRotatedErrorCode` = `refreshToken.rotate.alreadyRotated`
маппится в `RefreshSessionHandler`), но цепочку не отзывает: иначе два легитимных
одновременных refresh разлогинили бы клиента, а победитель потерял бы только что выданную
пару. Повторное предъявление того же старого токена позже по-прежнему идёт по обычной
reuse-detection и гасит цепочку.
Тесты: 149 domain + 121 application + 4 infrastructure. Новый
`RefreshTokenTests.Refresh_TwoConcurrentRequestsWithSameToken_ExactlyOneSucceeds`: два
обработчика читают токен до первого `UPDATE` (барьер в фейке), ровно один успех, у
проигравшего `auth.refresh.reused`, в «таблице» две строки и замена победителя жива.
`FakeRefreshTokenRepository` теперь отдаёт снимки строк (как два `DbContext`) и моделирует
условный `UPDATE` проверкой `IsRevoked` на сохранённой строке.
Проверено на локальной PostgreSQL 16.3: последовательный refresh через реальный handler и
EF ротирует пару (2 строки, старый отозван и связан с заменой); два параллельных
`RotateAsync` из разных `DbContext` после общего `SELECT` дают один успех и один
`refreshToken.rotate.alreadyRotated`, в БД снова 2 строки, старый токен отозван и связан
с заменой, токен победителя жив; повторное предъявление старого токена — `401
auth.refresh.reused` с отзывом цепочки.

---

### R-08. Смена пароля не атомарна — ✅ закрыто 2026-10-23

**Где:** `Application/Users/ChangePassword/ChangePasswordHandler.cs:80-94`
(`UpdateAsync` → `SaveChanges`, затем `RevokeAllForUserAsync` — отдельный SQL вне транзакции).

**Почему:** если отзыв токенов упадёт (сеть, таймаут, откат), пароль уже изменён, а refresh-токены
живы. Access-токены умрут из-за ротации stamp, но refresh продолжит выдавать новые — то есть
смена пароля «из-за кражи» не закрывает сессию злоумышленника.

**Как исправить:** обернуть оба шага в `IUnitOfWork.ExecuteAsync`. Заодно — единый паттерн для
всех операций из двух записей.

**Сделано:** `ChangePasswordHandler` выполняет `ChangePasswordHash`, `UpdateAsync` и
`RevokeAllForUserAsync` внутри одного `IUnitOfWork.ExecuteAsync`; PBKDF2-хеш считается до
открытия транзакции, чтобы не держать соединение. Любой `Failure` или исключение отзыва
откатывает и новый хеш, и `security_stamp`, и отзыв токенов — состояния «пароль сменён,
refresh-токены живы» больше нет. Тем же паттерном закрыты `ChangeUserRoleHandler` и
`DeactivateUserHandler`: у них та же пара «запись пользователя + отзыв всех токенов», и
неудачный отзыв больше не оставляет изменение агрегата. Тесты: 125 application (+4: отказ и
исключение отзыва при смене пароля, отказ при смене роли и при деактивации; в каждом
проверяются прежние `PasswordHash`/`Role`/`IsActive`, прежний `SecurityStamp` и живые токены).
Проверено на локальной PostgreSQL 16: успешная смена коммитит новый хеш и отзывает оба токена;
таймаут отзыва возвращает исключение, оставляя старый хеш, прежний `security_stamp` и оба
живых токена; смена роли и деактивация при том же таймауте тоже откатываются.

---

### R-09. Пустой ключ идемпотентности даёт 400 с кодом репозитория, доменная ошибка недостижима — ✅ закрыто 2026-10-23

**Где:** `Application/Bookings/CreateBooking/CreateBookingHandler.cs:68-87`,
`Infrastructure.Postgresql/Repositories/BookingRepository.cs:37-68`,
`Domain/Bookings/Booking.cs:72-78`, `Contracts/Bookings/CreateBooking/CreateBookingRequest.cs:22`.

**Почему:** `request.IdempotencyKey?.Trim() ?? string.Empty` → пустая строка →
`GetByIdempotencyKeyAsync` возвращает `Error.Validation("booking.getIdempotencyKey.idempotencyKey")`;
ветка `missing` ложна, обработчик возвращает эту ошибку. В результате:
- клиент получает **400 с кодом инфраструктуры**, а не доменным `booking.create.idempotencyKey`;
- проверки длины домена (`MAX_IDEMPOTENCY_KEY_LENGTH`) вообще не выполняются;
- контракт объявляет `IdempotencyKey` как `string` (не nullable), а код обращается с ним как
  с nullable — расхождение намерения и типа.

**Как исправить:** явная проверка в начале обработчика (`string.IsNullOrWhiteSpace` → 400
`booking.create.idempotencyKey`), либо сделать ключ необязательным и генерировать сервером,
а unique-индекс сделать частичным (`WHERE idempotency_key IS NOT NULL`).

**Сделано:** правила ключа вынесены из `Booking.Create` в публичные
`Booking.NormalizeIdempotencyKey` (срезает пробелы) и `Booking.ValidateIdempotencyKey`
(пустой/пробельный → `booking.create.idempotencyKey`, длиннее `MAX_IDEMPOTENCY_KEY_LENGTH`
→ `booking.create.idempotencyKey.tooLong`). `Booking.Create` использует оба метода и хранит
уже нормализованный ключ, поэтому `" key "` и `"key"` больше не расходятся ни в unique-индексе,
ни при повторном запросе. `CreateBookingHandler` нормализует и проверяет ключ **до**
`GetByIdempotencyKeyAsync`: невалидный ключ даёт `400` с доменным кодом, не порождает ни одного
SQL-запроса, и проверка длины домена теперь достижима. Контракт
`CreateBookingRequest.IdempotencyKey` остаётся обязательным (`string`, не nullable) — обработчик
проверяет его как обязательное поле, а не как nullable; `null` от десериализатора (или
отсутствующее свойство) тоже дают `400`. Тесты: 150 domain (+1: ключ с пробелами хранится
обрезанным) + 128 application (+3: пустой/пробельный и слишком длинный ключ не доходят до
репозитория, `" padded-key "` сохраняется как `padded-key`).

---

### R-10. Нет политики пароля — ✅ закрыто 2026-10-23

**Где:** `Contracts/Auth/Register/RegisterRequest.cs`, `Contracts/Auth/ChangePassword/ChangePasswordRequest.cs`,
`Application/Users/ChangePassword/ChangePasswordHandler.cs:46`, `Web/Controllers/AuthController.cs:63`.

**Почему:** проверяется только «не пустой». Пароль из одного символа проходит; максимальной
длины тоже нет (PBKDF2 выдержит, но это вектор для CPU-нагрузки: 10 попыток/мин на IP ×
много IP × 1 МБ пароля). Регистр/цифры/словарные пароли не проверяются.

**Как исправить:** в одном месте (валидатор контракта или `IPasswordPolicy` в Web):
длина 12..128, проверка по словарю утечек (top-10k), запрет равенства email/имени.
Плюс `LockoutEnd`-подобная прогрессивная задержка (см. R-21).

**Сделано:** правила вынесены в Application: `IPasswordPolicy` + `PasswordPolicy`
(`Users/Passwords/`), регистрация singleton в `AddApplication()`. Политика требует 12..128
символов, запрещает пароль из встроенного словаря SecLists top-10k (embedded resource
`Users/Passwords/10k-most-common.txt`, лениво читается один раз в `HashSet<string>` с
`StringComparer.OrdinalIgnoreCase`; строки словаря trim, пустые пропускаются, сеть в рантайме
не нужна) и запрещает совпадение с email целиком, его local-part, именем и фамилией
(без учёта регистра). Пробелы внутри и по краям пароля значимы — пароль не trim'ится.
Коды ошибок: `auth.password.required` / `tooShort` / `tooLong` / `common` / `equalsEmail` /
`equalsName`, `invalidField` — `password` или `newPassword`; прежний
`auth.changePassword.newPassword` унифицирован (в тестах и доках он не фигурировал).
`POST api/auth/register` и `POST api/users` вызывают политику до `HashPassword`, поэтому
невалидный пароль даёт `400` и не доходит до PBKDF2; `POST api/auth/change-password` —
в `ChangePasswordHandler`: обязательность/длина/словарь до загрузки пользователя,
совпадение с email/именем — после загрузки (этих данных нет в команде), но до сверки
текущего пароля и до `Hash`; `currentPassword` политикой не проверяется. В `login` добавлен
только дешёвый потолок: пароль длиннее 128 символов сразу получает те же
`401 auth.login.invalidCredentials` без PBKDF2, композиционные правила к существующим паролям
при входе не применяются. Публичные DTO и NuGet-пакеты не менялись, добавлены только коды `400`.
Тесты: 146 application (+18: 16 на политику — границы 11/12/128/129, пустой/пробельный/null,
словарные `password`/`123456`/`PASSWORD`, email/local-part, имя/фамилия, сильный пароль,
значимость пробелов; 1 на слабый новый пароль в `ChangePasswordHandler` — `Failure`,
`Hash`/`Verify` не вызваны, хеш/stamp прежние, токены живы; 1 на потолок длины при входе).
Domain 150 и Infrastructure 4 остались зелёными; сборка — 0 ошибок, 0 предупреждений.

---

## 3. P1 — высокий приоритет

### R-11. Проверка security stamp — запрос в БД с коллекцией избранного на **каждом** аутентифицированном запросе — ✅ закрыто 2026-10-23

**Где:** `Web/Auth/SecurityStampValidator.cs:58` → `Application/Users/IUserRepository` →
`Infrastructure.Postgresql/Repositories/UserRepository.cs:19-33` (`Include(FavoriteBarbers)`),
подключено в `Web/Auth/AuthenticationExtensions.cs:102-120` (cookie) и `:144-159` (JWT).

**Почему:** это два следствия:
1. JWT перестаёт быть stateless: на каждый запрос — round-trip в PostgreSQL. При 1000 rps это
   1000 дополнительных SELECT/с, и они конкурируют за те же соединения пула с бизнес-запросами.
2. Загружается **весь граф пользователя с избранным**. У активного клиента избранного может быть
   сотни строк — материализация коллекции на каждый запрос, при том что валидатору нужны только
   `IsActive` и `SecurityStamp`.

**Как исправить (по возрастанию сложности):**
1. Узкий метод `GetSecurityStampAsync(Guid id)` → `SELECT is_active, security_stamp` (без Include).
   Это убирает JOIN и материализацию, round-trip остаётся.
2. Кэш штампа: `IMemoryCache`/hybrid на 30–60 с с явной инвалидацией при `RotateSecurityStamp`
   (событие/публикация в общий кэш для нескольких инстансов). Отзыв применяется в пределах TTL —
   это придётся явно задокументировать как изменение гарантии.
3. Для bearer — не проверять stamp на каждом запросе (жить на 15-минутном TTL токена), оставить
   проверку только в `refresh`. Для cookie — проверять на интервале (например, раз в N минут
   через claim времени последней проверки), а не на каждом запросе.

**Сделано:** выбран минимальный вариант — узкий запрос без кэша, отзыв по-прежнему применяется
на следующем же запросе. В `IUserRepository` добавлен `GetSecurityStampAsync(Guid id)`;
`UserRepository` выполняет `Context.Users.AsNoTracking().Where(x => x.Id == id).Select(x => new
UserSecurityStamp(x.IsActive, x.SecurityStamp)).SingleOrDefaultAsync(...)`, то есть
`SELECT u.is_active, u.security_stamp FROM users AS u WHERE u.id = @id` — без `Include`, без
`JOIN` по `favorite_barbers`, без материализации агрегата и без отслеживания; отсутствие строки —
`Error.NotFound("user.securityStamp.notFound", …)`. Проверка вынесена в Application:
`ValidateSecurityStampQuery(Guid UserId, string SecurityStamp)` и `ValidateSecurityStampHandler`
(`IQueryHandler<bool, …>`), который вызывает только `GetSecurityStampAsync` и возвращает `false`
для «пользователь не найден / `!IsActive` / stamp не совпал ordinal», `true` иначе;
предупреждения логируются по образцу прежнего валидатора, успешная проверка на каждом запросе
не логируется. Web `SecurityStampValidator` стал тонким адаптером: парсит `sub` и
`security_stamp`, проверяет отсутствие claim и вызывает
`IQueryHandler<bool, ValidateSecurityStampQuery>` через DI — обращений к репозиторию из Web
больше нет. `OnTokenValidated` / `OnValidatePrincipal` не менялись, невалидный stamp по-прежнему
даёт `401` (JWT — `Fail`, cookie — `RejectPrincipal` + `SignOutAsync`); `GetByIdAsync` /
`GetByEmailAsync` не тронуты — ими пользуются логин и мутирующие сценарии. Кэш
(`IMemoryCache`/`IDistributedCache`) и проверка «раз в N минут» сознательно отклонены: они дали
бы окно, в котором смена пароля/роли и деактивация не действуют на уже выданный токен или
cookie (до TTL / до следующего интервала), а при нескольких инстансах локальный кэш пришлось бы
инвалидировать через общий канал — гарантия «отзыв на следующем запросе» была бы потеряна.
Round-trip в БД на каждый аутентифицированный запрос остаётся — это принятая цена немедленного
отзыва; теперь это один индексный SELECT двух колонок по первичному ключу. Тесты:
150 domain + 151 application (+5) + 4 infrastructure; сборка — 0 ошибок, 0 предупреждений.
Новый `ValidateSecurityStampHandlerTests`: активный + совпавший stamp → `true`; неактивный →
`false`; устаревший stamp → `false`; неизвестный пользователь → `false`; отдельно — `GetByIdAsync`
не вызывался вообще (`0`), `GetSecurityStampAsync` — ровно `1`. SQL-форма проверена без БД через
`ToQueryString()`: `SELECT u.is_active, u.security_stamp FROM users AS u WHERE u.id = @id`,
`JOIN` и `favorite_barbers` отсутствуют.

---

### R-12. Каждый write грузит полный агрегат профиля: до 7 SQL-запросов — ✅ закрыто 2026-10-23

**Где:** `Infrastructure.Postgresql/Repositories/BarberProfileRepository.cs:615-623`
(`FullAggregate()` c `AsSplitQuery`), вызывается из 29 обработчиков профиля через `GetByIdAsync`
(например, `AddBarberProfileServiceHandler.cs:35`).

**Почему:** чтобы проверить владельца и добавить одну дочернюю строку, выполняется 7 запросов
(корень + `Salons` + `WorkingSchedules` + `Services` + `PortfolioPhotos` +
`BarberSpecializations` + `TimeOffs`) и материализуется весь граф. На write-путях (добавление
фото, специализации, услуги) это кратно дороже самой операции; при росте портфолио/салонов
растёт и объём.

**Как исправить:**
1. Для проверки владения — `IsOwnedByAsync` (уже есть, `:569`) одним `EXISTS`.
2. Для операций над одним ребёнком — точечная загрузка: `Context.BarberSalons`/`BarberServices`
   с фильтром по `BarberProfileId`, а не корень с пятью Include.
3. Либо `ExecuteUpdate`/`ExecuteDelete` для простых переключений (`Publish` фото, `Reorder`,
   `RemoveService`) — без агрегата, если инвариант не требует коллекции.
4. Полный агрегат оставить только там, где он реально нужен: `UpdateProfile`, `AddTimeOff`
   (проверка пересечения), `SetPrimarySalon`.

**Сделано:** полный агрегат (`FullAggregate()` с `AsSplitQuery`) удалён: ни один обработчик
больше не грузит корень с пятью коллекциями. `IBarberProfileRepository` вместо
`GetByIdAsync`/`GetByUserIdAsync` получил узкие чтения: `GetRootByIdAsync` (только корень),
`GetWithSalonsAsync`, `GetWithSalonsAndSchedulesAsync`, `GetWithServicesAsync`,
`GetWithPhotosAsync`, `GetWithSpecializationsAsync`, `GetWithTimeOffsAsync` (корень и ровно
одна коллекция), `GetOwnerIdAsync` (`SELECT user_id`) и `GetSalonAsync` (один салон вместе
с расписанием, `AsNoTracking`). 31 write-обработчик профиля и `FindBarberProfileSalon`
переведены на соответствующее чтение: активация, деактивация, верификация, грейд и
`UpdateProfile` читают только корень, остальные — ту коллекцию, инвариант которой проверяет
домен. `UpdateProfile`, `AddTimeOff` и `SetPrimarySalon` сохраняют прежние доменные правила
(лимит фото, дубликаты, пересечение отпусков, единственный основной салон); `AddTimeOff`
грузит только отпуска, а не весь граф. Отслеживание и `xmin` сохранены: изменения идут через
`UpdateAsync`/`SaveChanges`, `ExecuteUpdate`/`ExecuteDelete` не применялись, поэтому
конкурентность R-05 не ослаблена. Коды и семантика ошибок (404/403/409) не менялись, lazy
loading в модели не включён. Проверено на локальной PostgreSQL 16.3 (временная БД
`barbersearch_verify_R12`, удалена): каждое узкое чтение — ровно один SQL; `GetRootByIdAsync`
без JOIN, `GetWithServicesAsync` — только `barber_services`, `GetWithPhotosAsync` — только
`portfolio_photos` и т. д.; `GetOwnerIdAsync` — `SELECT user_id`, `GetSalonAsync` —
`barber_salons` + `working_schedules`. Фактически проверено и сохранение: добавление фото и
отпуска, а также правка корня не затронули незагруженные коллекции. Тесты: 150 domain +
165 application (+14 на выбор узкого чтения для активации, профиля, грейда, салонов, услуг,
специализаций, отпусков, фото, расписания и поиска салона) + 10 infrastructure; сборка —
0 ошибок, 0 предупреждений.

---

### R-13. В проде EF логирует каждый SQL-запрос с параметрами — ✅ закрыто 2026-10-23

**Где:** `Web/appsettings.json:28-33` (`Default: Information`, переопределён только
`Microsoft.AspNetCore`).

**Почему:** категория `Microsoft.EntityFrameworkCore.Database.Command` пишет «Executed DbCommand»
на уровне Information. То есть в прод-логе будут все запросы и все параметры: email,
`password_hash`, idempotency-ключи, тексты отзывов. Это и объём логов (самая дорогая статья
после БД), и утечка PII/секретов в централизованный лог.

**Как исправить:** в `appsettings.json`:
```json
"Microsoft.EntityFrameworkCore.Database.Command": "Warning",
"Microsoft.EntityFrameworkCore": "Warning"
```
и отдельно убедиться, что `EnableSensitiveDataLogging` нигде не включён (сейчас не включён).
В Development можно оставить Information через `appsettings.Development.json`.

**Сделано:** в `Web/appsettings.json` категории `Microsoft.EntityFrameworkCore` и
`Microsoft.EntityFrameworkCore.Database.Command` переведены в `Warning`; уровни `Default`
(`Information`) и `Microsoft.AspNetCore` (`Warning`) не менялись, поэтому собственные
Information-логи приложения остаются, а «Executed DbCommand» с параметрами в прод-лог больше не
попадает — вместе с ним уходят email, `password_hash`, idempotency-ключи и тексты отзывов.
В `Web/appsettings.Development.json` обе EF-категории явно возвращены в `Information`, чтобы
локальная отладка видела SQL и не зависела от будущего ужесточения `Default`. Логирование
задаётся только конфигурацией: `EnableSensitiveDataLogging`, `LogTo` и фильтры уровня в `Web` и
`Infrastructure.Postgresql` отсутствуют (проверено поиском по репозиторию), поэтому код и DI не
могут вернуть Information в обход `appsettings`; внешнее поведение API не изменилось.
Тесты: 150 domain + 151 application + 6 infrastructure; новые два — в
`LoggingConfigurationTests`: боевой файл парсится, EF-категории равны `Warning` при неизменных
`Default`/`Microsoft.AspNetCore`, а Development даёт `Information` для EF и `Default`. Тест
читает сами файлы `BarberSearch.Web` (подключены к тестовому проекту content-ссылками), поэтому
ловит регрессию без запуска приложения; ручная проверка подтвердила, что возврат
`Microsoft.EntityFrameworkCore` в `Information` делает его красным. Сборка — 0 ошибок,
0 предупреждений.

---

### R-14. Нет сжатия ответов, нет таймаута запроса, нет ограничения конкурентности — ✅ закрыто 2026-10-23

**Где:** `Web/Program.cs:82-132` (конвейер).

**Почему:**
- JSON-ответы поиска/карточек/списков не сжимаются — при типичном сжатии в 5–10 раз это
  прямая экономия трафика и времени ответа; `UseResponseCompression` стоит две строки.
- Нет `AddRequestTimeouts`/`UseRequestTimeouts`: медленный запрос (деградация БД, `FOR UPDATE`
  без `lock_timeout`) держит поток и соединение до клиентского таймаута. На высоконагруженной
  системе глобальный таймаут (например, 30 с) — обязательная страховка.
- Нет `AddOutputCache`: осознанно (`HANDOFF.md` §1.3), но `UseResponseCompression` — не кэш,
  его отсутствие ничем не оправдано.

**Как исправить:** `builder.Services.AddResponseCompression(o => o.EnableForHttps = true)` +
`app.UseResponseCompression()` первым после forwarded headers; `AddRequestTimeouts` с политикой
по умолчанию; health endpoints вывести из таймаута.

**Сделано:** в `Web/Program.cs` добавлены `AddResponseCompression`/`UseResponseCompression`
(сразу после forwarded headers и до `UseExceptionHandler`) и `AddRequestTimeouts`/
`UseRequestTimeouts` (после обработчика исключений, то есть на весь бизнес-путь). Настройки
вынесены в `Web/Deployment/ResponseCompressionSettings.cs` и `RequestTimeoutSettings.cs`:
точный список MIME — JSON, `application/problem+json`, XML, JavaScript/CSS и текст (wildcard
middleware не поддерживает), без изображений, шрифтов и архивов, `EnableForHttps = true`;
таймаут по умолчанию — 30 с, по истечении клиент получает штатный `504`. `/health/live` и
`/health/ready` в `HealthCheckExtensions` помечены `WithRequestTimeout(Timeout.InfiniteTimeSpan)`,
поэтому readiness отвечает `503` по своему `HealthChecks:DatabaseTimeoutSeconds`, а не общим
таймаутом. Новых кодов ошибок нет: бизнес-ошибки по-прежнему идут через `Failure`/ProblemDetails.
Output caching, rate limiting и ограничение конкурентности не добавлялись — в пункте они не
предписаны, кэш осознанно отсутствует. Тесты: 4 новых в `HttpPipelineTests`
(`Infrastructure.UnitTests`) — allowlist/HTTPS и отрицательные MIME, фактическое решение
`IResponseCompressionProvider` для JSON/HTML/PNG/zip, политика 30 с и infinite-таймаут на обоих
health-эндпоинтах; итого 150 domain + 151 application + 10 infrastructure. Вручную: приложение
запущено в Development, `/health/live` и `/health/ready` — `200` (PostgreSQL healthy),
`/openapi/v1.json` с `Accept-Encoding: br` отдаёт `Content-Encoding: br` и
`Vary: Accept-Encoding`, без заголовка — без сжатия; Swagger UI `200`, его JS сжимается,
favicon `image/png` — нет. Сборка — 0 ошибок, 0 предупреждений.

---

### R-15. Репозиторий управляет транзакцией сам, хотя есть `IUnitOfWork` — ✅ закрыто 2026-10-23

**Где:** `Infrastructure.Postgresql/Repositories/BookingRepository.cs:211` (собственный
`BeginTransactionAsync` + `Commit`/`Rollback`), при том что `IUnitOfWork` используется в 5
обработчиках отзывов (`Application/Reviews/*`).

**Почему:** два конкурирующих механизма. Если завтра `CreateBookingHandler` обернут в
`IUnitOfWork` (что логично для идемпотентности/салонной проверки), EF бросит
`InvalidOperationException` о уже начатой транзакции — вложенные транзакции не поддерживаются.
Плюс `AddAsync` коммитит раньше, чем обработчик закончил: любая последующая запись в том же
запросе окажется вне транзакции.

**Как исправить:** убрать транзакцию из репозитория: `AddAsync` только `Add` + `SaveChanges`;
advisory lock + transaction вынести в `IUnitOfWork.ExecuteAsync` в обработчике (advisory lock
живёт до конца транзакции — семантика сохранится).

**Сделано:** транзакция и advisory lock убраны из `BookingRepository`. `AddAsync` теперь только
добавляет сущность, вызывает `SaveChanges` и при `Failure` или исключении отцепляет бронь
от change tracker (при успехе она остаётся отслеживаемой, как в остальных репозиториях) — сам
он ничего не коммитит и не откатывает, поэтому транзакция `IUnitOfWork` может включать и
проверки, и вставку. В `IBookingRepository` добавлен узкий
`AcquireBarberLockAsync(barberProfileId, ct)`: он выполняет
`SELECT pg_advisory_xact_lock(hashtextextended(...))` тем же ключом, что и раньше, и не
открывает транзакцию. `CreateBookingHandler` вызывает его первым действием внутри уже
существующего `IUnitOfWork.ExecuteAsync`, до проверок услуги, салона и пары «барбер — салон»;
блокировка живёт до коммита, поэтому сериализация per-barber сохранилась, а вложенной
транзакции, которую EF Core не поддерживает, больше нет. Откат при `Failure` и исключении
обеспечивает `IUnitOfWork`; новых кодов ошибок нет — `23P01`/`23505` по-прежнему дают
`booking.add.overlap`/`booking.add.uniqueViolation` (409), `booking.add.deadlock` остаётся
маппингом `RepositoryBase`. Тесты: 150 domain + 168 application + 12 infrastructure; новые —
три в `BookingCreateTests` (блокировка берётся до вставки и до проверок ссылок; вставка идёт
внутри транзакции `IUnitOfWork` и коммитится один раз; неудачная проверка ссылок откатывает
транзакцию) и два в `BookingRepositoryTests` без базы (`AddAsync` возвращает `Failure` и
отцепляет бронь, не начиная транзакцию — подключение указывает на закрытый порт; исключение
сохранения так же отцепляет бронь и пробрасывается). На локальной PostgreSQL 16 создана
отдельная БД `barbersearch_verify_R15` со всеми миграциями: успешный `lock + AddAsync` внутри
`IUnitOfWork` не виден второму соединению до коммита, advisory lock удерживается до коммита и
освобождается после; overlap и повтор ключа дают ожидаемые 409-коды, откатываются, строка
отцепляется; исключение после успешной вставки откатывает всю транзакцию. БД удалена после
проверки. Сборка — 0 ошибок, 0 предупреждений.

---

### R-16. Сырой SQL расходится с change tracker — ✅ закрыто 2026-10-23

**Где:** `Infrastructure.Postgresql/Repositories/RefreshTokenRepository.cs:82-109`
(`RevokeChainAsync`) и `:111-125` (`RevokeAllForUserAsync` через `ExecuteUpdateAsync`).

**Почему:** `UPDATE` идёт мимо EF. Если сущность уже отслеживается (например, `current` из
`GetByHashAsync`, который читается с трекингом), её свойства остаются старыми (`RevokedAt = null`).
Любой последующий `SaveChanges` в том же scope перезапишет `revoked_at` обратно в `null` —
то есть «тихо оживит» отозванный токен. Сейчас спасает только то, что обработчик сразу
возвращает результат; это скрытая мина для будущих правок.

**Как исправить:** после `ExecuteUpdate`/сырого SQL — `Context.ChangeTracker.Clear()` (или
`Entry(entity).State = Detached`), либо обновлять отслеживаемые сущности штатно
(`token.Revoke(...)` + `SaveChanges`). Добавить тест «после отзыва цепочки повторный
`SaveChanges` не возвращает `revoked_at`».

**Сделано:** в `RepositoryBase` добавлен `DetachTracked(predicate)`: он снимает с change tracker
только сущности своего типа, а не очищает трекер целиком, поэтому отслеживаемые `User` и прочие
агрегаты в том же scope остаются. `RefreshTokenRepository.RevokeChainAsync` вызывает его без
фильтра после рекурсивного `UPDATE` — состав цепочки в памяти неизвестен, а refresh-токены читает
только этот репозиторий; `RevokeAllForUserAsync` — с фильтром по `UserId`, токены других
пользователей остаются нетронутыми. `RotateAsync` не менялся: там `current` отцепляется сразу
после условного `UPDATE`. В XML-доке `IRefreshTokenRepository` зафиксировано, что после этих двух методов
отслеживаемые копии токенов недействительны и их нужно перечитывать. Новых кодов ошибок и
изменений API нет. Тесты: 2 новых в `RefreshTokenTrackerTests` (`Infrastructure.UnitTests`) —
после очистки в стиле «цепочки» и «всех токенов пользователя» изменённая в памяти копия с
`RevokedAt = null` отсоединяется, чужой токен и `User` остаются в трекере, а следующий
`SaveChanges` не пишет ничего (подключение указывает на закрытый порт, поэтому запись упала бы
`NpgsqlException`); итого 150 domain + 168 application + 14 infrastructure. На локальной
PostgreSQL 16 в отдельной БД `barbersearch_verify_R16` прогнаны оба метода с реальным SQL:
отозванные строки остаются с непустым `revoked_at`, трекер очищен, `User` остаётся отслеживаемым,
токен другого пользователя жив, `RotateAsync` по-прежнему пишет `replaced_by`; без очистки
(прямой `ExecuteUpdate` + `SaveChanges` со stale-копией) дефект воспроизводится — `revoked_at`
возвращается в `NULL`. БД удалена после проверки. Сборка — 0 ошибок, 0 предупреждений.

---

### R-17. Неудачный `SaveChanges` оставляет сущности в трекере — ✅ закрыто 2026-10-23

**Где:** `Infrastructure.Postgresql/Repositories/RepositoryBase.cs:49-63`.

**Почему:** `DbUpdateException` перехватывается и превращается в `Failure`, но неудачная
`Added`-сущность остаётся в change tracker. Повторный `SaveChanges` в том же запросе снова
наткнётся на то же ограничение (или, хуже, запишет часть графа). Сейчас это лечит только
`BookingRepository.AddAsync:245-253`, в остальных репозиториях — нет.

**Как исправить:** в `SaveChangesAsync` при ошибке отцеплять изменённые сущности
(`foreach (var e in ChangeTracker.Entries().Where(e => e.State != Unchanged)) e.State = Detached;`),
либо `ChangeTracker.Clear()` — и явно задокументировать, что после `Failure` контекст пуст.
Это же решит R-16 частично.

**Сделано:** `RepositoryBase.SaveChangesAsync` на каждом маппируемом сбое
(`DbUpdateConcurrencyException`, `PostgresException`, `DbUpdateException`) сначала вызывает
`FailAndClearTracker`, то есть `Context.ChangeTracker.Clear()`, и только затем возвращает
`Failure`. Выбран `Clear()`, а не отцепление только изменённых: `IUnitOfWork` откатывает
транзакцию, поэтому недействительны и прочитанные ранее копии, а контракт «после `Failure`
контекст пуст» нельзя нарушить забытым состоянием `Added`/`Modified`. Это зафиксировано в
XML-доке класса и метода: после сбоя агрегат нужно перечитать, прежде чем менять. Коды ошибок
не менялись (`{errorCode}.uniqueViolation`, `.overlap`, `.concurrencyConflict`, `.deadlock`,
`.foreignKeyViolation`, `.checkViolation`, `.notNullViolation`, `.database`). Явное отцепление
брони в `BookingRepository.AddAsync` сохранено: `SaveChangesAsync` очищает трекер только для
маппируемых ошибок, а это отцепление покрывает остальные исключения; `CreateBookingHandler`
после `Failure` перечитывает бронь по ключу идемпотентности уже на чистом контексте. Тесты:
три новых `[Fact]` в `RepositorySaveFailureTests` (`Infrastructure.UnitTests`) — `23505` в
`DbUpdateException`, отложенное exclusion-ограничение `23P01` в `COMMIT` и
`DbUpdateConcurrencyException`; в каждом после `Failure` трекер пуст, а `Added` и `Unchanged`
сущности отсоединены; итого 150 domain + 168 application + 17 infrastructure. На локальной
PostgreSQL 16 в отдельной БД `barbersearch_verify_R17` со всеми миграциями дубликат citext-имени
специализации внутри `IUnitOfWork` вернул `specialization.add.uniqueViolation`, трекер опустел,
транзакция откатилась и дубликат не сохранился; следующая запись в том же контексте прошла,
в таблице остались ровно две разные строки. БД удалена после проверки. Сборка — 0 ошибок,
0 предупреждений.

---

### R-18. Advisory lock без `lock_timeout` может держать все брони барбера — ✅ закрыто 2026-10-23

**Где:** `Infrastructure.Postgresql/Repositories/BookingRepository.cs:211-221`
(`pg_advisory_xact_lock`), `Web/DependencyInjection.cs` / `Infrastructure.../DependencyInjection.cs:40`
(нет настройки `lock_timeout`/`command_timeout`).

**Почему:** `pg_advisory_xact_lock` ждёт бесконечно. Зависшая или долгая транзакция (например,
отладочный `BEGIN` или медленный commit) блокирует **все** новые брони этого барбера, а
запросы копятся в пуле Npgsql до исчерпания. В `LOADTEST.md` p95 уже упирается в бюджет
(257–304 мс при лимите 300) — запас нулевой, и это до появления инцидента с блокировкой.

**Как исправить:** `SET LOCAL lock_timeout = '2s'` в начале транзакции и маппинг `55P03`
(`lock_not_available`) в 409/503 с ретраем; плюс `options.CommandTimeout` в `UseNpgsql`.
Стратегически — slot-claim-таблица (уже в планах, `HANDOFF.md` §1.2).

**Сделано:** в `BookingRepository.AcquireBarberLockAsync` перед `pg_advisory_xact_lock` тем же
соединением и в той же транзакции `IUnitOfWork` выполняется `SET LOCAL lock_timeout = '2s'`
(`PostgresSettings.LOCK_TIMEOUT`); ограничение действует только до конца транзакции создания
брони, поэтому read-запросы без транзакции его не получают. `55P03` добавлен в общий маппинг
`RepositoryBase.MapPostgresError` и превращается в `409 booking.add.lockTimeout` с сообщением
без SQL и параметров; `AcquireBarberLockAsync` теперь возвращает `UnitResult<Failure>`,
`CreateBookingHandler` пишет warning с `barberProfileId` и отдаёт ошибку до проверок ссылок и
вставки, транзакция откатывается. Для всех команд Npgsql в `AddPostgresInfrastructure` задан
`CommandTimeout(30)` (`PostgresSettings.COMMAND_TIMEOUT_SECONDS`); с readiness это не
конфликтует: `HealthChecks:DatabaseTimeoutSeconds` (5 с по умолчанию) отменяет
`CanConnectAsync` раньше. Тесты: 150 domain + 169 application + 19 infrastructure; новые —
`Create_LockTimeout_ReturnsConflictAndStopsBeforeInsert` (409, нет `AddAsync`, откат
транзакции) в `BookingCreateTests`, `RepositoryLockTimeoutTests` (`55P03` в `SaveChanges` →
`booking.add.lockTimeout`, без текста SQL) и `PostgresOptionsTests` (`CommandTimeout` из DI
равен 30). На локальной PostgreSQL 16 в отдельной БД `barbersearch_verify_R18`: транзакция A
держит advisory lock, `AcquireBarberLockAsync` в транзакции B после истечения `lock_timeout`
возвращает `409 booking.add.lockTimeout` и откатывается, после освобождения блокировка берётся
успешно; БД удалена. Сборка — 0 ошибок, 0 предупреждений.

---

### R-19. Пересчёт рейтинга делает два запроса к `reviews` под блокировкой профиля — ✅ закрыто 2026-10-23

**Где:** `Infrastructure.Postgresql/Repositories/ReviewRepository.cs:131-151`
(`CountAsync`, затем `AverageAsync`), вызывается внутри `ReviewRatingRecalculator` под
`SELECT ... FOR UPDATE` (`BarberProfileRepository.cs:58-78`).

**Почему:** блокировка строки профиля удерживается дольше необходимого (два round-trip вместо
одного), а на конкурентных публикациях отзывов одного барбера это прямой рост p95 (замерено
82–98 мс на publish/hide — заметная доля именно этот путь).

**Как исправить:** один запрос:
```csharp
var summary = await published
    .GroupBy(_ => 1)
    .Select(g => new { Count = g.Count(), Avg = g.Average(x => (decimal)x.Rating) })
    .FirstOrDefaultAsync(ct);
```

**Сделано:** `ReviewRepository.GetRatingSummaryAsync` читает количество и среднее одним
агрегирующим SQL (`GROUP BY` по константе, `COUNT(*)` и `AVG(rating::numeric)`); пустая
выборка даёт `(null, 0)`, округление по-прежнему делает домен. Запрос вынесен во внутренний
`BuildRatingSummaryQuery`, поэтому полный пересчёт под блокировкой профиля теперь выполняет три
команды (блокировка, сводка, `UPDATE`) вместо четырёх; контракты и коды ошибок не менялись.
Тесты: 150 domain + 171 application (+2: пустой набор — `null`/0, среднее и количество) +
20 infrastructure (+1: `ReviewRatingSummaryQueryTests` — `COUNT` и `AVG` в одном `SELECT` без
join'ов, без БД). На локальной PostgreSQL 16.3 в отдельной БД `barbersearch_verify_R19` со всеми
миграциями один вызов сводки дал ровно один `Executed DbCommand` для набора 5/4/5 + скрытый 1
(среднее 4.666…, count 3), для пустого и «только скрытые» — `(null, 0)`; полный
`ReviewRatingRecalculator` под `IUnitOfWork` выполнил три команды и записал 4.67/3. БД удалена.
Сборка — 0 ошибок, 0 предупреждений.

---

### R-20. `serviceId` в поиске/слотах и `barberServiceId` в брони — разные сущности — ✅ закрыто 2026-10-23

**Где:** `Infrastructure.Postgresql/Repositories/BarberProfileRepository.cs:216`
(`service.HairServiceId == serviceId` — поиск), `:397` (`HairServiceId == requestedServiceId` —
слоты), но `:134-159` (`GetServiceAsync` → `x.Id == barberServiceId` — бронь);
контракты: `Contracts/BarberProfiles/GetBarberSlots/BarberSlotsResponse.cs` (не содержит id
пары), `Contracts/Bookings/CreateBooking/CreateBookingRequest.cs:17`.

**Почему:** один и тот же по смыслу параметр `serviceId` означает две разные сущности:
`hair_services.id` (справочник) в поиске и слотах — это видно по
`service.HairServiceId == serviceId`, и `barber_services.id` (строка «услуга у барбера»)
в `CreateBookingRequest.BarberServiceId`. В карточке барбера id пары отдаётся под безликим
именем `Id` (`BarberProfileServiceResponse.cs:17`), а ответ слотов не содержит ни одного
идентификатора услуги — только `DurationMinutes`. Итог: клиенту, который выбрал услугу на
экране слотов, не из чего взять `barberServiceId` для брони; единственный «очевидный»
кандидат — тот `serviceId`, который он передал в `/slots`, и это неверная сущность. При
совпадении GUID'ов (маловероятно) бронь уйдёт на чужую услугу, иначе — 404.

**Как исправить:**
1. В ответ слотов добавить `barberServiceId` и `hairServiceId` (аддитивно, `DurationMinutes`
   уже говорит, что услуга учитывалась).
2. Переименовать параметры: `hairServiceId` в поиске/слотах, `barberServiceId` в брони —
   сделать различие явным в контракте, а не только в XML-доке.
3. В `CreateBookingRequest` оставить только `barberServiceId`, а `price` убрать из контракта
   (сделано в R-33).

**Сделано:** различие зафиксировано в контракте, а не только в XML-доке. `GET api/barbers` и
`GET api/barbers/{id}/slots` принимают `hairServiceId` (`hair_services.id`); прежний `serviceId`
переименован, это breaking change query-параметра. `CreateBookingRequest.BarberServiceId` и
`BookingResponse.BarberServiceId` остались `barber_services.id`; `Price` удалён из контракта
в R-33 (2026-10-24). Ответ слотов аддитивно получил nullable `barberServiceId` и
`hairServiceId`: без фильтра оба `null` и шаг 30 минут, с фильтром — та пара, чья
`DurationMinutes` задала сетку; `BarberSlotContext` несёт те же два id, а
`BarberProfileRepository.BuildSlotServiceQuery` проецирует `id`, `hair_service_id` и
`duration_minutes` из одной строки `barber_services` (фильтры `barber_profile_id`,
`hair_service_id`, `is_active` сохранены), поэтому длительность и `barberServiceId` не могут
разойтись. Несколько пар на одну услугу справочника невозможны: уникальный индекс
`ux_barber_services_barber_profile_id_hair_service_id`; на всякий случай оба поля читаются из
одной строки, а не двумя запросами. В карточке безликое `id` услуги переименовано в
`barberServiceId`, `hairServiceId` уже был; это тоже breaking change JSON. Обновлены XML-доки
контрактов, контроллера и репозитория, load harness (`hairServiceId` в query), `load/README.md`
и `BarberSearch.Web.http` (отдельная переменная `barberServiceId` для брони); `deploy/smoke.ps1`
эти эндпоинты не вызывает, правок не потребовалось. Коды ошибок не менялись. Тесты:
150 domain + 172 application (+1: слот с услугой возвращает пару и длительность 45 минут;
существующие 10 слотов дополнены проверкой `null` без фильтра) + 21 infrastructure (+1:
`BarberSlotServiceQueryTests` — `id` и `duration_minutes` одной строки в одном `SELECT` без
join'ов, без БД). На локальной PostgreSQL 16 в отдельной БД `barbersearch_verify_R20` со всеми
миграциями созданы профиль, салон, расписание и услуга на 45 минут: `GetSlotContextAsync` вернул
ровно `barberServiceId` этой строки и `hairServiceId` фильтра, `GetCardByIdAsync` — те же два id
в услуге, `SearchAsync` по `hairServiceId` дал 1 профиль, по случайному — 0, неизвестная услуга
в слотах — `404 barberSlots.service.notFound`; БД удалена. Сборка — 0 ошибок, 0 предупреждений.

---

### R-21. Нет защиты от перебора на уровне аккаунта — ✅ закрыто 2026-10-23

**Где:** `Web/Program.cs:44-59` (10/мин на IP), `Application/Users/LoginUser/LoginUserHandler.cs`
(нет счётчика неудач), `Domain/Users/User.cs` (нет `LockoutEnd`/`FailedAttempts`).

**Почему:** распределённый перебор с тысячи IP обходит лимит полностью. Для сервиса с email-
входом это классическая атака на credential stuffing; логи `auth.login.invalidCredentials` есть,
но реакции нет.

**Как исправить:** счётчик неудач на аккаунт (в `users` или в отдельной таблице) с
экспоненциальной задержкой/блокировкой и сбросом при успехе; уведомление владельцу.
Дополнительно — rate limit по `email` (partition key), а не только по IP.

**Сделано:** защита живёт на аккаунте, а не на IP. У `User` появились
`FailedLoginAttempts`, `LastFailedLoginAt`, `LockoutEnd` и два метода:
`RegisterFailedLogin(now)` (неудача после паузы больше 15 минут начинает счёт заново, иначе
инкремент; начиная с `MAX_FAILED_LOGIN_ATTEMPTS = 5` выставляет
`LockoutEnd = now + backoff`; счётчик ограничен `MAX + 10`, чтобы степень двойки не переполнила
`int`; метод всегда успешен и заканчивается `MarkUpdated`) и идемпотентный
`ResetFailedLogin()` (обнуляет счётчик, `LastFailedLoginAt`, `LockoutEnd`; `MarkUpdated` — только
при реальном изменении). `IsLockedOut(now)` истинно строго при `LockoutEnd > now`: в момент
окончания задержки аккаунт уже не заблокирован. Задержка —
`LOGIN_LOCKOUT_BASE_MINUTES * 2^(attempts − MAX_FAILED_LOGIN_ATTEMPTS)` минут с потолком
`LOGIN_LOCKOUT_MAX_MINUTES`: 5-я неудача — 1 минута, 6-я — 2, 7-я — 4, …, 11-я и далее — 60.
Константы (`MAX_FAILED_LOGIN_ATTEMPTS`, `LOGIN_FAILURE_RESET_MINUTES = 15`,
`LOGIN_LOCKOUT_BASE_MINUTES = 1`, `LOGIN_LOCKOUT_MAX_MINUTES = 60`) — в `Domain/Constants.cs`.
Схема: `users.failed_login_attempts` (`integer NOT NULL DEFAULT 0`, default затем снят —
значение всегда пишет домен), `users.lockout_end` и `users.last_failed_login_at`
(`timestamptz NULL`), check `ck_users_failed_login_attempts` (`>= 0`); миграция `AddUserLockout`,
`Down` удаляет колонки и constraint.

`LoginUserHandler` получает `TimeProvider` и считает «сейчас» через `GetUtcNow()`; потолок длины
пароля (128) и dummy-проверка для неизвестного email сохранены, пароль проверяется всегда.
При неверном пароле на **не** заблокированном аккаунте вызывается `RegisterFailedLogin(now)` +
`UpdateAsync`; ошибка сохранения только логируется, клиент всё равно получает
`401 auth.login.invalidCredentials` — неудачный вход не превращается в 500 и не выдаёт
существование аккаунта. Если аккаунт уже заблокирован, неверный пароль просто получает тот же
401: счётчик не растёт и `LockoutEnd` не продлевается, иначе атакующий держал бы владельца
заблокированным бесконечно. При верном пароле и активном аккаунте блокировка проверяется до
записи входа: `401 auth.login.lockedOut` («temporarily locked after repeated failed sign-in
attempts»), `LastLoginAt`/`SecurityStamp`/`UpdateCallCount` не меняются. Иначе — перехеширование
при необходимости, `ResetFailedLogin()`, `RecordLogin()`, `UpdateAsync`. Порядок «неактивный
аккаунт → `auth.login.inactive`» оставлен прежним, блокировка проверяется для активного.
Код `ApiErrorCodes.Auth.LockedOut = "auth.login.lockedOut"` добавлен в реестр (R-28) с XML-доком
(401).

**Enumeration — осознанное решение:** `auth.login.lockedOut` возвращается только при верном
пароле, поэтому перебором нельзя выяснить существование адреса; при неверном пароле
заблокированный аккаунт отвечает так же, как обычный. Уведомление владельцу **не реализовано**:
в проекте нет канала уведомлений (ни email, ни push), а добавлять его в этот срез значило бы
тянуть отдельную подсистему; счётчик и `lockout_end` видны в БД и логах. Rate limit по `email`
не добавлялся: распределённый перебор останавливает именно блокировка аккаунта, а лимит по IP
остаётся (R-22/R-25 — отдельные срезы).

**Тесты.** Domain 174 → **181** (+7 в `UserTests`): 4 неудачи не блокируют; 5-я — ровно 1 минута
и `IsLockedOut`; граница `now == LockoutEnd` — уже не заблокирован; 6-я после истечения —
2 минуты; рост 2 → 4 → 8; сотня неудач подряд упирается в cap 60 минут и счётчик `MAX + 10`;
`ResetFailedLogin` всё очищает и идемпотентен; пауза больше 15 минут начинает счёт заново.
Application 191 → **196** (+5 в `LoginLockoutTests`): неверный пароль инкрементирует и сохраняет
(`UpdateCallCount = 1`); 5 неверных подряд дают блокировку; верный пароль на заблокированном —
`auth.login.lockedOut`, `LastLoginAt` пуст, `UpdateCallCount` не растёт, `SecurityStamp` прежний;
неверный пароль на заблокированном — `auth.login.invalidCredentials`, счётчик и `LockoutEnd`
прежние, сохранения нет; успешный вход после трёх неудач сбрасывает счётчик и ставит
`LastLoginAt`. Негативный прогон: при временно убранной проверке `IsLockedOut` в обработчике
`Login_WithCorrectPasswordWhileLockedOut_ReturnsLockedOutWithoutSaving` падает (`4 passed,
1 failed`), после восстановления — все зелёные. Infrastructure 33 не менялись; сборка —
0 ошибок, 0 предупреждений.

**Проверено на локальной PostgreSQL 16.3** в отдельной БД `barbersearch_verify_R21`
(11 миграций, последняя `AddUserLockout`; после проверки БД удалена): колонки
`failed_login_attempts integer NOT NULL` без default, `last_failed_login_at timestamptz NULL`,
`lockout_end timestamptz NULL`, constraint `ck_users_failed_login_attempts CHECK
((failed_login_attempts >= 0))`. Реальные `UserRepository`/`UnitOfWork`/`LoginUserHandler`:
5 неверных паролей дали в БД `failed_login_attempts = 5`, `last_failed_login_at = 12:05Z`,
`lockout_end = 12:06Z`, `last_login_at = NULL`, `IsLockedOut = true`; верный пароль на
заблокированном — `401 auth.login.lockedOut`, строка не изменилась (`last_login_at` пуст);
после SQL-правки `lockout_end` в прошлое верный пароль прошёл, в ответе успех, а в БД
`failed_login_attempts = 0`, `last_failed_login_at`/`lockout_end` — `NULL`, `last_login_at`
заполнен.

---

### R-22. Лимит запросов только на auth-эндпоинтах; запись брони и отзывов не ограничена — ✅ закрыто 2026-10-23

**Где:** `Web/Program.cs:46-59`, `[EnableRateLimiting("auth")]` только в `AuthController`.

**Почему:** один аутентифицированный клиент может спамить `POST /api/bookings` (каждая бронь
берёт advisory lock и пишет строку) и `POST /api/reviews`. Это не столько DoS, сколько
злоупотребление бизнес-операцией: мусорные брони блокируют слоты настоящим клиентам.
`HANDOFF.md` фиксирует это как известное ограничение, но для запуска это P1.

**Как исправить:** политика `writes` (например, 30/мин на пользователя) на write-эндпоинты,
`PartitionedRateLimiter` с ключом `sub` из claims; 429 с `Retry-After` и ProblemDetails.

**Сделано:** настройка вынесена из `Program.cs` в `Web/Deployment/RateLimitSettings.cs` в стиле
соседних `*Settings`: константы `AuthPolicyName = "auth"`, `AuthPermitLimit = 10`,
`WritePermitLimit = 30`, `Window = TimeSpan.FromMinutes(1)`, `Configure(RateLimiterOptions)`,
партиции `AuthPartition`/`WritePartition`, ключи `AuthPartitionKey`/`WritePartitionKey`,
`IsWriteMethod` и `OnRejected`; `Program.cs` теперь вызывает
`builder.Services.AddRateLimiter(RateLimitSettings.Configure)`. Политика `auth` сохранена без
изменения поведения — 10/мин на `Connection.RemoteIpAddress` (после `UseForwardedHeaders`),
`QueueLimit = 0`.

Запись закрыта **глобальным** лимитером, а не именованной политикой с атрибутами на ~60
действиях: `options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
RateLimitSettings.WritePartition)` — 30/мин, окно 1 минута, `QueueLimit = 0`; `GET`/`HEAD`/
`OPTIONS`/`TRACE` получают `RateLimitPartition.GetNoLimiter("no-limit")`, поэтому чтение
карточек, слотов, поиска и списков не ограничивается. Ключ партиции — claim `sub`
(`ClaimsPrincipalFactory.SubClaim`, префикс `user:`), иначе `RemoteIpAddress` (`ip:`), иначе
`unknown`: один клиент не обходит лимит сменой адреса, а клиенты за NAT не делят квоту.
`UseRateLimiter` по-прежнему стоит после `UseAuthentication`/`UseAuthorization`, поэтому `sub`
уже доступен. На auth-эндпоинтах действуют оба лимита: 10/мин строже 30/мин, поэтому поведение
входа не меняется; двойной лимит описан в `AGENT.md` §17.5.

`options.OnRejected` отдаёт `429` как `ProblemDetails` (через `IProblemDetailsService`, как
`AuthProblemDetails`): заголовок `Retry-After` из `MetadataName.RetryAfter` лимитера (секунды до
сброса fixed window, например `30`), тело `application/problem+json` со `status: 429`, `title`,
`detail` и `errorCodes: ["rateLimit.exceeded"]`. Новый публичный код
`ApiErrorCodes.Common.RateLimitExceeded = "rateLimit.exceeded"` добавлен в реестр (R-28) с
XML-доком (429); без `OnRejected` клиент получал бы пустое тело.

**Тесты.** Infrastructure 33 → **40** (+7 в `RateLimitSettingsTests`): константы 10/30 и окно
1 минута; `IsWriteMethod` — да для POST/PUT/PATCH/DELETE (обе перегрузки), нет для GET/HEAD/
OPTIONS/TRACE; `WritePartition`/`WritePartitionKey` — `sub` даёт `user:{id}`, без него IP, без IP
`unknown`; 30 запросов записи проходят, 31-й отклоняется, другой пользователь получает свою
партицию; безопасные методы не ограничены (100 чтений) и получают `no-limit`; auth-партиция —
10 попыток на IP, 11-я отклоняется, другой IP проходит; `OnRejected` из `Configure` на
`DefaultHttpContext` с `AddProblemDetails()` и отклонённым lease с `RetryAfter = 30s` даёт `429`,
`Retry-After: 30`, `application/problem+json` и `rateLimit.exceeded` в теле и `errorCodes`.
Негативный прогон: с временно убранным `options.OnRejected = OnRejected` падает
`Configure_OnRejectedWrites429ProblemDetailsWithRetryAfterAndErrorCode` (1 failed / 6 passed),
после восстановления — 40/40. Domain 181 и Application 196 не менялись; сборка — 0 ошибок,
0 предупреждений. Ручной HTTP-прогон не выполнялся: партиции и ответ проверены юнит-тестами,
PostgreSQL для среза не нужна.

---

### R-23. Enum'ы принимаются числами; коллекции enum'ов не валидируются — ✅ закрыто 2026-10-23

**Где:** `Web/Program.cs:36-38` (`JsonStringEnumConverter()` без настроек),
`Domain/BarberProfiles/BarberProfile.cs:107-118` / `:155-165` (списки `Language`,
`PaymentMethod` не проверяются через `Enum.IsDefined`), `PrimitiveCollection` в
`BarberProfileConfiguration.cs:74-80`.

**Почему:** `JsonStringEnumConverter` по умолчанию принимает целые числа
(`AllowIntegerValues = true`). `"paymentMethods": [999]` успешно десериализуется и сохраняется
в массив в БД (в отличие от `Grade`/`Role`, где стоит `Enum.IsDefined`). Дальше клиент получает
мусор в карточке, а фильтрация по enum'ам ломается.

**Как исправить:** `new JsonStringEnumConverter(allowIntegerValues: false)` +
`Enum.IsDefined` для каждого элемента коллекций в `Create`/`UpdateProfile`, с кодом
`barberProfile.languages.unknown` и `InvalidField`.

**Сделано:** настройка JSON вынесена в `Web/Common/JsonSettings.Configure`, который регистрирует
`JsonStringEnumConverter(allowIntegerValues: false)`; `Program.cs` вызывает его из `AddJsonOptions`,
остальные настройки JSON не менялись. Теперь `"paymentMethods": [999]` отклоняется с
`JsonException` на десериализации и до обработчика не доходит, строковые имена (`"Ru"`, `"Cash"`)
работают как раньше. Домен проверяет каждый элемент `languages` и `paymentMethods` через
`Enum.IsDefined` в общей `ValidateInput`, поэтому ошибки накапливаются вместе с `displayName` и
`yearsOfExperience` (`DomainErrors.Collect`): коды `barberProfile.languages.unknown` /
`barberProfile.paymentMethods.unknown`, `InvalidField` — `languages` / `paymentMethods`, в сообщении
неизвестное числовое значение. В `Create` проверка стоит до создания агрегата, в `UpdateProfile` —
до первой мутации, поэтому неизвестное значение не попадает в коллекции даже при прямом вызове
домена; провал не меняет остальные поля. `null`-список по-прежнему допустим (`Create` подставляет
`[Language.Ru]` для языков и `[]` для способов оплаты), пустой список допустим. Схема БД не
менялась: в `integer[]` по-прежнему пишутся числа, домен — единственная проверка на этом пути;
DB-check на массивы сознательно не добавлен в этом срезе (прямой SQL в обход API такие значения
по-прежнему может записать). Тесты: 158 domain (+8: неизвестные `Language`/`PaymentMethod` в
`Create` и `UpdateProfile`, накопление ошибок вместе с `displayName`, валидные значения, пустые
списки в `Create`/`UpdateProfile`) + 186 application + 32 infrastructure (+3, `JsonSettingsTests`:
строки десериализуются, числа `999` в обоих списках дают `JsonException`, parameterless-конвертер
число принимает — контраст фиксирует регрессию). Негативный прогон: с временно возвращённым
`new JsonStringEnumConverter()` падает `Configure_RejectsIntegerEnumValues` (1 failed, 31 passed),
после восстановления — 32/32. Сборка — 0 ошибок, 0 предупреждений.

---

### R-24. URL фотографии портфолио не валидируется — ✅ закрыто 2026-10-23

**Где:** `Domain/BarberProfiles/BarberProfile.cs:325-349` (`AddPhoto`), `:330`
(проверяется только `IsNullOrWhiteSpace`).

**Почему:** в БД можно положить `javascript:alert(1)`, `data:text/html,...` или строку в
мегабайт. Клиент, который подставит это в `src`/`href`, получит stored XSS; колонка не
ограничена по длине. То же для `thumbnailUrl`, `description`.

**Как исправить:** `Uri.TryCreate(url, UriKind.Absolute)` + схема `http`/`https`, запрет
`userinfo`, `HasMaxLength` на колонки, `Enum.IsDefined` не нужен. Аналогично — `Salon.WebsiteUrl`
(`Salon.UpdateContactInfo`) и `User.AvatarUrl`.

**Сделано:** добавлен один доменный валидатор `Domain/Common/UrlValidator` (internal static):
значение тримится, пустое необязательное поле нормализуется в `null`, затем проверяются
абсолютный URI (`Uri.TryCreate(..., UriKind.Absolute)`), схема только `http`/`https` без учёта
регистра, непустой хост, отсутствие `userinfo` и длина `<= Constants.MAX_URL_LENGTH` (2048).
Валидатор возвращает `Error?` с переданными кодом и `InvalidField`, поэтому агрегаты
накапливают ошибки в `DomainErrors.Collect()` и отдают все сразу. Применено: `AddPhoto` —
`url` обязателен (`barberProfile.addPhoto.url`), `thumbnailUrl` необязателен
(`barberProfile.addPhoto.thumbnailUrl`), `description <= 2000` (`barberProfile.addPhoto.description`);
`PortfolioPhoto.UpdateDescription` — `portfolioPhoto.updateDescription.description`;
`Salon.Create`/`UpdateContactInfo` — `salon.websiteUrl`; `User.UpdateProfile` —
`user.updateProfile.avatarUrl`. Поля `url`/`thumbnailUrl`/`websiteUrl`/`avatarUrl` хранятся
обрезанными, пустая строка в необязательном поле становится `null`; `description` не тримится,
у него проверяется только длина. Схема: `HasMaxLength(2048)` у `portfolio_photos.url`,
`thumbnail_url`, `salons.website_url`, `users.avatar_url` и `HasMaxLength(2000)` у
`portfolio_photos.description`; миграция `AddUrlLengthLimits` меняет `text` → `varchar(n)`,
`Down` возвращает `text` (проверено откатом миграции). Существующие check-ограничения (`ck_portfolio_photos_url`,
`ck_salons_*`) не затрагивались и после миграции на месте. Тесты: 174 domain (+16: http/https
и верхний регистр схемы, `javascript:`/`data:`/`ftp:`, относительный путь, `userinfo`, длина
2048/2049, trim и пустые необязательные значения, накопление трёх ошибок `AddPhoto`,
`description` 2000/2001 в `AddPhoto` и `UpdateDescription`, сайт салона и аватар пользователя,
сохранение старого значения при провале) + 186 application + 32 infrastructure. Негативный
прогон: с временно отключённым `UrlValidator` падают
`AddPhoto_RejectsNonHttpSchemes` и `UpdateProfile_RejectsInvalidAvatarUrlAndKeepsOldValue`
(2 failed), после восстановления — 174/174. Миграция проверена на PostgreSQL 16.3 и на
контейнерной PostgreSQL 18 (`barbersearch_verify_R24`): пять колонок стали
`character varying(2048)`/`(2000)`, вставка URL ровно 2048 символов проходит, 2049 —
`value too long for type character varying(2048)`, `description` 2001 —
`character varying(2000)`; БД удалена. Сборка — 0 ошибок, 0 предупреждений.

---

### R-25. Read-эндпоинты закрыты для анонимов — ✅ закрыто 2026-10-23

**Где:** `Web/Auth/AuthorizationExtensions.cs:25-27` (fallback policy),
`Web/Controllers/BarbersController.cs:22-24`, `Web/Controllers/ReviewsController.cs:43`.

**Почему:** поиск барберов, карточка, слоты и отзывы требуют аутентификации. Для витрины
«найти барбера и записаться» это блокирующий продукт-дефект: незалогиненный пользователь
не увидит ничего. Если это осознанно — стоит зафиксировать в `AGENT.md`; если нет — это
главная функциональная дыра API.

**Как исправить:** `[AllowAnonymous]` на трёх GET в `BarbersController` и на `GET /api/reviews`,
плюс отдельная rate-limit-политика для анонимного чтения (по IP) и, при необходимости,
`Cache-Control` для анонимной выдачи.

**Сделано:** витрина открыта анонимно, запись и личные данные остались закрытыми.
`[AllowAnonymous]` стоит на `GET api/barbers`, `GET api/barbers/{barberProfileId:guid}`,
`GET api/barbers/{barberProfileId:guid}/slots` и `GET api/reviews`; на тех же четырёх
действиях — `[EnableRateLimiting(RateLimitSettings.ReadsPolicyName)]`. `GET api/bookings`,
`POST api/reviews`, `PUT api/reviews/{id}`, публикация/скрытие (там же `AdminOnly`) и все
write-эндпоинты по-прежнему требуют аутентификации: fallback-политика не менялась.
В `RateLimitSettings` добавлена политика `reads`: константы `ReadsPolicyName = "reads"` и
`ReadPermitLimit = 120`, окно то же (`Window` = 1 минута), `QueueLimit = 0`; партиция
`ReadsPartition` считает по `Connection.RemoteIpAddress` (после `UseForwardedHeaders`), без
`sub` — иначе вход в аккаунт обходил бы квоту; без адреса ключ `unknown`. Политика
зарегистрирована в `Configure` рядом с `auth` (`options.AddPolicy(ReadsPolicyName,
ReadsPartition)`), общий `OnRejected` отдаёт `429` с `Retry-After` и
`errorCodes: ["rateLimit.exceeded"]`. Ключ IP вынесен в `IpPartitionKey`, `AuthPartitionKey`
делегирует ему — поведение auth-лимита не изменилось.
PII в ответах не появилось и добавить её не пришлось: поиск/карточка/слоты уже фильтруют
только `IsActive && IsVerified` профили и активные салоны/услуги, отзывы — только
`IsPublished`; response-контракты содержат id, публичные поля профиля и текст отзыва, без
email/телефона. XML-доки контроллеров переписаны: `BarbersController` больше не «закрыт
fallback-политикой», а описывает публичное чтение и лимит по IP; `ReviewsController` —
список публичный, создание/правка/модерация закрыты.
`Cache-Control` для анонимной выдачи **сознательно не добавлялся**: слоты меняются сразу
после создания брони, а CDN/edge-кэша в проекте нет — заголовок дал бы клиентам и прокси
устаревшие слоты без выигрыша. Если появится edge-кэш, это отдельный срез с инвалидацией
по броням — возможный следующий шаг, не часть R-25.
Тесты (Infrastructure.UnitTests, Kestrel не поднимается): `AnonymousReadEndpointTests`
собирает `WebApplication` (`CreateBuilder`, `AddControllers().AddApplicationPart(...)`,
`AddAuthorization`, `AddRateLimiter(RateLimitSettings.Configure)`, `Build`, `MapControllers`)
и находит `RouteEndpoint` по `RoutePattern.RawText` + `HttpMethodMetadata`, поэтому GET и
POST одного шаблона не путаются; у четырёх анонимных маршрутов проверены
`Metadata.GetMetadata<IAllowAnonymous>() != null` и `EnableRateLimitingAttribute.PolicyName ==
"reads"`, у `GET api/bookings` и `POST api/reviews` — отсутствие `IAllowAnonymous` и политики
`reads`. `EnableRateLimitingAttribute` действительно попадает в endpoint metadata MVC —
рефлексия по методу контроллера не понадобилась. `RateLimitSettingsTests` дополнен тестом
`ReadsPartition`: 120 запросов с одного IP проходят, 121-й отклоняется, запрос с другого IP
не затронут; тест констант теперь проверяет 10/120/30 и окно. Всего infrastructure 40 → **43**
(+2 в `AnonymousReadEndpointTests`, +1 в `RateLimitSettingsTests`). Негативный прогон: с
временно убранным `[AllowAnonymous]` у `Get` падает
`AnonymousReadEndpoints_AreAllowAnonymousAndUseReadsPolicy` (1 failed / 1 passed), после
восстановления — 43/43. Domain 181 и Application 196 не менялись; сборка — 0 ошибок,
0 предупреждений. Ручной HTTP-прогон не выполнялся.

---

## 4. P2 — средний приоритет

### R-26. `CreateBarberProfile` не привязывает профиль к пользователю — ✅ закрыто 2026-10-23

**Где:** `Application/BarberProfiles/CreateBarberProfile/CreateBarberProfileHandler.cs:43-68`
(нет `user.LinkBarberProfile(profileId)`).

**Почему:** claim `barber_profile_id` (`Web/Auth/ClaimsPrincipalFactory.cs:69`) и
`/api/auth/me` остаются пустыми, пока админ вручную не вызовет
`PUT /api/users/{id}/barber-profile/{id}`. Любая логика, которая опирается на claim (а не на
запрос к профилю), будет вести себя по-разному в зависимости от того, был ли вызван этот
эндпоинт. Два пути создания одного состояния.

**Как исправить:** в том же обработчике (в одной транзакции) загрузить `User` и вызвать
`LinkBarberProfile`, либо убрать claim и всегда читать связь из БД.

**Сделано:** `CreateBarberProfileHandler` после доменной валидации открывает
`IUnitOfWork.ExecuteAsync<Guid>` и внутри одной транзакции загружает `User` через
`IUserRepository.GetByIdAsync`. Если `user.BarberProfileId is not null`, возвращается
`409 barberProfile.create.alreadyExists` — проверка стоит до вставки профиля и не полагается
на `ux_barber_profiles_user_id`. Иначе сохраняется профиль (`AddAsync`), вызывается
`user.LinkBarberProfile(profile.Id)` и `UpdateAsync` пользователя. `Failure` любого шага
откатывает и профиль, и привязку; ошибки загрузки пользователя пробрасываются как есть.
Идентификатор пользователя берётся из `RequireUserId()` (R-29): аноним получает
`401 auth.unauthorized`, `Guid.Empty` не подставляется. Публичный HTTP-контракт не менялся:
контроллер по-прежнему отдаёт `201` и id профиля; порядок «сначала доменная валидация,
потом запись» сохранён — невалидный запрос не открывает транзакцию. Тесты —
`CreateBarberProfileTests` (5 `[Fact]`): успех (профиль сохранён, `user.BarberProfileId ==
profileId`, транзакция закоммичена), пользователь уже с профилем → `409`, ни профиль, ни
пользователь не записаны, `UpdateAsync` → `Failure` откатывает профиль и привязку, доменная
ошибка `BarberProfile.Create` не доходит до репозиториев, аноним → `401` без записей. Без
`LinkBarberProfile` тест успеха падает (проверено временным удалением вызова). На локальной
PostgreSQL 16 в `barbersearch_verify_R26` со всеми миграциями: создан пользователь с ролью
Barber, обработчик вернул id профиля, `users.barber_profile_id` совпал с `barber_profiles.id`,
а `barber_profiles.user_id` — с id пользователя; повтор дал
`409 barberProfile.create.alreadyExists` и оставил ровно одну строку профиля; при
принудительном `Failure` в `UpdateAsync` в `barber_profiles` осталось 0 строк и привязки нет.
БД удалена. Итог: 150 domain + 183 application + 27 infrastructure, сборка — 0 ошибок,
0 предупреждений.

### R-27. Доменные часы идут мимо `TimeProvider` — ✅ закрыто 2026-10-23

**Где:** `Domain/Common/Entity.cs:16,25` (`DateTimeOffset.UtcNow`), `Domain/Users/User.cs:236`,
`Application/Bookings/MarkBookingNoShow/MarkBookingNoShowHandler.cs:62`,
`Web/Auth/JwtTokenIssuer.cs:45` (`DateTime.UtcNow`), `Web/Controllers/AuthController.cs:155,247,405`.

**Почему:** `TimeProvider` зарегистрирован (`AuthenticationExtensions.cs:46-48`) и используется
в refresh/change-password, но не в домене и не в выдаче токенов. Тесты времени возможны только
для части сценариев, а «сейчас» в системе определяется тремя разными способами.

**Как исправить:** передавать `TimeProvider` в домен (параметр метода или `IClock` в
конструкторе агрегата) либо явно фиксировать, что домен использует системное время, а
Application — `TimeProvider`; сейчас это не зафиксировано нигде, кроме комментария.
Минимум — `JwtTokenIssuer` и `AuthController` перевести на `TimeProvider`.

**Сделано:** политика часов зафиксирована явно, бизнес-«сейчас» переведено на `TimeProvider`.
`JwtTokenIssuer` получает его в конструктор (DI уже регистрирует singleton) и одним чтением
`GetUtcNow()` заполняет и `SecurityTokenDescriptor.Expires`, и `SecurityTokenDescriptor.IssuedAt`,
поэтому `exp` и `iat` считаются от одних часов; `ExpiresInSeconds` по-прежнему берётся из
`AuthOptions.AccessTokenMinutes` (900). `AuthController` получил `[FromServices] TimeProvider` в
`Login`, `LoginWithCookie` и `Refresh`: от него считаются `ExpiresUtc` постоянной cookie при
входе, `ExpiresUtc` cookie при refresh и остаток refresh-срока в `CreateTokenResponse` (метод
принимает `TimeProvider`, статического `DateTimeOffset.UtcNow` в контроллере не осталось).
`MarkBookingNoShowHandler` внедряет `TimeProvider` и сравнивает `booking.StartAt` с
`GetUtcNow()`. Домен сознательно остаётся на системных часах: `Entity.CreatedAt/UpdatedAt` и
`User.RecordLogin` — технические метки аудита, от них не зависит ни одно доменное правило;
это записано в XML-комментариях `Entity` и `RecordLogin` и в `AGENT.md` §7.6. Тесты:
`JwtTokenIssuerTests` (Infrastructure, 2 `[Fact]`) — `IssuedAt`/`ValidTo` распарсенного
`JsonWebToken` равны `fakeNow + AccessTokenMinutes` (JWT хранит секунды, сравнение с точностью
до секунды), `ExpiresInSeconds == 900`, а сдвиг фейковых часов на час смещает и `iat`, и `exp`
ровно на 3600 с; `MarkBookingNoShowTests` (Application, 3 `[Fact]`) — до начала брони
`booking.markNoShow.tooEarly` без `UpdateAsync`, после начала `NoShow` и ровно один `UpdateAsync`,
а сдвиг часов за `StartAt` меняет ответ того же обработчика; существующий тест владения обновлён
передачей фейковых часов. Фейковое время (2025-03-04 и 2025-05-06) заведомо отличается от
системного: при временном возврате `DateTime.UtcNow` в `JwtTokenIssuer` падают оба JWT-теста,
при возврате `DateTimeOffset.UtcNow` в `MarkBookingNoShowHandler` — два теста неявки (проверено,
затем восстановлено). Итог: 150 domain + 186 application + 29 infrastructure, сборка — 0 ошибок,
0 предупреждений.

### R-28. Коды ошибок смешаны: репозиторные попадают в API — ✅ закрыто 2026-10-23

**Где:** `Infrastructure.Postgresql/Repositories/*` (`booking.getIdempotencyKey.notFound`,
`barberProfile.getById.notFound`, `user.add.uniqueViolation`), `Domain/*` (`booking.create.*`),
`Application/Common/CurrentUserExtensions.cs:37` (`auth.forbidden`).

**Почему:** `AGENT.md` §10.7 задаёт формат `{aggregate}.{method}.{detail}`, но не разделяет
«доменные» и «инфраструктурные» коды. Клиент не может отличить `booking.getById.notFound`
(«брони нет») от `booking.getIdempotencyKey.notFound` («ключа нет»), и оба выглядят как
часть контракта. Ретраи/логика клиента по кодам становится хрупкой.

**Как исправить:** зафиксировать в `AGENT.md` реестр публичных кодов (константы в `Contracts`)
и не отдавать наружу коды репозиториев; внутри — внутренние коды с префиксом.

**Сделано:** публичные коды собраны в `BarberSearch.Contracts/Common/ApiErrorCodes.cs` —
`ApiErrorCodes` с вложенными классами `Auth`, `Booking`, `BarberProfile`, `User`, `Salon`,
`HairService`, `Specialization`, `Review`, `RefreshToken`, `Common`; у каждой константы
XML-док с смыслом и HTTP-статусом. Полные коды ошибок сохранения строятся из префикса
операции (`AddPrefix`, `UpdatePrefix`, `DeletePrefix` и т. п.) и фабрик `UniqueViolation`,
`Overlap`, `Deadlock`, `LockTimeout`, `ForeignKeyViolation`, `CheckViolation`,
`NotNullViolation`, `ConcurrencyConflict`; важные полные коды (`booking.add.overlap`,
`barberProfile.add.uniqueViolation`, `booking.update.concurrencyConflict` и др.) перечислены
константами. `internal.database` — единственный внутренний код: `RepositoryBase` отдаёт его
на любой немаппируемый `DbUpdateException`/SQLSTATE, поэтому 500 больше не выдаёт операцию
репозитория (`.database` из кода убран). `RepositoryBase`, все 8 репозиториев и
`CurrentUserExtensions` используют только константы реестра, строковых литералов кодов в
`Infrastructure.Postgresql` не осталось.

Убраны имена методов репозитория из публичных кодов: `{aggregate}.getById.notFound` →
`{aggregate}.notFound` (User, BarberProfile, Booking, Review, HairService, Salon,
Specialization), `barberProfile.getCardById.notFound` → `barberProfile.notFound`,
`booking.getIdempotencyKey.notFound` → `booking.idempotencyKey.notFound`,
`user.getByEmail.notFound` → `user.notFound`, `review.getByBookingId.notFound` →
`review.notFound`, `refreshToken.getByHash.notFound` → `refreshToken.notFound`;
`user.getByEmail.email`/`user.emailExists.email` → `user.email.invalid`,
`refreshToken.getByHash.tokenHash` → `refreshToken.tokenHash.invalid`,
`booking.getIdempotencyKey.idempotencyKey` → `booking.idempotencyKey.invalid`,
`salon.getByCity.city` → `salon.city.invalid`, `hairService.getByCategory.category` →
`hairService.category.invalid`, `specialization.getByName.name` → `specialization.name.invalid`,
`specialization.getByName.notFound` → `specialization.notFound`; пагинация
`{aggregate}.getPaged.page|pageSize` → `{aggregate}.paging.page|pageSize`,
`booking.getPage.limit` → `booking.page.limit`, `review.getPublishedPage.limit` →
`review.page.limit`, `booking.getActiveByBarber.period` → `booking.activePeriod.invalid`,
`booking.hasActiveOverlap.period` → `booking.overlap.period`; заодно убраны `getService` и
`getSlots` (`barberProfile.service.notFound` / `barberProfile.slots.notFound`), а
`barberProfile.getById.invalidId` стал `barberProfile.id.invalid`. Это сознательное breaking
change HTTP-контракта перед продым: клиент, опиравшийся на старые имена, должен обновиться.

`CreateBarberProfileHandler` теперь ловит гонку двух create на
`ux_barber_profiles_user_id`: `barberProfile.add.uniqueViolation` от `AddAsync` маппится в тот
же `409 barberProfile.create.alreadyExists`, что и предполётная проверка (фейк
`FakeBarberProfileRepository` получил `AddBehavior`). Фейки репозиториев возвращают те же
коды, что и PostgreSQL-реализации. Тесты: `ApiErrorCodesTests` (4) — константы непусты и
уникальны, соответствуют `^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)+$`, не содержат запрещённых
имён методов, единственный `internal.*` — `internal.database`; `CreateBarberProfileTests`
(+1 на маппинг unique violation); `RepositorySaveFailureTests` (+1 на `internal.database`).
Негативный прогон: временно добавленный `booking.getById.notFound` роняет
`ApiErrorCodesTests.Constants_DoNotContainRepositoryMethodNames` (1 failed / 190 passed),
после восстановления — 191/191. Итог: 174 domain + 191 application + 33 infrastructure,
сборка — 0 ошибок, 0 предупреждений.

### R-29. `ICurrentUser.UserId` бросает `InvalidOperationException` — ✅ закрыто 2026-10-23

**Где:** `Web/Auth/HttpContextCurrentUser.cs:26-38`.

**Почему:** любой вызов на анонимном запросе — 500. Сейчас все эндпоинты защищены, но при
добавлении `[AllowAnonymous]` (R-25) любая забытая проверка даст 500 вместо 401. Кроме того,
исключение из property-getter плохо читается в стеке.

**Как исправить:** `TryGetUserId(out Guid)` или `Guid? UserId` + явная ошибка
`auth.unauthorized` в обработчике; минимум — задокументировать инвариант.

**Сделано:** бросающее свойство `Guid UserId` удалено из `ICurrentUser`; остались безопасный
`bool TryGetUserId(out Guid userId)` и `IsAdmin`. `HttpContextCurrentUser.TryGetUserId`
возвращает `false`, если `HttpContext` отсутствует, claim `sub` не найден или не парсится,
и ничего не бросает. В Application добавлен `CurrentUserExtensions.RequireUserId()` →
`Result<Guid, Failure>` с `Error.Unauthorized("auth.unauthorized", …)`; `EnsureCanManage`
и `EnsureIsBarberOfAsync` для анонима возвращают тот же `401 auth.unauthorized`, для
владельца/администратора поведение прежнее, для чужого — `403 auth.forbidden`. Прямые
чтения `_currentUser.UserId` заменены на `RequireUserId()` в `CreateBookingHandler`,
`CancelBookingHandler`, `CreateReviewHandler`, `CreateBarberProfileHandler` и
`GetBookingsHandler`; остальные обработчики по-прежнему идут через `Ensure*`.
`FakeCurrentUser` получил фабрику `Anonymous()`. Тесты: `CurrentUserAuthorizationTests` (6) —
`RequireUserId`/`EnsureCanManage`/`EnsureIsBarberOfAsync` на анониме дают
`401 auth.unauthorized`, `CancelBookingHandler` и `CreateBookingHandler` на анониме возвращают
`Failure` и ничего не пишут (нет `Update`/`Add`, lock не берётся, ключ идемпотентности не
читается), аутентифицированный `RequireUserId` возвращает идентификатор; `HttpContextCurrentUserTests`
(3) — без claim'а и без `HttpContext` `TryGetUserId` = `false` без исключения, с claim'ом
`sub` = `true` и правильный id. Без правки тесты падают (проверено временным возвратом броска
в адаптере и подстановкой `Guid.Empty` в фейке). Итог: 150 domain + 178 application +
27 infrastructure, сборка — 0 ошибок, 0 предупреждений.

### R-30. `UnitOfWork` откатывает транзакцию, но не очищает change tracker — ✅ закрыто 2026-10-23

**Где:** `Infrastructure.Postgresql/UnitOfWork.cs:48-67`.

**Почему:** после `Failure`/исключения в контексте остаются изменённые сущности. Если в рамках
одного scope (или после ретрая в том же запросе) произойдёт ещё один `SaveChanges`, откатанные
изменения могут попасть в БД.

**Как исправить:** `_context.ChangeTracker.Clear()` в `finally` после `Rollback` (или общий
хелпер `ResetAsync`), плюс тест на «после Failure повторный SaveChanges ничего не пишет».

**Сделано:** `UnitOfWork.ExecuteCoreAsync` получил флаг `committed` и `try/finally`:
`await using` транзакции перенесён внутрь `try`, поэтому при исключении из операции или
`CommitAsync` транзакция сначала откатывается при dispose, а затем `finally` вызывает
`_context.ChangeTracker.Clear()`. При `Failure` явный `RollbackAsync` выполняется один раз, и тот
же `finally` очищает трекер; повторного отката нет. При успешном коммите `committed = true`,
и трекер не очищается — обработчики по-прежнему могут работать с отслеживаемыми сущностями.
Исключение не превращается в `Failure` и уходит наружу; публичный контракт `IUnitOfWork` не
менялся, изменена только XML-дока `UnitOfWork`. Тесты — `UnitOfWorkTrackerTests` в
`Infrastructure.UnitTests` (3 `[Fact]`): транзакция подменена через
`ReplaceService<IDbContextTransactionManager, FakeTransactionManager>()`, фейковая транзакция
считает `Commit`/`Rollback` и откатывается при dispose, если её не завершили. Проверены три
сценария: `Failure` → ровно один `Rollback`, трекер пуст, повторный `SaveChanges` пишет 0 строк;
исключение → ровно один `Rollback`, трекер пуст, исключение ушло наружу; успех → `Commit`,
трекер не очищен. Без очистки первые два теста падают (проверено отключением строки). Итог:
150 domain + 172 application + 24 infrastructure. На локальной PostgreSQL 16 в отдельной БД
`barbersearch_verify_R30` со всеми миграциями операция добавила бронь, вернула `Failure`:
трекер опустел, `SaveChanges` после отката затронул 0 строк, в таблице `bookings` строки нет;
следующая операция в том же контексте закоммитила другую бронь — в БД ровно одна строка.
БД удалена после проверки. Сборка — 0 ошибок, 0 предупреждений.

### R-31. `JwtTokenIssuer` создаёт `JsonWebTokenHandler` на каждый токен — ✅ закрыто 2026-10-24

**Где:** `Web/Auth/JwtTokenIssuer.cs:49`.

**Почему:** `JsonWebTokenHandler` — потокобезопасный и переиспользуемый; создание на каждый
выпуск — лишние аллокации и инициализация на самом горячем auth-пути (login/refresh).

**Как исправить:** `private static readonly JsonWebTokenHandler Handler = new();`

**Сделано:** в `Web/Auth/JwtTokenIssuer.cs:31` добавлено `private static readonly JsonWebTokenHandler Handler = new();`, а `Issue` вызывает `Handler.CreateToken(descriptor)` вместо создания экземпляра на каждый токен. Claims, `TimeProvider`, `SigningCredentials`, сроки и форма `TokenResponse` не менялись; обработчик один на процесс, потому что `JsonWebTokenHandler` потокобезопасен. Регрессионный тест — `JwtTokenIssuerTests.Handler_IsSingleStaticReadonlyInstance` в `Infrastructure.UnitTests`: через reflection находит ровно одно приватное статическое поле типа `JsonWebTokenHandler`, проверяет `IsStatic`, `IsInitOnly` и что значение не `null`; если поле убрать, сделать экземплярным или снять `readonly`, тест падает (проверено временной заменой на `private readonly` — 1 failed / 43 passed, после восстановления 44/44). Итог: 181 domain + 196 application + 44 infrastructure, сборка — 0 ошибок, 0 предупреждений.

### R-32. Offset-пагинация в админских списках — ✅ закрыто 2026-10-24

**Где:** `Infrastructure.Postgresql/Repositories/UserRepository.cs:65-84`,
`BarberProfileRepository.cs:103-132`, `ReviewRepository.cs:54-84`.

**Почему:** `Skip((page-1)*pageSize)` деградирует с глубиной; для админских экранов это терпимо,
но keyset-механика (`CursorCodec`, `PagedResponse`) уже написана и переиспользуема.

**Как исправить:** перевести на keyset при появлении реальной глубины, либо зафиксировать
ограничение `page * pageSize <= N`.

**Сделано:** глубина зафиксирована на 10 000 без перевода на keyset. В `RepositoryBase` добавлены `internal const int MAX_PAGE_OFFSET = 10_000` и `internal static Failure? ValidatePageOffset(int page, int pageSize, string depthCode)`: произведение `page * pageSize` считается в `long` (переполнение при `page = int.MaxValue` исключено) и при превышении предела возвращает `Error.Validation` — `400` с переданным кодом и `InvalidField = "page"`. Helper вызывается после `ValidatePaging`, поэтому при недопустимых параметрах клиент по-прежнему получает прежние `*.paging.page` / `*.paging.pageSize`. Проверка подключена в `UserRepository.GetPagedAsync`, `BarberProfileRepository.GetPagedAsync` и `ReviewRepository.GetByBarberProfileIdAsync`; в `Contracts/Common/ApiErrorCodes.cs` добавлены `User.PagingDepthInvalid = "user.paging.depth"`, `BarberProfile.PagingDepthInvalid = "barberProfile.paging.depth"` и `Review.PagingDepthInvalid = "review.paging.depth"` (рядом с `PagingPageSizeInvalid`), XML-доки трёх методов и интерфейсов описывают предел и код. Salon/HairService/Specialization не тронуты. Тесты — новый `PagingDepthTests` в `Infrastructure.UnitTests` (7 `[Fact]`): ровно 10 000 (`50 * 200`) — `null`; 10 001 — `Failure` с нужным кодом и `ErrorType.VALIDATION`; `int.MaxValue * 200` — `Failure` без переполнения; три репозитория на неоткрытом `ApplicationDbContext` (`Host=localhost;Database=unused;…`) с `page = 51, pageSize = 200` возвращают `IsFailure` и `*.paging.depth` до SQL; при `pageSize = 201` побеждает прежний `user.paging.pageSize` — порядок проверок сохранён. Негативный прогон: с временно отключённым helper'ом падают 5 из 7 (три проводки уходят в БД и получают `Npgsql.PostgresException 3D000`), после восстановления — 7/7. Итог: 181 domain + 196 application + 51 infrastructure (+7), сборка — 0 ошибок, 0 предупреждений.

### R-33. `CreateBookingRequest.Price` принимается и игнорируется — ✅ закрыто 2026-10-24

**Где:** `Contracts/Bookings/CreateBooking/CreateBookingRequest.cs:21`.

**Почему:** поле в контракте, которое молча не работает, — источник поддержки («я передал цену,
почему она другая»). Плюс клиент может решить, что цена проверяется.

**Как исправить:** убрать поле из контракта (breaking change для одного эндпоинта — допустимо
до продакшена) либо пометить `[JsonIgnore]` и задокументировать.

**Сделано:** поле удалено из контракта, а не помечено `[JsonIgnore]`. В позиционном record
`CreateBookingRequest` осталось семь параметров в прежнем порядке (`BarberProfileId`,
`BarberServiceId`, `SalonId`, `StartAt`, `EndAt`, `IdempotencyKey`, `CustomerNote`); из XML-доки
убран абзац про `Price` и совместимость, чтобы контракт больше не обещал цену. Сам
`CreateBookingHandler` не менялся — он и раньше брал `BarberService.Price`; в его XML-доку
уточнено, что поля `price` в запросе больше нет (R-33). Обновлены все места конструирования:
`BookingCreateTests.CreateRequest`, `CurrentUserAuthorizationTests`, тело
`POST {{host}}/api/bookings` в `BarberSearch.Web.http` (у `POST …/services` цена услуги осталась),
`$bookingBody` в `deploy/smoke.ps1` и `BuildBookingRequest` в `load/harness/Runner.cs`; в
`load/seed/*.sql` `price` — колонка БД, а не поле запроса. Проверено, что старый клиент с лишним
`"price"` не получает `400`: сериализатор пропускает неизвестный член (default
`JsonUnmappedMemberHandling.Skip`), что подтверждено прогоном десериализации такого тела через
MVC `JsonOptions` с конвертером из `JsonSettings` — объект создаётся, поля читаются, а обработчик
цену из запроса не читает, поэтому валидное тело с лишним `price` проходит те же проверки и
получает прежний `201`, а не `400`. Тест-регрессия
`BookingCreateTests.CreateBookingRequest_HasNoPriceProperty` (reflection: у типа нет публичного
свойства `Price` и нет параметра `Price` у конструктора); тест про цену переименован в
`Create_PriceComesFromBarberService`. Тесты: 181 domain + 197 application (+1) + 51 infrastructure;
негативный прогон с временно добавленным свойством `Price` — 1 failed / 0 passed, после
восстановления 197/197; сборка — 0 ошибок, 0 предупреждений.

### R-34. Нет аудита изменений и доменных событий — ✅ закрыто 2026-10-24

**Где:** весь Application-слой, `Domain/Common`.

**Почему:** в логах только «id создан/изменён». Кто сменил роль, деактивировал пользователя,
скрыл отзыв — восстановить нельзя. Для сервиса с админскими операциями и рейтингом это
обязательный элемент (жалобы, разбор инцидентов).

**Как исправить:** исходящая таблица `audit_log` (actor, action, entity, payload, timestamp),
запись из Web-слоя через фильтр/декоратор команд; доменные события — отдельный срез.

**Сделано:** аудит введён отдельной таблицей `audit_log`, без доменных событий (они остаются
открытым пунктом §12.6 `AGENT.md`). Схема: `id uuid` (UUIDv7, PK `pk_audit_log`),
`occurred_at timestamptz NOT NULL`, `actor_user_id uuid NULL`, `action varchar(64) NOT NULL`,
`entity_type varchar(64) NOT NULL`, `entity_id uuid NOT NULL`, `payload jsonb NOT NULL`.
Индексы — `ix_audit_log_occurred_at` (`occurred_at DESC`), `ix_audit_log_entity_type_entity_id`,
`ix_audit_log_actor_user_id`. Внешнего ключа на `users` нет сознательно: аудит должен переживать
удаление пользователя, поэтому `actor_user_id` — исторический идентификатор, а не ссылка;
`AuditLogConfiguration` и XML-дока это фиксируют. Миграция — `AddAuditLog`; её `Down` —
`DropTable("audit_log")`.

Запись атомарна с изменением: `AuditTrail.Capture` вызывается в `RepositoryBase.SaveChangesAsync`
до `Context.SaveChangesAsync` и добавляет строки через `context.Set<AuditLogEntry>().AddRange(...)`,
поэтому они попадают в тот же `SaveChanges` и в ту же транзакцию `IUnitOfWork`. Откат операции
убирает и изменение, и аудит. Web-фильтр не подошёл бы: он пишет после коммита и не видит старых
значений; EF-интерсептор не подошёл, потому что `AddDbContextPool` создаёт интерсептор один раз
на пул и не может получить scoped `ICurrentUser` — `AddDbContextPool` оставлен как есть.
`AuditTrail` — scoped-сервис (`ICurrentUser` + `TimeProvider`), зарегистрирован в `AddRepositories`
до репозиториев; конструкторы всех восьми репозиториев получили
`(ApplicationDbContext context, AuditTrail? auditTrail = null)`, поэтому прежние вызовы
`new XRepository(context)` в тестах компилируются без изменений.

Аудируются только `User`, `BarberProfile` и `Review` в состояниях Added/Modified/Deleted.
Allowlist: User — `Email`, `Role`, `IsActive`, `IsEmailVerified`, `IsPhoneVerified`,
`PasswordHash`, `SecurityStamp`, `BarberProfileId`; BarberProfile — `UserId`, `Grade`, `IsActive`,
`IsVerified`, `Rating`, `ReviewsCount`; Review — `BookingId`, `BarberProfileId`, `UserId`,
`Rating`, `Text`, `IsPublished`. Логины (`LastLoginAt`, `FailedLoginAttempts`, `LockoutEnd`) и
`UpdatedAt`/`xmin` в аудит не попадают: это шум, в котором не видно смены роли и скрытия отзыва.
Для Modified пишутся только реально изменённые allowlist-свойства (`IsModified` и
`OriginalValue != CurrentValue`), поэтому повторное сохранение отсоединённого графа через
`Context.Update` ложных строк не создаёт; если изменений нет — строки нет. Payload —
`{"values":{…}}` для created/deleted и `{"changes":{"role":{"from":"Customer","to":"Admin"}}}`
для updated; camelCase, enum'ы строками. Значения свойств, имя которых содержит `password`,
`hash`, `stamp`, `token` или `secret` (без учёта регистра), заменяются на `"[redacted]"`;
строки длиннее 500 символов обрезаются с маркером `...` (длина не превышает предел). `action` —
`{entity}.{created|updated|deleted}`, `entity_type` — стабильные `user` / `barber_profile` /
`review`, `OccurredAt` — одно значение `TimeProvider.GetUtcNow()` на весь `Capture`,
`ActorUserId` — `TryGetUserId` (аноним → `null`), `EntityId` — из `Id`.

Тесты в `Infrastructure.UnitTests`: `AuditTrailTests` (8 `[Fact]`, без реальной БД — контекст на
`UseNpgsql("Host=localhost;Database=unused;…")` без `SaveChanges`): создание пользователя
(`user.created`, `values.email`/`values.role`, `passwordHash`/`securityStamp` = `"[redacted]"`,
actor и время из FakeTimeProvider); смена роли (`user.updated`, `changes.role.from/to`,
`securityStamp` тоже `[redacted]`); `Verify()` профиля (`barber_profile.updated`,
`changes.isVerified`); `Review.Hide()` (`review.updated`, `changes.isPublished` true→false);
текст отзыва длиннее 500 символов обрезается; `RecordLogin()` не даёт ни одной строки; `Salon`
вне allowlist не даёт строк; payload создания не содержит хеш пароля в открытом виде.
`AuditLogModelTests` (4 `[Fact]`): таблица `audit_log`, PK `pk_audit_log`, `ValueGeneratedNever`,
колонки и типы (`payload` → `jsonb`), ровно три индекса, `occurred_at DESC` (design-time модель),
отсутствие FK. Негативный прогон: с временно отключённым `AuditTrail.Capture` падают 6 из 8
аудит-тестов (2 теста «ничего не пишется» проходят и так); после восстановления — 63/63.
Итог: 181 domain + 197 application + **63** infrastructure (+12), сборка — 0 ошибок,
0 предупреждений.

Проверено на реальной PostgreSQL: создана отдельная БД `barbersearch_verify_r34`, применены все
миграции (включая `AddAuditLog`). Временный консольный проект в `%TEMP%` с настоящим DI
(`AddPostgresInfrastructure(...).AddRepositories()`, scoped `ICurrentUser`, `TimeProvider.System`)
создал пользователя и сменил роль через `UserRepository`; в `audit_log` появились две строки:
`actor=01a16a97-a113-7494-8095-f48d323dd57b action=user.created entity=user
payload={"values": {"email": "audit-r34@example.com", "role": "Customer", "passwordHash":
"[redacted]", "securityStamp": "[redacted]", …}}` и `action=user.updated` с
`changes.role.from=Customer/to=Admin` (и `securityStamp` = `[redacted]`). Атомарность: в
`IUnitOfWork.ExecuteAsync` пользователь деактивировался и сохранялся, затем операция вернула
`Failure`; число строк `audit_log` до и после — 3 и 3, пользователь остался активным. БД удалена,
временный проект удалён. Отдельно `psql` подтвердил схему: `payload jsonb NOT NULL`, индексы
`ix_audit_log_occurred_at btree (occurred_at DESC)`, `ix_audit_log_entity_type_entity_id`,
`ix_audit_log_actor_user_id`; в базе 16 таблиц. Замечание по окружению: на хосте порт 5432
слушает локальный PostgreSQL 16.3, поэтому `Host=localhost` в этой среде попадает в него, а не в
контейнер `barbersearch-db` (PostgreSQL 18). Проверка выполнена на реальной PostgreSQL 16.3 в
отдельной БД — так же, как в R-30/R-06; контейнерная БД `barbersearch_verify_r34` создана и
удалена, база `barbersearch` не тронута.

### R-35. Отзыв публикуется сразу при создании — ✅ закрыто 2026-10-24

**Где:** `Domain/Reviews/Review.cs:82` (`IsPublished = false`; было `true` на строке 68).

**Почему:** модерации нет: любой завершённый визит сразу влияет на публичный рейтинг. Для
рейтинга, который показывается в поиске и от которого зависит выдача, это продуктовый риск
(накрутка отзывами с фейковых броней — см. R-02, R-04).

**Как исправить:** решить продуктово: `IsPublished = false` + модерация, либо автопубликация
с постмодерацией и жалобами. Зафиксировать решение в `AGENT.md` §12.

**Сделано:** принято продуктовое решение — **пре-модерация**, а не постмодерация. В домене
`Review.Create` теперь ставит `IsPublished = false` (XML-доки класса и фабрики описывают
«создаётся на модерации, публикует администратор»); `Publish`/`Hide`/`Update` не менялись.
`CreateReviewHandler` больше не зависит от `ReviewRatingRecalculator` и не пересчитывает
профиль: скрытый отзыв не влияет на публичную сводку, лог — «created and awaits moderation».
Валидации брони/владения/уникальности не тронуты. Рейтинг по-прежнему меняется на
`Publish`/`Hide` и на правке опубликованного отзыва (`UpdateReviewHandler`).

Чтобы администратор находил скрытые отзывы, добавлена очередь модерации:
`IReviewRepository.GetModerationPageAsync(ReviewCursor?, int, …)` и реализация в
`ReviewRepository` — только `is_published = false`, всех барберов, `created_at ASC, id`,
keyset (`CreatedAt > cursor.CreatedAt || (== && Id.CompareTo(cursor.Id) > 0)`), `limit <= 0` →
`review.page.limit`; `ReviewCursor` хранит пару, направление задаёт запрос. Application:
`GetModerationQueueQuery`/`GetModerationQueueHandler` (`Pagination.ValidatePageSize(pageSize,
"review.moderation")`, `limit = pageSize + 1`, `NextCursor` из последнего элемента). Web:
`[Authorize(Policy = AdminOnly)] [HttpGet("moderation")]` в `ReviewsController`, ответ
`PagedResponse<ReviewResponse>`; XML-доки контроллера обновлены (создание — на модерации,
публикация/скрытие — админ, очередь скрытых). `GET api/reviews/moderation` — 82-й endpoint.

Схема: в `ReviewConfiguration` добавлен частичный индекс
`ix_reviews_moderation_created_at_id` по `(created_at, id)` с фильтром `is_published = false`
(ASC); миграция `AddReviewModerationIndex` (`Up` — `CREATE INDEX … WHERE is_published = false`,
`Down` — `DropIndex`), snapshot обновлён, `has-pending-model-changes` — «No changes…». Индекс
проверен на отдельной БД `barbersearch_verify_r35` (PostgreSQL 16.3): `\d reviews` /
`pg_indexes` дают `CREATE INDEX ix_reviews_moderation_created_at_id ON public.reviews USING
btree (created_at, id) WHERE (is_published = false)`; `Down` до `AddAuditLog` удаляет индекс,
повторный `Up` возвращает; БД удалена, `barbersearch` не тронута.

Тесты: domain-тест `Create_StartsPublished` переименован в `Create_StartsPendingModeration`
(ожидает `false`, затем `Publish()` → `true`); application — `Create_PublishedReview_…` →
`Create_PendingReview_DoesNotChangeRating` (профиль остаётся `null`/0, отзыв скрыт), новый
`Publish_CreatedReview_UpdatesRatingAndCount` (создание обработчиком → чтение из
`FakeReviewRepository` → `PublishReviewHandler` → 5.00/1), helper `CreateReview(…)` публикует
только при `published == true`; `FakeReviewRepository.GetModerationPageAsync` повторяет
фильтр/ASC/курсор; `ReviewModerationTests` (2) — 2 скрытых + 1 опубликованный, pageSize=1 →
старый скрытый первым и `NextCursor`, вторая страница → второй скрытый и `NextCursor = null`,
опубликованный не попадает; невалидный `pageSize` → `400 review.moderation.pageSize`.
Infrastructure — `ReviewModerationIndexModelTests`: имя, колонки `(created_at, id)`, фильтр,
ASC. Попутно `AuditTrailTests.Capture_WhenReviewHidden_…` публикует отзыв перед `Hide`,
`ReviewReadTests`/`ReviewOwnershipTests` обновлены под новый `Review.Create`/конструктор.
Итог: 181 domain + 200 application (+3) + 64 infrastructure (+1); сборка 0/0; негативный прогон
с временно возвращённым `IsPublished = true` — по 1 failed в
`Create_StartsPendingModeration` и `Create_PendingReview_DoesNotChangeRating`, после
восстановления все зелёные.

Load harness: `BatchOutcome.CreatedIds` (init, по умолчанию `[]`), после warmup и каждого
измеренного батча `RunCreateReviewBatchAsync` отдаёт созданные id, harness публикует их
админом (`POST api/reviews/{id}/publication`) и только потом проверяет рейтинг; публикация не
входит в статистику create, не-204 — сценарий failed. `LOADTEST.md`/`load/README.md` отмечают,
что с 2026-10-24 create оставляет отзыв на модерации, а старые p95 — верхняя оценка (create
больше не пересчитывает рейтинг). `deploy/smoke.ps1` отзывов не трогает; в
`BarberSearch.Web.http` комментарий обновлён.

Ограничение (follow-up): автор не видит свой отзыв до публикации — публичный список отдаёт
только `IsPublished`, отдельного author-facing status list нет; это осознанно оставлено
следующим срезом.

### R-36. Нет интеграционных тестов — ✅ закрыто 2026-10-24

**Сделано:** проект `tests/BarberSearch.IntegrationTests` (net10.0, xunit 2.9.2,
`Microsoft.AspNetCore.Mvc.Testing` 10.0.11, `Testcontainers.PostgreSql` 4.16.0, Npgsql 10.0.3),
добавлен в `BarberSearch/BarberSearch.sln`; в `BarberSearch.Web/Program.cs` добавлен
`public partial class Program { }` для `WebApplicationFactory<Program>`, другого изменения
прод-кода нет.

Фикстура и изоляция:
- контейнер `postgres:18` (образ из локального кэша Docker) один на xUnit-collection;
  `Database.Migrate()` применяется один раз, перед каждым тестом — `TRUNCATE … RESTART IDENTITY
  CASCADE` по всем таблицам `public`, кроме `__EFMigrationsHistory`. Отдельная база на тест не
  нужна: тесты одной collection идут последовательно, а миграции на каждый тест были бы медленнее;
- `BARBERSEARCH_TEST_POSTGRES` (строка подключения Npgsql к существующему серверу) переключает
  на внешнюю PostgreSQL для CI без Docker: создаётся уникальная база `barbersearch_it_<guid>`
  на прогон и удаляется в `DisposeAsync` (`DROP … WITH (FORCE)` + `NpgsqlConnection.ClearAllPools()`);
- WAF создаётся на каждый тест: отдельные `ConnectionStrings:Postgres`, `Auth:SigningKey`
  (≥32 символов) и `DataProtection:KeysPath` (временный каталог, удаляется), окружение
  `Testing`. Настройки выставляются переменными окружения: minimal hosting читает конфигурацию
  в `WebApplication.CreateBuilder` раньше, чем применяются `ConfigureAppConfiguration` WAF.
  Провайдеры логирования очищаются (`ClearProviders()` + `Logging__EventLog__LogLevel__Default=None`),
  иначе на Windows запись warning в EventLog даёт 500 на первом запросе. Отдельный хост на тест
  изолирует rate limiter: `RemoteIpAddress = null` → партиция `unknown`, auth-лимит 10/мин,
  иначе второй сценарий получил бы 429.

Сценарии (6 `[Fact]`, все зелёные, повторный прогон тоже):
1. `BookingFlowTests` — регистрация → login (JWT) → `POST api/bookings` 201 → повтор на тот же
   слот 409 `booking.add.overlap` (exclusion `ex_bookings_no_overlap`) → отмена 204 → повторная
   бронь освободившегося слота 201.
2. `SearchKeysetTests` — 7 профилей (рейтинги 4.50, 4.50, 3.00, 3.00, null, null, null),
   `pageSize=2`, обход всех страниц: порядок `(rating IS NULL), rating DESC, id`, NULL строго
   последними, без дублей и пропусков (4 страницы).
3. `PrimarySalonTests` — два салона, `PUT …/salons/{id}/primary` в обе стороны: 204 и ровно один
   `is_primary` в БД (deferrable exclusion-ограничение R-06).
4. `ConcurrentBookingTests` — 6 параллельных POST на один слот: ровно один 201, остальные 409.
5. `RefreshReuseTests` — ротация refresh-токена, повтор старого 401 `auth.refresh.reused`,
   после чего и новый токен 401 `auth.refresh.revoked` (цепочка отозвана).
6. `HealthTests` — `/health/live` и `/health/ready` 200 на мигрированной базе.

Запуск (Docker Desktop запущен; вывод тестов — вне workspace, иначе testhost падает, §5.2):
```powershell
dotnet build BarberSearch/BarberSearch.sln --nologo
dotnet test tests/BarberSearch.IntegrationTests --nologo --verbosity quiet "-p:BaseOutputPath=$env:TEMP\bs-tests\IntegrationTests\"
# CI без Docker:
$env:BARBERSEARCH_TEST_POSTGRES='Host=127.0.0.1;Port=5432;Username=postgres;Password=postgres;Database=postgres'
```
В этом SDK короткий `-q` у `dotnet test` уходит в MSBuild как **question mode** (`/q`), а не quiet:
на несобранном дереве он падает с «Building target … completely». Поэтому используется длинная
форма `--verbosity quiet` (эквивалент `-v:m`); после сборки работает и
`dotnet test tests/BarberSearch.IntegrationTests --nologo -q --no-build "-p:BaseOutputPath=…"`.

Итог: сборка 0/0; интеграционные 6/6 (два прогона — Testcontainers и внешняя PostgreSQL 16.3);
регресс 181 domain + 200 application + 64 infrastructure — зелёные. Ограничения: нужен Docker
либо `BARBERSEARCH_TEST_POSTGRES`; collection одна и parallelization выключена, поэтому
`TRUNCATE`-изоляция предполагает последовательный запуск; cookie-ветка с antiforgery, CORS и
rate limiting остаются за `deploy/smoke.ps1` (кандидаты на следующие интеграционные сценарии).

**Где:** `tests/` — только unit-тесты с фейками (`tests/BarberSearch.Application.UnitTests/Fakes/*`).

**Почему:** не покрыты: маппинг EF, индексы, exclusion-ограничение, keyset-предикаты (порядок
`(rating IS NULL), rating DESC, id` — самое хрупкое место поиска), миграции, конвейер auth,
antiforgery, rate limiting, health checks. Всё это проверяется ручным `deploy/smoke.ps1`;
на каждый рефакторинг приходится гонять скрипт руками.

**Как исправить:** Testcontainers (PostgreSQL) + `WebApplicationFactory` для 5–7 ключевых
сценариев: регистрация → логин → бронь → слот занят → отмена; поиск с курсором на границе
групп рейтинга; `SetPrimarySalon` (R-06); конкурентные брони (1×201 / N×409); refresh-reuse.
Это же закроет регресс на R-05/R-07.

---

## 5. P3 — низкий приоритет / долг

### R-37. Нет `Directory.Build.props`, `.editorconfig`, `global.json`, CI, Dockerfile — ✅ закрыто 2026-10-24

**Где:** корень `backend/` (было: настройки размазаны по всем `.csproj`, общих файлов нет);
новые `backend/Directory.Build.props`, `backend/global.json`, `backend/.editorconfig`,
`backend/Dockerfile`, `backend/.dockerignore`, `.github/workflows/backend-ci.yml` в корне
репозитория.

**Почему:** каждый проект задавал `Nullable`/`ImplicitUsings` сам, SDK не зафиксирован,
предупреждения анализаторов никого не останавливали, CI и образа API не было. Это самый
дешёвый рычаг качества: `TreatWarningsAsErrors` + анализаторы ловят дефекты на сборке,
а не на ревью.

**Как исправить:** общий `Directory.Build.props` на весь `backend/` (включая тесты и
`load/harness`), `global.json` с 10.0.303 и `rollForward: latestFeature`, `.editorconfig`
с `root = true` и явными severity анализаторов, GitHub Actions с restore/build/тестами,
multi-stage Dockerfile с non-root-пользователем и `.dockerignore`.

**Сделано:**
- `Directory.Build.props`: `Nullable`, `ImplicitUsings`, `LangVersion=14.0`, `Deterministic`,
  `DebugType=portable`, `GenerateDocumentationFile=false` (иначе 1054 × CS1591, AGENT §10.15),
  `EnableNETAnalyzers`, `AnalysisLevel=latest-recommended`, `AnalysisMode=Recommended`,
  `TreatWarningsAsErrors=true`, `ContinuousIntegrationBuild=true` только при `CI=true`.
  `TargetFramework` намеренно не дублируется — он остаётся у каждого проекта.
- `global.json`: SDK `10.0.303`, `rollForward: latestFeature`; `dotnet --version` из
  `backend/` — `10.0.303`.
- `.editorconfig`: `root=true`, `charset=utf-8`, CRLF, отступы 4/2, файловые namespace и
  базовые C#-стили, `IDE0005=warning`. Из recommended-набора точечно заглушены:
  `CA1707` (SCREAMING_SNAKE_CASE константы, AGENT §10.9, и xUnit-имена `Method_Scenario`),
  `CA1848`/`CA1873` (перевод логирования на `LoggerMessage` — отдельный рефакторинг),
  `CA1716` (C#-only: `Shared`, `Error`, `Auth.Me`, `date`/`to` — устоявшийся контракт;
  переименование — breaking change). Локально: `CA1861`/`CA1825` — сгенерированные EF-миграции;
  `CA1304`/`CA1311`/`CA1862` — `x.Name.ToLower()` транслируется в SQL `lower()` (замена
  изменила бы SQL); `CA1711` — xUnit collection definition. `CA1067` (R-41) не срабатывал:
  `Equals(object)` уже был переопределён; недостающий интерфейс `IEquatable<Entity>` добавлен
  отдельным срезом R-41.
- Вместо заглушения исправлены тривиальные замечания: `CA1859` в `AuditTrail`/тесте
  `ApiErrorCodesTests`/`load/harness/Runner.cs`, `CA1826` в `RepositoryBase`, `CA1305`
  в `BarberProfile` и `BarberProfileTests` (InvariantCulture), `CA1869` в `JsonSettingsTests`
  (кэш `JsonSerializerOptions`), `CA1822` — удалено мёртвое свойство в тестовой фейк-транзакции.
  Поведение прод-кода не менялось.
- `Dockerfile`: two-stage `sdk:10.0` → `aspnet:10.0`, publish `BarberSearch.Web` в Release,
  `UseAppHost=false`, `USER $APP_UID` (uid 1654), `EXPOSE 8080`,
  `ENTRYPOINT ["dotnet","BarberSearch.Web.dll"]`; контекст — `backend/`.
- CI: `.github/workflows/backend-ci.yml` — push/PR, `working-directory: backend`,
  `setup-dotnet@v4` с `10.0.x` и кэшем NuGet, `restore`, Release-build, unit-тесты
  (domain/application/infrastructure) и интеграционные тесты (Testcontainers на
  `ubuntu-latest`); секретов нет.
- Документация: `AGENT.md` §5 (build/CI/Docker-команды), `DEPLOY.md` §9 (сборка и запуск
  образа, переменные окружения).

Проверка (2026-10-24, из `backend/`):
```powershell
dotnet --version                                                  # 10.0.303
dotnet build BarberSearch/BarberSearch.sln --nologo               # 0 ошибок / 0 предупреждений
dotnet build BarberSearch/BarberSearch.sln --nologo -c Release   # 0 / 0
dotnet build load/harness/LoadHarness.csproj --nologo            # 0 / 0
dotnet test tests/BarberSearch.Domain.UnitTests --nologo --verbosity quiet "-p:BaseOutputPath=$env:TEMP\bs-tests\Domain\"                 # 181/181
dotnet test tests/BarberSearch.Application.UnitTests --nologo --verbosity quiet "-p:BaseOutputPath=$env:TEMP\bs-tests\Application\"       # 200/200
dotnet test tests/BarberSearch.Infrastructure.UnitTests --nologo --verbosity quiet "-p:BaseOutputPath=$env:TEMP\bs-tests\Infrastructure\" # 64/64
dotnet test tests/BarberSearch.IntegrationTests --nologo --verbosity quiet "-p:BaseOutputPath=$env:TEMP\bs-tests\Integration\"             # 6/6 (Testcontainers)
docker build -t barbersearch-api:r37 -f Dockerfile .             # успешно
# контейнер: uid 1654(app); GET /health/live → 200; GET /health/ready → 200 (существующая БД)
```
Ограничения: CI **не запускался на GitHub** — проверены только YAML (парсится, `on:
push/pull_request`, `working-directory: backend`, `setup-dotnet 10.0.x`) и локальный прогон
тех же команд; `dotnet format` по всему дереву не гонялся (IDE-правила в сборке не
включены); образ не публиковался, проверен только локальный `docker build`/`run`.

### R-38. Три пересекающихся индекса на barber_profiles — ✅ закрыто 2026-10-24

**Где:** `BarberProfileConfiguration.cs` (было `HasIndex(x => new { x.IsActive, x.IsVerified, x.Rating })`
→ `ix_barber_profiles_is_active_is_verified_rating`), миграции `AddReadIndexes` (raw SQL
`ix_barber_profiles_search`: `(rating DESC NULLS LAST, id) WHERE is_active AND is_verified`) и
`AddSearchKeysetIndex` (`ix_barber_profiles_search_keyset`:
`(((rating IS NULL)), rating DESC, id) WHERE is_active AND is_verified`); новая миграция
`20261024185404_DropRedundantBarberProfileIndexes` и тест
`tests/BarberSearch.Infrastructure.UnitTests/BarberProfileIndexModelTests.cs`.

**Почему:** на `barber_profiles` жили три индекса с одним частичным предикатом
`is_active AND is_verified`. `SearchAsync` сортирует `(rating IS NULL), rating DESC, id` и может
использовать только expression-индекс `ix_barber_profiles_search_keyset`; `ix_barber_profiles_search`
(`rating DESC NULLS LAST, id`) и `ix_barber_profiles_is_active_is_verified_rating`
(`is_active, is_verified, rating DESC`) — его подмножества по порядку, но каждый платит за себя:
лишние страницы при INSERT/UPDATE профиля, вакуум, место и варианты для планировщика. Индексы
добавлялись аддитивно («на всякий случай»), их реальная нужность не проверялась.

**Как исправить:** снять `EXPLAIN (ANALYZE, BUFFERS)` настоящих запросов поиска на объёме
20–50 тыс. профилей (первая страница, глубокая страница с курсором, каждый фильтр `SearchAsync`),
плюс `pg_stat_user_indexes.idx_scan` после сброса счётчиков и `enable_seqscan=off`; оставить
индекс, который выбирает планировщик; удалить остальные **новой** миграцией (применённые не
редактировать), вернуть оба в `Down`, обновить snapshot и зафиксировать отсутствие индекса в
EF-модели тестом.

**Сделано:**

Проверка на реальной PostgreSQL 16.3 (`localhost:5432`, отдельная БД `barbersearch_verify_r38`,
база `barbersearch` не трогалась): все миграции применены, засидено 40 000 профилей (32 000
видимых, 21 333 с рейтингом и 10 667 с `rating = NULL`), 200 салонов, 40 000 связей
`barber_salons`, 120 000 услуг, 280 000 строк расписания, `ANALYZE` выполнен. Запросы — реальные
`GET /api/barbers` к запущенному приложению (6 форм: без фильтров, `city`, `specializationId`,
`hairServiceId`, `date`, `city+date`; глубокая страница — обход 40 страниц `pageSize=20` и замер
41-й, курсор `(rating=4.93, id=…39793)`); SQL взят из EF-лога и выполнен через `PREPARE`/
`EXPLAIN (ANALYZE, BUFFERS)`, чтобы параметры совпадали с настоящими.

Планы «до» (три индекса в БД) — все семь форм идут `Index Scan using
ix_barber_profiles_search_keyset`, `Sort` отсутствует:
- первая страница: `Limit` cost 4.82, actual 0.038–0.162 ms, buffers 23;
- `city=Moscow`: 0.046–0.273 ms; `date` (day_of_week=1): 0.148–1.008 ms;
- `specializationId`: 6.540–6.611 ms (1 488 строк индекса до первых 21 совпадения);
- `hairServiceId`: 3.212–3.400 ms (821 строка); `city+date`: 5.609–6.058 ms (954 строки);
- глубокая страница: 1.153–1.192 ms, 800 строк отброшено фильтром курсора;
- `enable_seqscan=off` — тот же keyset Index Scan (0.017–0.049 ms): индекс способен обслужить
  запрос; `enable_indexscan=off` — `Seq Scan` 32 000 видимых строк + top-N `Sort` 33.7 ms, то есть
  цена отказа от индекса.
`pg_stat_user_indexes` после `pg_stat_reset()` и 47 реальных запросов: keyset `idx_scan = 47`
(`idx_tup_read = 20 593`), `ix_barber_profiles_search` — 0, `ix_barber_profiles_is_active_is_verified_rating`
— 0, `seq_scan` по `barber_profiles` — 0.

Решение: оставить только `ix_barber_profiles_search_keyset`. Его порядок
`(((rating IS NULL)), rating DESC, id)` — надмножество `(rating DESC NULLS LAST, id)`, предикат
совпадает у всех трёх, поэтому `ix_barber_profiles_search` не даёт ничего; других запросов,
фильтрующих только `is_active`/`is_verified` с сортировкой по `rating`, в коде нет. Гипотеза
подтверждена планами, а не принята на веру.

Миграция `20261024185404_DropRedundantBarberProfileIndexes`: `Up` — `DropIndex`
`ix_barber_profiles_is_active_is_verified_rating` плюс raw SQL `DROP INDEX IF EXISTS
ix_barber_profiles_search` (второй в EF-модели не объявлен, поэтому генератор его не видел);
`Down` — raw SQL создаёт `ix_barber_profiles_search` в исходном виде и `CreateIndex` возвращает
составной индекс; `ApplicationDbContextModelSnapshot` обновился. `has-pending-model-changes` →
«No changes have been made to the model since the last migration».

Проверка миграции на `barbersearch_verify_r38`: `Up` → в `pg_indexes` остались только
`ix_barber_profiles_search_keyset`, `ix_barber_profiles_languages`, `pk_barber_profiles`,
`ux_barber_profiles_user_id` (удалённых — 0); `Down` → оба индекса вернулись с теми же
определениями (`(rating DESC NULLS LAST, id) WHERE (is_active AND is_verified)` и
`(is_active, is_verified, rating DESC)`); повторный `Up` снова их удалил. Планы «после» не
изменились: все семь форм — по-прежнему `Index Scan using ix_barber_profiles_search_keyset` без
`Sort` (первая страница 0.056–0.383 ms, глубокая 0.715–0.735 ms, `enable_seqscan=off` — keyset
Index Scan 0.010–0.028 ms), `seq_scan` после 47 запросов — 0, keyset `idx_scan = 47`.

Тест `BarberProfileIndexModelTests` (infrastructure, без БД): в design-time модели
`barber_profiles` нет индекса `ix_barber_profiles_is_active_is_verified_rating`, нет
`ix_barber_profiles_search` и нет индекса по набору колонок `(IsActive, IsVerified, Rating)`
(проверка по колонкам ловит переименованный дубликат); при этом `ux_barber_profiles_user_id`
и `ix_barber_profiles_languages` на месте. Итог: сборка 0 ошибок / 0 предупреждений; тесты
181 domain + 200 application + **65** infrastructure (было 64) + 6 integration — зелёные.

Ограничения: замеры — на 40 000 профилей (не продовый объём), `LOADTEST.md` и исторические
цифры §15.7 не перемерялись; тайминги `EXPLAIN` шумят в пределах миллисекунд, вывод опирается на
форму плана (`Index Scan` vs `Seq Scan` + `Sort`) и счётчики `idx_scan`; `PREPARE` воспроизводит
SQL и параметры EF, но не сам протокол Npgsql.

### R-39. `Failure.Errors` возвращает новую `ReadOnlyCollection` на каждое обращение — ✅ закрыто 2026-10-24

**Где:** `BarberSearch/src/Shared/Failure.cs:28` (было:
`public IReadOnlyList<Error> Errors => _errors.AsReadOnly();`).

**Почему:** `List<T>.AsReadOnly()` создаёт новый `ReadOnlyCollection<T>` на **каждый** вызов.
`FailureProblemDetails.CreateResult` читает `failure.Errors` в пути каждого неуспешного ответа,
а обработчики Application дополнительно проходят по нему (`Any`, `All`, `Select`). Ошибка —
не исключение, поэтому лишние аллокации приходятся на частый путь.

**Как исправить:** создать read-only view один раз (поле, инициализируемое в обоих
конструкторах после `_errors`) и возвращать его. Публичный контракт не меняется: свойство
по-прежнему `IReadOnlyList<Error>`, сам `Failure` — `IEnumerable<Error>`. После конструктора
`_errors` не мутируется, поэтому `ReadOnlyCollection<T>` над `List<T>` потокобезопасен для
параллельного чтения.

**Сделано:**
- `Shared/Failure.cs`: поле `private readonly ReadOnlyCollection<Error> _errorsView`; оба
  конструктора (`IEnumerable<Error>` и `Error`) инициализируют его `_errors.AsReadOnly()`;
  `Errors => _errorsView`. XML-док фиксирует причину и гарантию потокобезопасности.
  `Error`/`ErrorType` не тронуты — это R-40.
- `tests/BarberSearch.Application.UnitTests/FailureTests.cs` — 6 `[Fact]`:
  `Assert.Same` на повторных обращениях (оба конструктора и пустой набор); состав и порядок
  ошибок из `IEnumerable<Error>`; одиночная ошибка из конструктора `Error`; `IList<Error>.IsReadOnly
  == true` + `NotSupportedException` на `Add` при неизменном `Count`; снимок исходной коллекции
  (мутация источника не меняет `Failure`); пустой набор (`Assert.Empty`).
- Проверка: сборка — 0 ошибок / 0 предупреждений; application — **206/206** (было 200, +6);
  регресс — 181 domain, 65 infrastructure, 6 integration, все зелёные. Red-green: с прежней
  реализацией (`=> _errors.AsReadOnly()`) `Errors_ReturnsSameInstance_OnEveryAccess` падает
  (1 из 6), после возврата поля — снова 206/206.
- Ограничение: view остаётся обёрткой над тем же `List<T>`, а не копией на чтении —
  неизменяемость держится на отсутствии мутаций `_errors` после конструктора (копия на каждом
  чтении убила бы смысл оптимизации).

### R-40. `Error` — record с приватным конструктором, `ErrorType.NONE` не используется — ✅ закрыто 2026-10-24

**Где:** `BarberSearch/src/Shared/Error.cs` (было: свойства `{ get; }`, в `ErrorType` — `NONE`);
`BarberSearch/src/BarberSearch.Web/Common/FailureProblemDetails.cs` (маппинг заканчивался `_ =>`).

**Почему:** все свойства были `{ get; }`, поэтому `error with { InvalidField = ... }` не
компилировался у потребителей: скопировать ошибку, поменяв только `InvalidField`/`Code`/`Message`,
было нельзя. `ErrorType.NONE` нигде не использовался, но молча маппился в 500 через `_ =>`;
так же молча в 500 попал бы любой новый тип ошибки без маппинга — ошибка конфигурации
маскировалась.

**Как исправить:** `Code`, `Message`, `Type`, `InvalidField` → `{ get; init; }`; приватный
конструктор и статические фабрики сохранить, чтобы снаружи по-прежнему нельзя было создать
`Error` в обход фабрик; удалить `NONE`; в `ToStatusCode` перечислить все типы явно
(`FAILURE` → 500), оставив `_ => 500` только как защиту от значения вне enum.

**Сделано:**
- `Shared/Error.cs`: свойства `init`-only (приватный конструктор и шесть фабрик `NotFound`,
  `Validation`, `Conflict`, `Failure`, `Unauthorized`, `Forbidden` не менялись); `NONE` удалён;
  XML-доки обновлены — объясняют `with` из другой сборки, приватность конструктора и отсутствие
  «неизвестного» типа. Снаружи по-прежнему нет ни публичного parameterless-, ни полного
  конструктора.
- `Web/Common/FailureProblemDetails.cs`: явный маппинг `VALIDATION`→400, `UNAUTHORIZED`→401,
  `FORBIDDEN`→403, `NOT_FOUND`→404, `CONFLICT`→409, `FAILURE`→500; `_ => 500` оставлен с
  комментарием про неопределённое значение enum. Ссылка в XML-доке исправлена на AGENT.md §7.5
  (было §11). Поведение снаружи не изменилось: enum в JSON и так сериализуется строками, в БД
  `ErrorType` не хранится, поэтому сдвиг числовых значений после удаления `NONE` ни на что не
  влияет.
- `tests/BarberSearch.Application.UnitTests/ErrorTests.cs` — 6 `[Fact]`: значения всех фабрик
  (дефолтные коды при `null`, `Guid` в `InvalidField` у `NotFound`); `with { InvalidField = ... }`
  и `with { Message = ... }` меняют только указанное поле, исходный экземпляр не меняется,
  `Code`/`Message`/`Type` сохраняются; равенство и `GetHashCode` для одинаковых и разных значений
  по всем четырём свойствам. `with` вызывается из тестовой сборки, которая не содержит
  `Error.cs`, — это и есть проверка исходного дефекта.
- `tests/BarberSearch.Infrastructure.UnitTests/ErrorMappingTests.cs` — 4 `[Fact]`: HTTP-код для
  каждого `Enum.GetValues<ErrorType>()` по словарю; набор ключей словаря совпадает с набором
  значений enum (новый тип без маппинга роняет тест); `FAILURE` → 500 явно; значение вне enum
  → 500.
- Проверка: сборка — **0 ошибок / 0 предупреждений**; application — **212/212** (было 206, +6);
  infrastructure — **69/69** (было 65, +4); регресс — 181 domain, 6 integration, все зелёные.
  Red-green: с возвращёнными `{ get; }` тестовый проект не компилируется (CS0200 на шести
  строках `with`), после возврата `init` — снова зелёный; с возвращённым `NONE` падают 2 теста
  маппинга (67 из 69), после удаления — снова 69/69.

### R-41. `Entity` не реализует `IEquatable<Entity>` при наличии `Equals(Entity?)` — ✅ закрыто 2026-10-25

**Где:** `BarberSearch/src/BarberSearch.Domain/Common/Entity.cs`;
`tests/BarberSearch.Domain.UnitTests/EntityTests.cs`.

**Почему:** типизированный `Equals(Entity?)` был, а контракта `IEquatable<Entity>` не было:
`EqualityComparer<Entity>.Default` и generic-код шли через `Equals(object?)` с проверкой типа.
CA1067 не срабатывал (R-37: `Equals(object)` уже переопределён) — дефект жил в контракте.

**Сделано:**
- `Entity`: `public class Entity : IEquatable<Entity>`; XML-доки класса и `Equals(Entity?)`
  объясняют сравнение по `Id` без `GetType()` (ради прокси EF Core), типизированный вызов через
  интерфейс без приведения к `object` (для значимых типов — без боксинга) и почему две разные
  сущности с `Guid.Empty` не равны (`ReferenceEquals` отсекает только совпадение с собой).
  Логика `Equals`/`GetHashCode`/`==`/`!=` сохранена.
- `EntityTests.cs` — 3 `[Fact]`: интерфейс реализован тем же `Equals(Entity?)`; вызов через
  интерфейс согласован с `==`/`Equals(object)`/хешем (прокси-подтип, `null` → false); разные
  Id → false. Существующие тесты не менялись.
- Проверка: Debug и Release — **0/0**; domain **184/184** (+3), application 212, infrastructure 69,
  integration 6 — зелёные. Red-green: без интерфейса — CS0266 ×2 (0 запущенных), с ним — 184/184.

### R-42. 201 без заголовка `Location` — ✅ закрыто 2026-10-25

**Где:** `Web/Common/ApiController.cs` (`ToCreatedResult`);
`Contracts/Common/CreatedResourceResponse.cs`.

**Почему:** 201 без `Location` выглядит недоделанным, но self-URI, читаемого сразу после
создания, нет ни у одного из восьми create-эндпоинтов:

| Create-эндпоинт | GET по id | Почему `Location` не ставится |
|---|---|---|
| `POST api/auth/register` | нет | только `GET api/auth/me` — текущий пользователь, не ресурс по id |
| `POST api/users` | нет | у `api/users` чтения нет |
| `POST api/barber-profiles` | нет | `GET api/barbers/{id}` — 404 до `IsActive && IsVerified` (`GetCardByIdAsync`); новый профиль не подтверждён |
| `POST api/bookings` | нет | только список `GET api/bookings` |
| `POST api/reviews` | нет | только список опубликованных и админская очередь; созданный отзыв скрыт до публикации (R-35) |
| `POST api/salons` | нет | у `api/salons` чтения нет |
| `POST api/hair-services` | нет | у `api/hair-services` чтения нет |
| `POST api/specializations` | нет | у `api/specializations` чтения нет |

Ссылка в никуда хуже её отсутствия, поэтому **сознательно отклонено**: 201 и тело `{ id }`
остаются как есть. Пересмотреть при появлении GET по id у созданного ресурса — тогда
`CreatedAtAction`/`Created(uri, value)` и интеграционный тест.

**Сделано:** XML-доки `ToCreatedResult` и `CreatedResourceResponse` фиксируют причину,
доказательство и условие пересмотра со ссылкой на R-42; сигнатуры и коды ответов не менялись.

### R-43. `FindBarberProfileSalon` возвращал доменную сущность из query-обработчика — ✅ закрыто 2026-10-25

**Где:** `Application/BarberProfiles/FindBarberProfileSalon/{FindBarberProfileSalonQuery,FindBarberProfileSalonHandler}.cs`;
`Web/Controllers/BarberProfilesController.cs` (`FindSalon`, приватный `ToResponse`).

**Почему:** обработчик запроса возвращал `BarberSalon`, а `BarberSalonResponse` собирался в контроллере
(`ToResponse`): доменный граф с навигацией `BarberSalon.BarberProfile` доходил до Web-слоя. Остальные
query-обработчики (`GetBarberProfile`, `GetBarberSlots`, `SearchBarbers`, `GetBookings`, `GetReviews`,
`GetModerationQueue`) отвечают DTO из `BarberSearch.Contracts` — этот выбивался.

**Сделано:**
- `FindBarberProfileSalonQuery : IQuery<BarberSalonResponse>`; обработчик —
  `IQueryHandler<BarberSalonResponse, FindBarberProfileSalonQuery>`,
  `Task<Result<BarberSalonResponse, Failure>>`.
- Маппинг домена в DTO перенесён в обработчик приватным статическим `ToResponse`; проверка владельца
  (`GetOwnerIdAsync` + `EnsureCanManage`) и узкое чтение (`GetSalonAsync`) не менялись, коды ошибок прежние.
- `BarberProfilesController.FindSalon` — `=> ToActionResult(await handler.Handle(...))`; приватный
  `ToResponse` и `using BarberSearch.Domain.BarberSalons` удалены, JSON-контракт прежний.
- Тест `FindSalon_LoadsOwnerAndOneSalon` усилен: `Assert.IsType<BarberSalonResponse>`, проверка
  `Id`/`BarberProfileId`/`SalonId`/`IsPrimary`, маппинг двух записей расписания (день/время/`IsDayOff`)
  и вызовы `["owner", "salon"]`.
- Проверка: Debug и Release — **0/0**; 184 domain + **212** application (+0) + 69 infrastructure +
  6 integration — зелёные. Red-green: с возвращённым `IQuery<BarberSalon>` тест падает
  `Assert.IsType`: expected `BarberSalonResponse`, actual `BarberSalon`; после возврата — 212/212.

### R-44. Дублирование переупорядочивания фото — ✅ закрыто 2026-10-25

**Где:** `Web/Controllers/PortfolioPhotosController.cs`; `Application/PortfolioPhotos/ReorderPortfolioPhoto/*`;
`Contracts/PortfolioPhotos/ReorderPortfolioPhoto/*`; `Domain/PortfolioPhotos/PortfolioPhoto.cs`.

**Почему:** один эффект жил в двух маршрутах и двух обработчиках: `PUT …/photos/{photoId}/sort-order` шёл через
`BarberProfile.ReorderPhoto` (проверка принадлежности фото + `MarkUpdated` корня), а `PUT …/photos/{photoId}/order` —
через `PortfolioPhoto.Reorder` напрямую, минуя агрегат. `HANDOFF.md` §2.1 (на момент находки): «один нужно убрать».

**Сделано:**
- Действие `Reorder` в `PortfolioPhotosController`, команда/обработчик `ReorderPortfolioPhoto` и контракт
  `ReorderPortfolioPhotoRequest` удалены вместе с `using`-ами; XML-док контроллера больше не обещает порядок.
- `PortfolioPhoto.Reorder(int)` стал `internal` (единственный вызывающий — `BarberProfile.ReorderPhoto` в той же
  сборке); XML-док называет публичной точкой входа метод агрегата. `BarberProfile.ReorderPhoto` и маршрут
  `…/sort-order` не менялись: поведение, код `barberProfile.reorderPhoto.notFound`, `MarkUpdated`.
- **Breaking change API:** `PUT api/barber-profiles/{id}/photos/{photoId}/order` удалён; клиенты используют
  `PUT …/photos/{photoId}/sort-order`. Новых эндпоинтов, изменений JSON/БД/`.sln` нет.
- Проверка: Debug и Release — **0/0**; 184 domain + 212 application + 69 infrastructure + 6 integration — зелёные.
  В коде не осталось `ReorderPortfolioPhoto` и маршрута `photos/{photoId:guid}/order`; `photo.Reorder(`
  вызывается только из `BarberProfile.ReorderPhoto` (внутренняя деталь), `PortfolioPhoto.Reorder` —
  только в документации о закрытии.

### R-45. `GetSlotContextAsync` хардкодил 30 минут — ✅ закрыто 2026-10-25

**Где:** `Infrastructure.Postgresql/Repositories/BarberProfileRepository.cs:497`
(`var durationMinutes = 30;`); XML-доки — `Application/BarberProfiles/GetBarberSlots/{BarberSlotContext,GetBarberSlotsQuery}.cs`,
`Application/BarberProfiles/IBarberProfileRepository.cs`,
`Contracts/BarberProfiles/GetBarberSlots/BarberSlotsResponse.cs`,
`Web/Controllers/BarbersController.cs`, `AGENT.md` §16.

**Почему:** шаг сетки по умолчанию (услуга не выбрана) был единственным магическим числом
в этом пути, и значение 30 дублировалось в коде и в XML-доках, поэтому смена политики
потребовала бы синхронной правки в нескольких местах. Вторая половина пункта — «берёт услугу
по `HairServiceId`» — уже закрыта R-20: `BuildSlotServiceQuery` фильтрует `barber_services`
по `barber_profile_id` + `hair_service_id` + `is_active` и проецирует `id`/`hair_service_id`/
`duration_minutes` одной строкой; переделывать этот путь не требовалось.

**Сделано:**
- `Domain/Constants.cs`: `public const int DEFAULT_SLOT_DURATION_MINUTES = 30;` рядом с блоком
  «Услуги»; комментарий фиксирует, что это шаг без услуги, а при выбранной услуге шаг —
  `BarberService.DurationMinutes` (R-20). Существующие константы и значения не менялись.
- `BarberProfileRepository.GetSlotContextAsync`: `var durationMinutes =
  Constants.DEFAULT_SLOT_DURATION_MINUTES;` (+ `using BarberSearch.Domain;`); других изменений
  логики нет — 30 минут остаются значением по умолчанию, выбранная услуга перекрывает его.
- XML-доки, называвшие «30 минут» словами, теперь ссылаются на константу
  (`<c>Constants.DEFAULT_SLOT_DURATION_MINUTES</c>`): `BarberSlotContext.DurationMinutes`,
  `GetBarberSlotsQuery` (2 места), `BarberSlotsResponse.DurationMinutes`,
  `IBarberProfileRepository.GetSlotContextAsync` (2 места), `BarbersController.GetSlots`,
  `AGENT.md` §16.
- Новых эндпоинтов/контрактов/миграций нет; поведение API не изменилось. Отдельный тест не
  добавлялся: извлечение константы с тем же значением; шаг 30 минут без услуги уже проверяет
  `BarberSlotsTests.Slots_UseSalonTimeZoneAndExplicitDayBounds` (9 часов → 18 слотов по
  30 минут), а услуга/длительность — `Slots_ServiceDuration_ControlsGridStep` и
  `BarberSlotServiceQueryTests` (R-20).
- Проверка: Debug и Release — **0/0**; domain **184/184**, application **212/212**,
  infrastructure **69/69**, integration **6/6**. `var durationMinutes = 30` в коде не осталось;
  `= 30;` — только в несвязанных константах (`RefreshToken.MAX_LIFETIME_DAYS`,
  `PostgresSettings.COMMAND_TIMEOUT_SECONDS`, таймаут запросов, лимит записи), сама константа —
  единственное место шага слотов.

### R-46. `appsettings.json` содержал рабочий пароль БД — ✅ закрыто 2026-10-25

**Где:** `Web/appsettings.json` (`ConnectionStrings:Postgres`), `Web/BarberSearch.Web.csproj`,
`Web/Deployment/StartupSettings.cs`, `DEPLOY.md` §1.1.

**Почему:** в репозитории лежала рабочая dev-строка подключения с паролем PostgreSQL. Это
противоречило собственному правилу `DEPLOY.md` («`appsettings.json` содержит только несекретные
значения»): пароль попадал в git и в контекст любой сессии, а заменить его на боевой без правки
файла было нельзя.

**Сделано:**
- `ConnectionStrings:Postgres` в `appsettings.json` оставлена пустой строкой; рабочего пароля
  в репозитории нет. Публичные значения остальных секций не менялись.
- В `BarberSearch.Web.csproj` добавлен `<UserSecretsId>barbersearch-web</UserSecretsId>` —
  без него `dotnet user-secrets` не работает.
- `StartupSettings.Load` по-прежнему падает на пустой строке, но сообщение теперь объясняет
  причину и даёт команду `dotnet user-secrets set "ConnectionStrings:Postgres" "<connection string>"`
  из каталога Web-проекта и альтернативу `ConnectionStrings__Postgres`; значение секрета
  не выводится.
- `DEPLOY.md` §1.1 «Локальная разработка» — одноразовые команды user secrets для строки
  подключения и `Auth:SigningKey`, env-альтернатива, запрет коммитить эти значения.

**Проверка:** Debug и Release — **0/0**; domain 184/184, application 212/212, infrastructure
69/69, integration 6/6 (на момент закрытия R-46). Реальный процесс без
`ConnectionStrings__Postgres` завершился с кодом `-532462766` и новым сообщением, без пароля
в выводе; с env-переменной `GET /health/live` вернул `200`. Интеграционные тесты, smoke и load
уже передают строку через env, поэтому их правки не потребовались.

### R-47. `AllowedHosts: "*"` — ✅ закрыто 2026-10-25

**Где:** `Web/appsettings.json` (`AllowedHosts`), `Web/Deployment/StartupSettings.cs`
(`Load` + `ValidateAllowedHosts`), `tests/BarberSearch.Infrastructure.UnitTests/StartupSettingsTests.cs`,
`tests/BarberSearch.IntegrationTests/HostFilteringTests.cs`, `deploy/smoke.ps1`, `DEPLOY.md` §1/§3.

**Почему:** wildcard `AllowedHosts` принимает любой `Host`, поэтому host-header атаки и подмена
хоста для cookie-scope не отсекаются на входе. В `Production` список хостов должен быть явным
и fail-closed, а не «любой».

**Сделано:**
- `appsettings.json`: `"AllowedHosts": "localhost;127.0.0.1"` — только локальная разработка;
  публичные имена задаются на деплое (`AllowedHosts=api.example.com`).
- `StartupSettings.Load`: после существующих проверок (connection string, Auth, CORS,
  forwarded headers, DataProtection, SameSite) и только в `Production` вызывается
  `ValidateAllowedHosts`: `configuration["AllowedHosts"]` делится по `;`
  (`TrimEntries | RemoveEmptyEntries`); пустой список и `*` → `InvalidOperationException`.
  Сообщение содержит `'*' is not allowed`, пример `AllowedHosts=api.example.com` и
  объяснение, что неизвестный `Host` получает `400`; секреты не выводятся.
  Development/Testing остаются permissive.
- Тесты: `StartupSettingsTests.Load_InProduction_RejectsWildcardAllowedHosts` (Production +
  `*`, без БД и запуска хоста, временный каталог key ring удаляется в `finally`);
  `HostFilteringTests.Unknown_host_is_rejected_with_400_while_localhost_is_served`
  (`GET /health/live` с `Host: localhost` → 200, с `Host: evil.example.test` → 400).
- `deploy/smoke.ps1`: `AllowedHosts` в `$managedEnvironmentKeys` и `New-BaseEnvironment`
  (`localhost;127.0.0.1;api.barbersearch.test` — production-инстанс ходит с
  `Host: api.barbersearch.test`); fail-fast `allowed-hosts-wildcard` в §9 (Production +
  `AllowedHosts=*` → `'*' is not allowed`).
- `DEPLOY.md`: `AllowedHosts` в таблице §1 (обязателен вне локальной разработки, имена через
  `;` без схемы/порта, дефолт `localhost;127.0.0.1`, `*`/пусто — падение старта) и заметка §3
  (прокси сохраняет исходный `Host`, неизвестный `Host` → 400).

**Проверка:** red-green интеграционного теста: до смены `appsettings.json` —
`Expected: BadRequest, Actual: OK`; после — 1/1 зелёный. Debug и Release — **0/0**;
domain 184/184, application 212/212, infrastructure 70/70, integration 7/7 (на момент
закрытия R-47). Полный smoke не запускался; правки в нём согласованы по коду, синтаксис
проверен парсером PowerShell.

### R-48. `logout` без rate limit — ✅ закрыто 2026-10-25

**Где:** `Web/Controllers/AuthController.cs` (`Logout`), `Web/Deployment/RateLimitSettings.cs`
(XML-доки политики `auth`).

**Почему:** `Logout` был `[AllowAnonymous]` и без `[EnableRateLimiting("auth")]`, тогда как
`Register`, `Login`, `LoginWithCookie`, `Refresh` и `ChangePassword` уже были ограничены
10 запросами в минуту на адрес клиента. Анонимность logout оправдана (работа с истёкшей
сессией и refresh-токеном в теле), но отсутствие лимита выбивало действие из общей политики
`AuthController` и оставляло дешёвый эндпоинт без квоты.

**Сделано:**
- `AuthController.Logout` получил `[EnableRateLimiting("auth")]` в порядке `[AllowAnonymous]`
  → `[EnableRateLimiting("auth")]` → `[HttpPost("logout")]`; `[AllowAnonymous]` сохранён,
  ветки (тело/Bearer/cookie), antiforgery и коды ответов не менялись.
- XML `<remarks>` действия фиксирует: logout анонимен намеренно (истёкшая сессия, токен
  в теле) и ограничен политикой `auth` — 10 запросов в минуту на адрес клиента, как
  остальные действия контроллера.
- XML `<remarks>` `RateLimitSettings` называет `logout` среди действий политики `auth`;
  лимиты, партиции и глобальный лимит записи 30/мин не менялись.
- Новый интеграционный тест `tests/BarberSearch.IntegrationTests/LogoutRateLimitTests.cs`:
  10 × `POST api/auth/logout` с непустым фиктивным refresh-токеном в теле → `204`,
  11-й → `429` с `Retry-After` (положительное целое), `application/problem+json` и
  `rateLimit.exceeded`.

**Проверка:** Debug и Release — **0/0**; domain 184/184, application 212/212, infrastructure
70/70, integration 8/8 (на момент закрытия R-48). Red-green: без атрибута 11-й запрос
возвращает `204` (`Expected: TooManyRequests`, `Actual: NoContent`); с атрибутом тест зелёный.

### R-49. Refresh-cookie с `Path=/` — ✅ закрыто 2026-10-25 (сознательно принято)

**Где:** `Web/Auth/RefreshTokenCookie.cs` (`CreateOptions`, `Path = "/"`);
`tests/BarberSearch.IntegrationTests/RefreshCookieTests.cs`; `DEPLOY.md` §4.

**Почему:** `Path` не является границей доверия: cookie уходит только на тот же origin,
который и так обслуживает `/api/auth/*`, а `HttpOnly` + `Secure` + `SameSite` не дают
прочитать её скриптам; сторонних CDN и логов, куда мог бы попасть токен, в проекте нет.
`Path=/` — обязательное требование префикса `__Host-`, поэтому сузить путь, сохранив
префикс, нельзя. Узкий путь потребовал бы `__Secure-` и потери запрета `Domain`: соседний
поддомен смог бы перекрыть refresh-cookie. Это сильнее экономии нескольких байт в заголовках
запросов, поэтому **сознательно принято**: имя, `Path=/` и флаги остаются как есть.

**Сделано:**
- XML-`<remarks>` `RefreshTokenCookie` фиксируют решение, принятую цену и условие пересмотра.
- `DEPLOY.md` §4 — тот же тезис для эксплуатации.
- Новый `RefreshCookieTests`: по сырому `Set-Cookie` ответа `POST api/auth/login/cookie`
  проверяются `__Host-barbersearch-refresh` с сегментами ровно `path=/`, `httponly`,
  `secure`, `samesite=lax`, без `domain=`, и access-cookie `__Host-barbersearch` с `path=/`.
  Путь сравнивается сегментом целиком: `Contains("path=/")` прошёл бы и на `path=/api/auth`.

**Пересмотреть**, если публичные пути переедут на отдельный origin/CDN или префикс
`__Host-` будет снят по другой причине: тогда `Path=/api/auth` (или отдельный cookie-хост)
вместе с миграцией имени cookie.

**Проверка:** Debug и Release — **0/0**; domain 184/184, application 212/212, infrastructure
70/70, integration 9/9 (на момент закрытия R-49). Red-green: с временным
`Path = "/api/auth"` тест падает `Cookie должна содержать атрибут 'path=/' целиком.
Set-Cookie: … path=/api/auth; secure; samesite=lax; httponly`; после возврата `Path = "/"` —
10/10.

### R-50. Истёкшие refresh-токены не удалялись — ✅ закрыто 2026-10-25

**Где:** `Application/RefreshTokens/IRefreshTokenRepository.cs`,
`Infrastructure.Postgresql/Repositories/RefreshTokenRepository.cs`,
`Infrastructure.Postgresql/Configurations/RefreshTokenConfiguration.cs`,
`Infrastructure.Postgresql/Migrations/20261024210659_AddRefreshTokenExpiresAtIndex.cs`,
`Web/Auth/RefreshTokenCleanupService.cs`, `Web/Deployment/StartupSettings.cs`,
`Web/Program.cs`, `Web/appsettings.json`,
`tests/BarberSearch.Infrastructure.UnitTests/RefreshTokenExpiresAtIndexModelTests.cs`,
`tests/BarberSearch.IntegrationTests/RefreshTokenCleanupTests.cs`.

**Почему:** ротация добавляет строку на каждый refresh, а удалять истёкшие было некому —
таблица росла линейно. Удалять «всё истёкшее» нельзя: `replaced_by` — self-FK с
`ON DELETE SET NULL`, и удаление родителя с живым потомком обнулило бы ссылку живой цепочки.
Leaf-only тоже не подходит: у активной цепочки лист ровно один — живой кончик, поэтому
уборка не удаляла бы ничего.

**Сделано:**
- `IRefreshTokenRepository.DeleteExpiredAsync(cutoff, ct)` — один `DELETE` строк с
  `ExpiresAt < cutoff`, возвращает число удалённых; XML-док фиксирует инвариант границы.
- `RefreshTokenRepository`: `ExecuteDeleteAsync` + `DetachTracked(token => token.ExpiresAt < cutoff)`
  (`ExecuteDelete` идёт мимо change tracker).
- Граница `cutoff = now - RefreshToken.MAX_LIFETIME_DAYS` (30 дней). Ротация выпускает замену
  раньше истечения родителя и токен живёт не дольше 30 дней, поэтому
  `expiresAt(child) < expiresAt(parent) + 30 дней`; у каждой удаляемой строки все потомки уже
  истекли, и `ON DELETE SET NULL` не обнуляет ссылку живого токена. Отозванные, но истёкшие
  меньше 30 дней назад строки остаются для reuse-detection.
- Индекс `ix_refresh_tokens_expires_at` на `expires_at` (неуникальный, один столбец) и миграция
  `AddRefreshTokenExpiresAtIndex` (Up `CreateIndex`, Down `DropIndex`).
- `RefreshTokenCleanupService` (`Web/Auth`, `BackgroundService`): прогон сразу на старте, затем
  `PeriodicTimer` каждые `RefreshTokens:CleanupIntervalMinutes` (60 по умолчанию, 1..1440,
  проверяется `StartupSettings.ReadInt`); `DbException`/`TimeoutException` логируются
  warning'ом и не роняют хост, следующий тик повторяет; `OperationCanceledException` при
  остановке — штатный выход; удаление > 0 логируется. `InitialCleanupCompleted` опубликован
  для тестов и диагностики.
- `FakeRefreshTokenRepository.DeleteExpiredAsync` удаляет строки с `ExpiresAt < cutoff`.
- Регистрация: `RefreshTokenCleanupOptions` singleton + `RefreshTokenCleanupService` singleton,
  хост получает тот же экземпляр через `AddHostedService(sp => sp.GetRequiredService<...>())`
  (иначе `AddHostedService<T>` регистрирует только `IHostedService`, и конкретный тип не
  резолвится; второй экземпляр не создаётся).
- `TestApp.StartAsync` ждёт `InitialCleanupCompleted` (до 30 с), поэтому стартовый `DELETE`
  не может забрать строки, засеянные тестом.

**Проверка:** Debug и Release — **0/0**; domain 184/184, application 212/212,
infrastructure 71/71 (+`RefreshTokenExpiresAtIndexModelTests`), integration 10/10
(+`RefreshTokenCleanupTests`: old + parent + child удалены, recent/live остались, повторный
прогон 0; полный интеграционный прогон повторён дважды — 10/10). `dotnet ef migrations
has-pending-model-changes` — «No changes have been made to the model since the last migration»;
миграция применена интеграционной фикстурой на свежей БД.

### R-51. `AGENT.md` раздут хронологией: разбиение отклонено, история вынесена — ✅ закрыто 2026-10-25

**Где:** `AGENT.md` (вводный блок, §3/§4, §5.2, §5.3, §8, §9, §13), `CODE-REVIEW.md`, `HANDOFF.md`.

**Почему:** файл вырос до **209 135 байт / 2442 строк**: §5.3 хранил ~300 строк прогонов по
срезам, §8 — 19 строк на ~21.7 КБ с пересказом кода и устаревшими 181/200, §9.1–§9.12
дублировали `HANDOFF.md`/`CODE-REVIEW.md`. Разбиение на несколько документов **сознательно
отклонено**: на `§N.M` ссылаются ~70 мест в коде и доках (`.cs`, `AGENT.md`, `DEPLOY.md`,
`LOADTEST.md`), `AGENT.md` авто-загружается агентом, и вынесенные §10/§13 рискованно сделали бы
правила «необязательными»; сам split не устраняет причину роста. Причина — хронология и
дублирование; их убрали, а политику зафиксировали в §13.

**Сделано:**
- вводный блок обновлён по факту: `features/issue-6`, 10 проектов, 184/212/71/10, текущий срез
  R-01…R-54 в рабочем дереве;
- добавлено оглавление со ссылками на §1–§17 (GitHub-анкоры; номера и заголовки не менялись);
- §5.3: удалены исторические прогоны R-03…R-35 (~300 строк) и негативные абзацы; оставлен
  baseline 2026-10-25 (184/212/71/10) со ссылкой на `HANDOFF.md` §2, рецепт fallback-раннера
  сохранён;
- §8 переписан в компактную таблицу «слой → статус»: 10 проектов, Debug/Release 0/0, тесты,
  13 контроллеров / 81 endpoint (73 доменных + 8 auth), миграции/16 таблиц/ограничения,
  domain events по-прежнему открыты (§12.6); детальная инвентаризация — `HANDOFF.md` §0/§2,
  `LOADTEST.md`, `DEPLOY.md`;
- §9.1–§9.12 свёрнуты в абзац; §9.3 и §9.7 сохранены, потому что на них ссылается §15.2;
  из §9.13 убран неверный пункт про `/weatherforecast` (в `.http` его нет; legacy GUID оставлен
  для R-54);
- §13 — политика документации: `AGENT.md` — долговечные правила и справка, не changelog;
  прогоны и блоки «Сделано» — в `HANDOFF.md`/`CODE-REVIEW.md`; номера § стабильны; XML-доки
  описывают контракт и причину, а не сигнатуру; split — отдельный срез с обновлением всех ссылок;
- в §5.2 «9 projects» → 10, `-q` заменён на `--verbosity quiet`; попутно исправлены устаревшие
  «9 projects»/«three test projects» в §3/§4 и добавлен IntegrationTests в дерево.
- XML-доки исходников не переписывались: их требует §10, это отдельный срез; правило
  зафиксировано в §13.

**Проверка:** Debug и Release — **0/0**; domain 184/184, application 212/212, infrastructure
71/71, integration 10/10 (локальная PostgreSQL, `BARBERSEARCH_TEST_POSTGRES`). Размер:
**209 135 → 170 583 байт** (204.2 → 166.6 КБ; 2442 → 2107 строк, −18.4%). Скрипт сверки:
63 → 53 заголовка `## N.`/`### N.M`; отсутствуют ровно удалённые подпункты §9 (9.1, 9.2,
9.4–9.6, 9.8–9.12), §9.3/§9.7 и остальные на месте; 17 ссылок оглавления разрешаются. Из 52
ссылок `AGENT.md §N.M` 47 указывают на заголовки, 5 — на нумерованные пункты §10/§12
(`§10.7`, `§12.2`), которые заголовками не являются (существующая конвенция, не менялась).

### R-52. `GET api/reviews` без `barberProfileId`: доменная 400 вместо binding — ✅ закрыто 2026-10-25

**Где:** `Web/Controllers/ReviewsController.cs`,
`Application/Reviews/GetReviews/GetReviewsHandler.cs`,
`Application/Reviews/GetReviews/GetReviewsQuery.cs`,
`tests/BarberSearch.IntegrationTests/ReviewsBindingTests.cs`, `BarberSearch.Web.http`.

**Почему:** `[FromQuery] Guid barberProfileId` не был обязательным, поэтому отсутствие параметра
связывалось в `Guid.Empty` и доходило до `GetReviewsHandler`, который отвечал 400 с доменным
кодом `review.getReviews.barberProfileId`. Клиент не отличал «параметр не передан» от «передан
пустой», а нарушение контракта выглядело доменной ошибкой. `[ApiController]` уже превращает
невалидный `ModelState` в `400 ValidationProblemDetails`, поэтому достаточно пометить параметр
`[BindRequired]`: запрос отсекается до обработчика, отдельная проверка в контроллере не нужна.

**Сделано:**
- `ReviewsController.Get`: `[FromQuery, BindRequired] Guid barberProfileId` и
  `using Microsoft.AspNetCore.Mvc.ModelBinding;`. Отсутствие параметра → 400
  `ValidationProblemDetails` с ошибкой на поле `barberProfileId`. Сигнатура, маршрут, остальные
  действия и коды не менялись. XML-док фиксирует: параметр обязателен, отсутствие — binding,
  явный пустой GUID — доменная 400.
- Проверка `Guid.Empty` в `GetReviewsHandler`/`GetReviewsQuery` сохранена как defense-in-depth:
  `GET api/reviews?barberProfileId=00000000-0000-0000-0000-000000000000` по-прежнему отвечает
  доменным `review.getReviews.barberProfileId`; XML-доки уточнены, поведение не менялось.
- `ReviewsBindingTests` — один интеграционный сценарий на реальном хосте (новая коллекция
  `PostgresFixture`, `TestApp.StartAsync` чистит БД) с тремя запросами: без параметра (400,
  есть `errors.barberProfileId`, нет `review.getReviews`), пустой GUID (400 + доменный код),
  несуществующий непустой профиль (200, пустая страница, `nextCursor: null`). Разбор —
  `System.Text.Json.JsonDocument`, новых пакетов нет.
- `BarberSearch.Web.http`: пример `GET {{host}}/api/reviews?barberProfileId={{barberProfileId}}`
  рядом с очередью модерации.

**Проверка:** red-green. Без `[BindRequired]` тест падает на первом запросе — тело:

```json
{"title":"One or more validation errors occurred.","status":400,"detail":"Barber profile identifier cannot be empty.","errors":{"BarberProfileId":["Barber profile identifier cannot be empty."]},"errorCodes":["review.getReviews.barberProfileId"]}
```

— доменный ответ, ключа `errors.barberProfileId` нет. С атрибутом — `1/1` зелёный.
Debug и Release — **0/0**; domain 184/184, application 212/212, infrastructure 71/71,
integration **11/11** (10 прежних + `ReviewsBindingTests`).

### R-53. `UseRateLimiter` после auth: перенос отклонён — ✅ закрыто 2026-10-25

**Где:** `Web/Program.cs`, `Web/Deployment/RateLimitSettings.cs`,
`tests/BarberSearch.IntegrationTests/RateLimitOrderTests.cs`, `AGENT.md` §17.5.

**Почему:** предложение перенести лимитер выше `UseAuthentication`, чтобы не аутентифицировать
отбрасываемые запросы, несовместимо с партицией записи. `RateLimitSettings.WritePartitionKey`
берёт claim `sub` из `HttpContext.User` и только без него падает на IP; до `UseAuthentication`
пользователя ещё нет, поэтому лимит 30/мин на пользователя (R-22) стал бы общим на адрес —
два клиента за одним NAT отбирали бы квоту друг у друга, а смена IP обходила бы лимит.
Цена — аутентификация запросов, которые затем получают `429`, — сознательно принята;
альтернативы (двухступенчатый лимитер до/после auth) усложнили бы конвейер без выигрыша.

**Сделано:**
- `Program.cs`: порядок `UseAuthentication` → `UseAuthorization` → `UseRateLimiter` не менялся;
  добавлен комментарий, почему лимитер обязан идти после auth (ключ `sub`, R-22, R-53).
- `RateLimitSettings`: XML-`<remarks>` фиксирует зависимость от порядка middleware и ссылается
  на тест; поведение, константы и партиции не менялись.
- `RateLimitOrderTests` — интеграционный сценарий на реальном конвейере: два пользователя
  регистрируются и входят без `Authorization` (4 записи в анонимную партицию), затем A
  отправляет ровно 30 `POST api/bookings` с пустым JSON-телом (каждый — `400` от binding, но
  permit расходуется), 31-й получает `429 rateLimit.exceeded`; запрос B после этого — `400`,
  потому что у него своя партиция `sub`.

**Проверка:** red-green. С `app.UseRateLimiter()` выше `UseAuthentication`/`UseAuthorization`
тест падает на 27-м запросе A из 30: `получено 429` (общая анонимная партиция уже израсходована
регистрацией и входом); с исходным порядком — `1/1` зелёный. Debug и Release — **0/0**;
domain 184/184, application 212/212, infrastructure 71/71, integration **12/12**
(+`RateLimitOrderTests`).

### R-54. `.sln` с legacy project type GUID; `.http` уже без `/weatherforecast` — ✅ закрыто 2026-10-25

**Где:** `BarberSearch/BarberSearch.sln`, `BarberSearch/src/BarberSearch.Web/BarberSearch.Web.http`,
`AGENT.md` §5.1/§9.13, `HANDOFF.md`.

**Почему:** все **10** project-записей решения объявляли legacy C# project type GUID
`{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}` (формат старого `.csproj`); SDK-style проекты
объявляются как `{9A19103F-16F7-4668-BE54-9A1E7A4F7556}`, и старая метка — единственное, что
отличало файл от современного вида. Вторая половина пункта («`.http` с `/weatherforecast`»)
к моменту среза **уже неверна**: в `BarberSearch.Web.http` нет ни одного вхождения
`/weatherforecast` (grep — 0), а R-52 дописал туда пример `GET api/reviews`. Поэтому `.http`
не менялся, и это зафиксировано, а не «исправлено». Одиннадцатая запись решения — solution
folder `tests` с типом `{2150E333-…}` — не project-запись и не трогалась.

**Сделано:**
- `.sln`: ровно **10** вхождений `{FAE04EC0-…}` заменены на `{9A19103F-…}` — по одному на каждую
  project-запись (`Web`, `Domain`, `Shared`, `Domain.UnitTests`, `Infrastructure.Postgresql`,
  `Application`, `Contracts`, `Application.UnitTests`, `Infrastructure.UnitTests`,
  `IntegrationTests`). Project GUID'ы, пути, `SolutionConfigurationPlatforms`,
  `ProjectConfigurationPlatforms`, `NestedProjects`, solution folder и порядок строк не менялись.
  Файл остался в той же кодировке: ASCII + CRLF, без BOM (проверено побайтно; BOM в нём
  не было и до правки), поэтому diff — только 10 строк.
- `.http` не менялся: `/weatherforecast` в нём нет, пример `GET api/reviews` уже добавлен R-52.
- `AGENT.md` §5.1: абзац «still uses the legacy project type GUID … separate cleanup (§9.13)»
  заменён фактом — с 2026-10-25 (R-54) все десять project-записей используют SDK-style
  `{9A19103F-…}`, `dotnet sln list` по-прежнему печатает 10 проектов, Debug/Release — 0/0.
  Предупреждение «не добавлять проекты через `dotnet sln add`» и ручные GUID'ы проектов
  сохранены. §9.13: пункт про legacy GUID удалён, остальные открытые мелочи на месте.

**Проверка:** diff `.sln` относительно копии до правки — `git diff --no-index --numstat`:
**10 добавлений / 10 удалений**, а не весь файл (78 строк, 6 197 байт до и после). Относительно
HEAD тот же файл даёт 22/8, потому что 14 добавлений/0 удалений в нём — незакоммиченные
`Infrastructure.UnitTests` и `IntegrationTests` из прошлых срезов; чистых 10/10 от R-54 это
не меняет. `dotnet sln list` — те же 10 проектов (4 тестовых + 6 `src`). Debug и Release —
**0/0**; domain 184/184, application 212/212, infrastructure 71/71, integration 12/12.
Отдельно: `grep /weatherforecast` по `.http` — 0 вхождений, файл не изменён.

Открытых пунктов P3 нет: R-46–R-54 закрыты 2026-10-25.

---

## 6. Что сделано хорошо (не ломать при исправлениях)

1. **Ошибки домена не бросаются исключениями**, маппинг `ErrorType` → HTTP сосредоточен в
   `FailureProblemDetails` — единая точка, легко расширять.
2. **Exclusion-ограничение `ex_bookings_no_overlap`** с полуоткрытым `tstzrange` и частичным
   предикатом `status IN (1,2)` — корректная модель занятости.
3. **Keyset-пагинация** с непрозрачными курсорами и валидацией (`CursorCodec`,
   `BarberSearchCursor`) — редко встречается в проектах такого размера.
4. **Expression-индекс `ix_barber_profiles_search_keyset`** терм в терм под `ORDER BY` — по
   `LOADTEST.md` дал 5-кратное ускорение.
5. **Auth-контур**: cookie + JWT через `PolicyScheme`, security stamp, ротация ключей с `kid`,
   antiforgery только для cookie-ветки, `__Host-`, явный CORS, доверенные прокси — продумано.
6. **Startup validation** (`StartupSettings`, `AuthOptions.FromConfiguration`,
   `DataProtectionSettings`) — падение на старте, а не на первом запросе.
7. **`AsNoTracking` + проекции в DTO** на всех read-путях; доменные сущности не сериализуются.
8. **Узкие чтения агрегата профиля** (R-12): корень или корень + одна коллекция — один SQL;
   `AsSplitQuery` нужен только там, где в одном запросе действительно несколько коллекций.

---

## 7. Предлагаемые срезы для следующих сессий

Порядок учитывает зависимости: сначала то, что меняет схему/контракты, потом то, что строится
на них.

| Срез | Содержимое | Оценка |
|---|---|---|
| **S1. Целостность и валидация ввода** | R-01, R-02, R-03, R-10, R-23, R-24 — **закрыты** (миграции `citext`/`lower(email)`, `HasMaxLength`, пять FK) | M |
| **S2. Бронь: правила и контракт** | R-04, R-09, R-20, R-06 (deferrable constraint), R-33 (`price` убран из контракта брони) — **закрыты** | M |
| **S3. Согласованность записи** | R-05 (`xmin`), R-07 (условный UPDATE), R-08, R-15, R-16, R-17, R-30 — **закрыты** | M/L |
| **S4. Производительность** | R-11 (закрыт: узкий stamp без кэша), R-12 (закрыт: узкие чтения), R-13 (закрыт: EF `Warning`), R-14 (закрыт: сжатие JSON/текста, таймаут 30 с), R-18 (закрыт: `lock_timeout` 2 с, `55P03` → 409, `CommandTimeout` 30 с), R-19 (закрыт: `COUNT`/`AVG` одним запросом) | M |
| **S5. Доступ и эксплуатация** | R-21, R-22, R-25, R-46, R-47 — **закрыты** | S/M |
| **S6. Наблюдаемость и тесты** | R-26, R-34, R-36 — **закрыты** | M |
| **S7. Гигиена** | R-37–R-54 — **закрыты** (открытых нет) | S |

Критерий готовности к прод-запуску: **S1, S2 и S3 закрыты** (R-33 закрыт 2026-10-24), S4 — минимум
R-12/R-13/R-14 (R-11, R-12, R-13 и R-14 закрыты), S5 — R-46/R-47 (закрыты 2026-10-25).

---

## 8. Как ссылаться на этот документ

В промпте следующей сессии достаточно указать: «исправь R-08, R-19 из `backend/CODE-REVIEW.md`».
Номера стабильны; при закрытии пункта — вычеркнуть его здесь и дописать строку в `HANDOFF.md`
(в стиле «Закрыто 2026-10-21: …»), чтобы не потерять историю решений.
