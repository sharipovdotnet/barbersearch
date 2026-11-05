-- E2E-фикстуры для Playwright. ТОЛЬКО тестовая база `barbersearch_e2e`.
--
-- Скрипт идемпотентен и по своей природе является «сбросом»: строки пользователей
-- `*@barbersearch.test` (отзывы, записи) удаляются и вставляются заново, поэтому
-- повторный запуск возвращает базу в известное состояние после мутирующих спек.
-- Чужие данные (barbersearch, barbersearch_load) не трогаются: гвард ниже.
--
-- Пароль всех аккаунтов — `e2e-password` (тестовый, не секрет). Хеш IdentityV3
-- получен один раз через `POST /api/auth/register` и скопирован из `users.password_hash`;
-- соль встроена в строку, поэтому один хеш годится для всех фикстур.
--
-- Запуск (из `frontend/`):
--   npm run test:e2e:seed
-- или напрямую:
--   psql -h localhost -U postgres -d barbersearch_e2e -v ON_ERROR_STOP=1 -f e2e/fixtures/seed.sql
--
-- Что даёт разметка для тестов:
--   B1 (9e100000-…-0001)        12 опубликованных отзывов (2 страницы по 10), 76 записей
--                               в истории (52 прежних + 24 админских на 2026-09-12),
--                               21 предстоящая запись (вторая страница — одна строка)
--   e2e-customer                12 своих отзывов (вторая страница — 2 строки),
--                               21 предстоящая запись и 21 запись истории — две вкладки
--                               «/account» по две страницы (20 + одна строка)
--   e2e-mutations               12 своих отзывов (вторая страница — 2 строки) + 2 свободные
--                               записи под восстановление после удаления отзыва
--   B2…B13                      12 барберов, по 2 опубликованных отзыва; 13 профилей в «Москве»
--   B14 (9e100000-…-0014)        профиль БЕЗ подтверждения, без услуг и салона: единственная
--                               строка блока «Барберы без подтверждения» в `/admin/summary`
--   2 отзыва на модерации       вкладка «На модерации» админки
--   26 пользователей            две страницы админки: «1–20» и вторая; в CI к ним
--                               добавляется `ci-owner` из `backend/load/seed/minimal.sql`
--   25 строк audit_log           журнал `/admin/audit` на 2026-09-10 UTC:
--                               страница «1–20», вторая «21–25», фильтры по дате,
--                               типу и автору (+ salon/hair_service/specialization/booking)
--   24 брони e2e-bookings       `/admin/bookings` на 2026-09-12 UTC: две страницы
--                               (20 + 4) и все пять статусов

\set ON_ERROR_STOP on
SET TIME ZONE 'UTC';

DO $$
BEGIN
  IF current_database() <> 'barbersearch_e2e' THEN
    RAISE EXCEPTION
      'refusing to seed database "%": only barbersearch_e2e is allowed (see README, E2E)',
      current_database();
  END IF;
END
$$;

BEGIN;

-- 1. Сброс строк, которыми владеют фикстуры. Порядок — от зависимых к родителям.
DELETE FROM reviews
WHERE user_id IN (SELECT id FROM users WHERE email LIKE '%@barbersearch.test');

DELETE FROM bookings
WHERE user_id IN (SELECT id FROM users WHERE email LIKE '%@barbersearch.test');

DELETE FROM refresh_tokens
WHERE user_id IN (SELECT id FROM users WHERE email LIKE '%@barbersearch.test');

-- 2. Пользователи. Пароль `e2e-password` (IdentityV3).
INSERT INTO users
    (id, email, password_hash, first_name, last_name, phone_number, avatar_url,
     is_email_verified, is_phone_verified, role, is_active, last_login_at,
     barber_profile_id, created_at, updated_at, security_stamp, failed_login_attempts)
SELECT
    fixture.id::uuid,
    fixture.email,
    'AQAAAAIAAYagAAAAEPI2KBIkNonT1XQgxFg9pgsJsMX97SCCS0mO/jLxqGuhFjZwh4sumyi/WPkW6vG6Ew==',
    fixture.first_name,
    fixture.last_name,
    fixture.phone_number,
    NULL,
    fixture.email_verified,
    false,
    fixture.role,
    fixture.is_active,
    NULL,
    NULL,
    fixture.created_at::timestamptz,
    fixture.created_at::timestamptz,
    'e2e-stamp-' || fixture.id,
    0
FROM (
    VALUES
        ('9e000000-0000-4000-8000-000000000001', 'e2e-admin@barbersearch.test',
         'Арина', 'Админова', '+70000000001', true, true, 3, true,
         '2026-09-01 10:00+00'),
        ('9e000000-0000-4000-8000-000000000002', 'e2e-barber@barbersearch.test',
         'Тимур', 'Барберов', '+70000000002', true, true, 2, true,
         '2026-09-01 10:01+00'),
        ('9e000000-0000-4000-8000-000000000003', 'e2e-customer@barbersearch.test',
         'Ева', 'Клиентова', '+70000000003', true, true, 1, true,
         '2026-09-01 10:02+00'),
        ('9e000000-0000-4000-8000-000000000004', 'e2e-mutations@barbersearch.test',
         'Марк', 'Правкин', '+70000000004', true, true, 1, true,
         '2026-09-01 10:03+00'),
        -- Владелец единственного профиля БЕЗ подтверждения (секция 4.1). Профиль
        -- не попадает в публичную выдачу, поэтому остальные спеки он не двигает.
        ('9e000000-0000-4000-8000-000000000113', 'e2e-pending-barber@barbersearch.test',
         'Егор', 'Неподтверждённый', NULL, true, true, 2, true,
         '2026-09-01 10:04+00')
) AS fixture(id, email, first_name, last_name, phone_number, email_verified, is_active,
             role, _unused, created_at)
ON CONFLICT (id) DO UPDATE SET
    email = EXCLUDED.email,
    password_hash = EXCLUDED.password_hash,
    first_name = EXCLUDED.first_name,
    last_name = EXCLUDED.last_name,
    phone_number = EXCLUDED.phone_number,
    avatar_url = EXCLUDED.avatar_url,
    is_active = EXCLUDED.is_active,
    is_email_verified = EXCLUDED.is_email_verified,
    role = EXCLUDED.role,
    failed_login_attempts = 0,
    lockout_end = NULL,
    updated_at = now();

-- 2.1 Барбер без профиля для онбординга (#45).
--
-- Роль `Barber` выдана, `barber_profile_id` — NULL: таким аккаунт видит кабинет
-- только через `BarberProfileMissingGate`, а профиль создаёт сценарий
-- `barber-onboarding-mutations.spec.ts`.
--
-- Идемпотентность: созданный прошлым прогоном профиль удаляется первым. Порядок
-- обязателен — `users.barber_profile_id` ссылается на `barber_profiles`
-- (ON DELETE RESTRICT), поэтому сначала обнуляем ссылку, потом удаляем профиль.
-- Без этого второй прогон без пересева падал бы на `409
-- barberProfile.create.alreadyExists`. Счётчики админки этот аккаунт не двигает:
-- без профиля он не попадает ни в очередь барберов, ни в `usageCount`.
UPDATE users
SET barber_profile_id = NULL, updated_at = now()
WHERE id = '9e000000-0000-4000-8000-000000000005';

DELETE FROM barber_profiles
WHERE user_id = '9e000000-0000-4000-8000-000000000005';

INSERT INTO users
    (id, email, password_hash, first_name, last_name, phone_number, avatar_url,
     is_email_verified, is_phone_verified, role, is_active, last_login_at,
     barber_profile_id, created_at, updated_at, security_stamp, failed_login_attempts)
VALUES
    ('9e000000-0000-4000-8000-000000000005', 'e2e-new-barber@barbersearch.test',
     'AQAAAAIAAYagAAAAEPI2KBIkNonT1XQgxFg9pgsJsMX97SCCS0mO/jLxqGuhFjZwh4sumyi/WPkW6vG6Ew==',
     'Илья', 'Новиков', '+70000000005', NULL,
     true, false, 2, true, NULL,
     NULL,
     timestamptz '2026-09-01 10:05+00', timestamptz '2026-09-01 10:05+00',
     'e2e-stamp-new-barber', 0)
ON CONFLICT (id) DO UPDATE SET
    email = EXCLUDED.email,
    password_hash = EXCLUDED.password_hash,
    first_name = EXCLUDED.first_name,
    last_name = EXCLUDED.last_name,
    phone_number = EXCLUDED.phone_number,
    is_active = EXCLUDED.is_active,
    is_email_verified = EXCLUDED.is_email_verified,
    role = EXCLUDED.role,
    barber_profile_id = NULL,
    failed_login_attempts = 0,
    lockout_end = NULL,
    updated_at = now();

-- 12 дополнительных барберов: из них 13 профилей в одном городе — этого хватает,
-- чтобы у поиска появился nextCursor и «Показать ещё».
INSERT INTO users
    (id, email, password_hash, first_name, last_name, phone_number, avatar_url,
     is_email_verified, is_phone_verified, role, is_active, last_login_at,
     barber_profile_id, created_at, updated_at, security_stamp, failed_login_attempts)
SELECT
    ('9e000000-0000-4000-8000-' || lpad((100 + n)::text, 12, '0'))::uuid,
    'e2e-barber-' || n || '@barbersearch.test',
    'AQAAAAIAAYagAAAAEPI2KBIkNonT1XQgxFg9pgsJsMX97SCCS0mO/jLxqGuhFjZwh4sumyi/WPkW6vG6Ew==',
    'Барбер',
    'Номер ' || n,
    NULL,
    NULL,
    true,
    false,
    2,
    true,
    NULL,
    NULL,
    timestamptz '2026-09-01 11:00+00' + (n || ' minutes')::interval,
    timestamptz '2026-09-01 11:00+00' + (n || ' minutes')::interval,
    'e2e-stamp-barber-' || n,
    0
FROM generate_series(1, 12) AS n
ON CONFLICT (id) DO UPDATE SET
    password_hash = EXCLUDED.password_hash,
    is_active = EXCLUDED.is_active,
    updated_at = now();

-- 8 клиентов-фикстур: страница пользователей админки получает вторую страницу,
-- а фильтры статуса — непустые наборы (2 заблокированных, 2 без подтверждения email).
INSERT INTO users
    (id, email, password_hash, first_name, last_name, phone_number, avatar_url,
     is_email_verified, is_phone_verified, role, is_active, last_login_at,
     barber_profile_id, created_at, updated_at, security_stamp, failed_login_attempts)
SELECT
    ('9e000000-0000-4000-8000-' || lpad((200 + n)::text, 12, '0'))::uuid,
    'e2e-customer-' || n || '@barbersearch.test',
    'AQAAAAIAAYagAAAAEPI2KBIkNonT1XQgxFg9pgsJsMX97SCCS0mO/jLxqGuhFjZwh4sumyi/WPkW6vG6Ew==',
    'Клиент',
    'Номер ' || n,
    CASE WHEN n <= 3 THEN '+70000001' || lpad(n::text, 3, '0') ELSE NULL END,
    NULL,
    NOT (n IN (2, 3)),
    false,
    1,
    NOT (n IN (5, 6)),
    NULL,
    NULL,
    timestamptz '2026-09-02 10:00+00' + (n || ' minutes')::interval,
    timestamptz '2026-09-02 10:00+00' + (n || ' minutes')::interval,
    'e2e-stamp-customer-' || n,
    0
FROM generate_series(1, 8) AS n
ON CONFLICT (id) DO UPDATE SET
    password_hash = EXCLUDED.password_hash,
    is_active = EXCLUDED.is_active,
    is_email_verified = EXCLUDED.is_email_verified,
    updated_at = now();

-- 3. Салон и услуга фикстур. Свой салон, а не из `load/seed/minimal.sql`: селектор
-- города в тестах не должен зависеть от CI-фикстур бэкенда.
INSERT INTO salons
    (id, name, address, city, postal_code, country, time_zone_id,
     latitude, longitude, phone_number, website_url, is_active, created_at, updated_at)
VALUES
    ('9e400000-0000-4000-8000-000000000001', 'E2E Studio', 'ул. Тестовая, 1', 'Москва',
     '101000', 'Russia', 'Europe/Moscow', 55.7558, 37.6173, '+70000000009', NULL, true, now(), now())
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    city = EXCLUDED.city,
    is_active = EXCLUDED.is_active,
    updated_at = now();

INSERT INTO hair_services
    (id, name, description, category, default_duration_minutes,
     is_active, created_at, updated_at)
VALUES
    ('9e500000-0000-4000-8000-000000000001', 'E2E Стрижка', 'Услуга E2E-фикстур', 1, 30,
     true, now(), now())
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    is_active = EXCLUDED.is_active,
    updated_at = now();

-- Выключенные строки справочников для админского каталога (`/admin/catalogue`).
--
-- Только выключенные: активная услуга, специализация или салон попадают в публичные
-- фильтры витрины и на карточки барберов, и чужие спеки (`search-login`, `a11y`,
-- карточки барберов) поехали бы по счётчикам. Выключенные строки публичные чтения
-- не отдают, поэтому фикстуры видны только админскому каталогу.
--
-- Опираются на них: `admin-catalogue.spec.ts` (вкладки, фильтр статуса, поиск,
-- `usageCount` = 0 у выключенных строк) и `admin-catalogue-mutations.spec.ts`
-- (выключение/включение, переименование, правка контактов).
INSERT INTO hair_services
    (id, name, description, category, default_duration_minutes,
     is_active, created_at, updated_at)
VALUES
    ('9e500000-0000-4000-8000-000000000002', 'E2E Борода',
     'Выключенная услуга E2E-фикстур', 2, 45, false, now(), now())
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    category = EXCLUDED.category,
    default_duration_minutes = EXCLUDED.default_duration_minutes,
    is_active = EXCLUDED.is_active,
    updated_at = now();

-- Специализации: публичный справочник фикстур пуст, поэтому здесь только выключенные
-- строки — на витрине их нет, а админский каталог видит.
INSERT INTO specializations
    (id, name, description, is_active, created_at, updated_at)
VALUES
    ('9e600000-0000-4000-8000-000000000001', 'E2E Фейд',
     'Выключенная специализация E2E-фикстур', false, now(), now()),
    ('9e600000-0000-4000-8000-000000000002', 'E2E Борода',
     'Выключенная специализация E2E-фикстур', false, now(), now())
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    is_active = EXCLUDED.is_active,
    updated_at = now();

-- Выключенный салон: публичный поиск и карточки его не показывают, админский каталог
-- видит и позволяет включить обратно. Правка контактов в mutations-спеке возвращает
-- телефон в `finally`, а повторный сид восстанавливает все поля строки.
INSERT INTO salons
    (id, name, address, city, postal_code, country, time_zone_id,
     latitude, longitude, phone_number, website_url, is_active, created_at, updated_at)
VALUES
    ('9e400000-0000-4000-8000-000000000002', 'E2E Studio Nord', 'ул. Северная, 2',
     'Санкт-Петербург', '190000', 'Russia', 'Europe/Moscow', 59.9386, 30.3141,
     '+78120000000', NULL, false, now(), now())
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    address = EXCLUDED.address,
    city = EXCLUDED.city,
    postal_code = EXCLUDED.postal_code,
    country = EXCLUDED.country,
    time_zone_id = EXCLUDED.time_zone_id,
    latitude = EXCLUDED.latitude,
    longitude = EXCLUDED.longitude,
    phone_number = EXCLUDED.phone_number,
    website_url = EXCLUDED.website_url,
    is_active = EXCLUDED.is_active,
    updated_at = now();

-- 4. Профили барберов: витринный B1 и B2…B13 для пагинации поиска.
INSERT INTO barber_profiles
    (id, user_id, display_name, grade, bio, languages, years_of_experience,
     certificates_and_training, rating, reviews_count, achievements,
     payment_methods, communication_format, goodies, late_policy,
     is_active, is_verified, created_at, updated_at)
SELECT
    profile.id::uuid,
    profile.user_id::uuid,
    profile.display_name,
    5,
    'E2E-профиль ' || profile.display_name,
    ARRAY[1, 2],
    8,
    ARRAY['E2E certificate'],
    profile.rating,
    profile.reviews_count,
    ARRAY[]::text[],
    ARRAY[1, 2],
    ARRAY['Friendly'],
    ARRAY['Coffee'],
    '10 minutes',
    true,
    true,
    now(),
    now()
FROM (
    VALUES
        ('9e100000-0000-4000-8000-000000000001', '9e000000-0000-4000-8000-000000000002',
         'Тимур Барберов', 4.50::numeric, 12),
        ('9e100000-0000-4000-8000-000000000002', '9e000000-0000-4000-8000-000000000101',
         'Барбер Номер 1', 5.00::numeric, 1),
        ('9e100000-0000-4000-8000-000000000003', '9e000000-0000-4000-8000-000000000102',
         'Барбер Номер 2', 5.00::numeric, 1),
        ('9e100000-0000-4000-8000-000000000004', '9e000000-0000-4000-8000-000000000103',
         'Барбер Номер 3', 5.00::numeric, 1),
        ('9e100000-0000-4000-8000-000000000005', '9e000000-0000-4000-8000-000000000104',
         'Барбер Номер 4', 5.00::numeric, 1),
        ('9e100000-0000-4000-8000-000000000006', '9e000000-0000-4000-8000-000000000105',
         'Барбер Номер 5', 5.00::numeric, 1),
        ('9e100000-0000-4000-8000-000000000007', '9e000000-0000-4000-8000-000000000106',
         'Барбер Номер 6', 5.00::numeric, 1),
        ('9e100000-0000-4000-8000-000000000008', '9e000000-0000-4000-8000-000000000107',
         'Барбер Номер 7', 5.00::numeric, 1),
        ('9e100000-0000-4000-8000-000000000009', '9e000000-0000-4000-8000-000000000108',
         'Барбер Номер 8', 5.00::numeric, 1),
        ('9e100000-0000-4000-8000-000000000010', '9e000000-0000-4000-8000-000000000109',
         'Барбер Номер 9', 5.00::numeric, 1),
        ('9e100000-0000-4000-8000-000000000011', '9e000000-0000-4000-8000-000000000110',
         'Барбер Номер 10', 5.00::numeric, 1),
        ('9e100000-0000-4000-8000-000000000012', '9e000000-0000-4000-8000-000000000111',
         'Барбер Номер 11', 5.00::numeric, 1),
        ('9e100000-0000-4000-8000-000000000013', '9e000000-0000-4000-8000-000000000112',
         'Барбер Номер 12', 5.00::numeric, 1)
) AS profile(id, user_id, display_name, rating, reviews_count)
ON CONFLICT (id) DO UPDATE SET
    display_name = EXCLUDED.display_name,
    rating = EXCLUDED.rating,
    reviews_count = EXCLUDED.reviews_count,
    is_active = EXCLUDED.is_active,
    is_verified = EXCLUDED.is_verified,
    updated_at = now();

-- 4.1 Профиль без подтверждения — единственная положительная строка блока
-- «Барберы без подтверждения» (`/admin/summary`) и очереди «На проверке».
--
-- Зачем нужен: до этого этапа все профили фикстур были подтверждены, и число
-- `unverifiedCount` всегда равнялось нулю — положительный путь сводки проверить
-- было нечем.
-- Почему без услуг и салона: `is_verified = false` и так не пускает профиль
-- в публичную выдачу, а без `barber_services` он не попадает и в `usageCount`
-- админского каталога. Ни одна чужая спека не двигается.
-- Почему id и даты фиксированы: сводка и очередь «На проверке» показывают
-- профиль по имени, а FIFO-порядок очереди не должен зависеть от дня прогона.
INSERT INTO barber_profiles
    (id, user_id, display_name, grade, bio, languages, years_of_experience,
     certificates_and_training, rating, reviews_count, achievements,
     payment_methods, communication_format, goodies, late_policy,
     is_active, is_verified, created_at, updated_at)
VALUES
    ('9e100000-0000-4000-8000-000000000014',
     '9e000000-0000-4000-8000-000000000113',
     'E2E Барбер Без Подтверждения',
     5,
     NULL,
     ARRAY[1],
     3,
     ARRAY[]::text[],
     NULL,
     0,
     ARRAY[]::text[],
     ARRAY[]::integer[],
     ARRAY[]::text[],
     ARRAY[]::text[],
     '10 minutes',
     true,
     false,
     '2026-08-01 10:00+00',
     '2026-08-01 10:00+00')
ON CONFLICT (id) DO UPDATE SET
    user_id = EXCLUDED.user_id,
    display_name = EXCLUDED.display_name,
    is_active = EXCLUDED.is_active,
    is_verified = EXCLUDED.is_verified,
    rating = EXCLUDED.rating,
    reviews_count = EXCLUDED.reviews_count,
    created_at = EXCLUDED.created_at,
    updated_at = EXCLUDED.updated_at;

-- Салон у каждого профиля (карточка поиска показывает «салон · город»).
INSERT INTO barber_salons
    (id, barber_profile_id, salon_id, is_primary, created_at, updated_at)
SELECT
    ('9e200000-0000-4000-8000-' || lpad(n::text, 12, '0'))::uuid,
    ('9e100000-0000-4000-8000-' || lpad(n::text, 12, '0'))::uuid,
    '9e400000-0000-4000-8000-000000000001'::uuid,
    true,
    now(),
    now()
FROM generate_series(1, 13) AS n
ON CONFLICT (id) DO UPDATE SET
    is_primary = EXCLUDED.is_primary,
    updated_at = now();

-- Услуга у каждого профиля: она нужна записям, а те — «Истории» кабинета.
INSERT INTO barber_services
    (id, barber_profile_id, hair_service_id, price, duration_minutes,
     is_active, created_at, updated_at)
SELECT
    ('9e300000-0000-4000-8000-' || lpad(n::text, 12, '0'))::uuid,
    ('9e100000-0000-4000-8000-' || lpad(n::text, 12, '0'))::uuid,
    '9e500000-0000-4000-8000-000000000001'::uuid,
    30.00,
    30,
    true,
    now(),
    now()
FROM generate_series(1, 13) AS n
ON CONFLICT (id) DO UPDATE SET
    price = EXCLUDED.price,
    is_active = EXCLUDED.is_active,
    updated_at = now();

-- График витринного барбера: все семь дней 09:00–18:00 — секция «Свободное время»
-- на профиле получает слоты на любую дату.
INSERT INTO working_schedules
    (id, barber_salon_id, day_of_week, start_time, end_time, is_day_off, created_at, updated_at)
SELECT
    md5('e2e-working-schedule:' || d)::uuid,
    '9e200000-0000-4000-8000-000000000001'::uuid,
    d,
    '09:00'::time,
    '18:00'::time,
    false,
    now(),
    now()
FROM generate_series(0, 6) AS d
ON CONFLICT (id) DO UPDATE SET
    start_time = EXCLUDED.start_time,
    end_time = EXCLUDED.end_time,
    is_day_off = EXCLUDED.is_day_off,
    updated_at = now();

-- 5. Записи. Колонки одинаковые, меняются владелец, барбер и время.
--    Статусы: 1 Pending, 2 Confirmed, 3 Cancelled, 4 Completed, 5 NoShow.

-- 5.1 Витринный барбер: 12 записей, на которых построены опубликованные отзывы.
INSERT INTO bookings
    (id, user_id, barber_profile_id, barber_service_id, salon_id,
     start_at, end_at, price, status, idempotency_key, customer_note, barber_note,
     created_at, updated_at)
SELECT
    md5('e2e-b1-review-booking:' || n)::uuid,
    (ARRAY[
        '9e000000-0000-4000-8000-000000000201',
        '9e000000-0000-4000-8000-000000000202',
        '9e000000-0000-4000-8000-000000000203',
        '9e000000-0000-4000-8000-000000000204',
        '9e000000-0000-4000-8000-000000000205',
        '9e000000-0000-4000-8000-000000000206',
        '9e000000-0000-4000-8000-000000000207',
        '9e000000-0000-4000-8000-000000000208'
    ])[1 + ((n - 1) % 8)]::uuid,
    '9e100000-0000-4000-8000-000000000001'::uuid,
    '9e300000-0000-4000-8000-000000000001'::uuid,
    '9e400000-0000-4000-8000-000000000001'::uuid,
    timestamptz '2026-06-01 09:00+00' + ((n * 2) || ' days')::interval,
    timestamptz '2026-06-01 09:30+00' + ((n * 2) || ' days')::interval,
    30.00,
    4,
    'e2e-b1-review-' || n,
    NULL,
    NULL,
    timestamptz '2026-06-01 10:00+00' + ((n * 2) || ' days')::interval,
    timestamptz '2026-06-01 10:00+00' + ((n * 2) || ' days')::interval
FROM generate_series(1, 12) AS n;

-- 5.2 Витринный барбер: 40 завершённых записей — «История» кабинета на три страницы.
INSERT INTO bookings
    (id, user_id, barber_profile_id, barber_service_id, salon_id,
     start_at, end_at, price, status, idempotency_key, customer_note, barber_note,
     created_at, updated_at)
SELECT
    md5('e2e-b1-history:' || n)::uuid,
    ('9e000000-0000-4000-8000-' || lpad((201 + (n % 8))::text, 12, '0'))::uuid,
    '9e100000-0000-4000-8000-000000000001'::uuid,
    '9e300000-0000-4000-8000-000000000001'::uuid,
    '9e400000-0000-4000-8000-000000000001'::uuid,
    timestamptz '2026-01-05 09:00+00' + (n * 3 || ' days')::interval,
    timestamptz '2026-01-05 09:30+00' + (n * 3 || ' days')::interval,
    30.00,
    4,
    'e2e-b1-history-' || n,
    CASE WHEN n % 5 = 0 THEN 'Комментарий клиента ' || n END,
    CASE WHEN n % 7 = 0 THEN 'Заметка барбера ' || n END,
    timestamptz '2026-01-05 10:00+00' + (n * 3 || ' days')::interval,
    timestamptz '2026-01-05 10:00+00' + (n * 3 || ' days')::interval
FROM generate_series(1, 40) AS n;

-- 5.3 Витринный барбер: 21 предстоящая запись. Страница 1 — 20 строк, страница 2 — одна:
--     ровно то состояние, где терминальное действие опустошает страницу.
INSERT INTO bookings
    (id, user_id, barber_profile_id, barber_service_id, salon_id,
     start_at, end_at, price, status, idempotency_key, customer_note, barber_note,
     created_at, updated_at)
SELECT
    md5('e2e-b1-upcoming:' || n)::uuid,
    '9e000000-0000-4000-8000-000000000004'::uuid,
    '9e100000-0000-4000-8000-000000000001'::uuid,
    '9e300000-0000-4000-8000-000000000001'::uuid,
    '9e400000-0000-4000-8000-000000000001'::uuid,
    date_trunc('day', now()) + (n || ' days')::interval + interval '9 hours',
    date_trunc('day', now()) + (n || ' days')::interval + interval '9 hours 30 minutes',
    30.00,
    CASE WHEN n % 2 = 0 THEN 1 ELSE 2 END,
    'e2e-b1-upcoming-' || n,
    CASE WHEN n % 4 = 0 THEN 'Позвоните заранее' END,
    NULL,
    now(),
    now()
FROM generate_series(1, 21) AS n;

-- 5.4 Клиент e2e-customer: 12 записей, по одной на барьера B2…B13.
INSERT INTO bookings
    (id, user_id, barber_profile_id, barber_service_id, salon_id,
     start_at, end_at, price, status, idempotency_key, customer_note, barber_note,
     created_at, updated_at)
SELECT
    md5('e2e-customer-booking:' || n)::uuid,
    '9e000000-0000-4000-8000-000000000003'::uuid,
    ('9e100000-0000-4000-8000-' || lpad((n + 1)::text, 12, '0'))::uuid,
    ('9e300000-0000-4000-8000-' || lpad((n + 1)::text, 12, '0'))::uuid,
    '9e400000-0000-4000-8000-000000000001'::uuid,
    timestamptz '2026-03-01 09:00+00' + (n || ' days')::interval,
    timestamptz '2026-03-01 09:30+00' + (n || ' days')::interval,
    30.00,
    4,
    'e2e-customer-booking-' || n,
    NULL,
    NULL,
    timestamptz '2026-03-01 10:00+00' + (n || ' days')::interval,
    timestamptz '2026-03-01 10:00+00' + (n || ' days')::interval
FROM generate_series(1, 12) AS n;

-- 5.4.1 Клиент e2e-customer: 21 предстоящая запись — «Предстоящие» на две страницы
--       (20 строк + одна). Барберы B2…B13 по кругу, каждый на своём дне в 09:00 UTC:
--       у одного барбера не больше двух записей, поэтому exclusion-ограничение
--       не срабатывает. Строки нужны account-bookings.spec.ts (пагинатор, hero)
--       и account-bookings-mutations.spec.ts (отмена уменьшает страницу до 19).
INSERT INTO bookings
    (id, user_id, barber_profile_id, barber_service_id, salon_id,
     start_at, end_at, price, status, idempotency_key, customer_note, barber_note,
     created_at, updated_at)
SELECT
    md5('e2e-customer-upcoming:' || n)::uuid,
    '9e000000-0000-4000-8000-000000000003'::uuid,
    ('9e100000-0000-4000-8000-' || lpad((2 + ((n - 1) % 12))::text, 12, '0'))::uuid,
    ('9e300000-0000-4000-8000-' || lpad((2 + ((n - 1) % 12))::text, 12, '0'))::uuid,
    '9e400000-0000-4000-8000-000000000001'::uuid,
    date_trunc('day', now()) + (n || ' days')::interval + interval '9 hours',
    date_trunc('day', now()) + (n || ' days')::interval + interval '9 hours 30 minutes',
    30.00,
    CASE WHEN n % 2 = 0 THEN 1 ELSE 2 END,
    'e2e-customer-upcoming-' || n,
    NULL,
    NULL,
    now(),
    now()
FROM generate_series(1, 21) AS n;

-- 5.4.2 Клиент e2e-customer: 9 прошедших записей со статусами 3/5 (отменена/неявка).
--       Вместе с 12 завершёнными из 5.4 они дают ровно 21 строку истории — тоже две
--       страницы (20 + одна). Статус не Completed намеренно: завершённый визит
--       предлагал бы отзыв, а эти строки нужны только пагинатору и порядку.
--       Нечётные строки — отмены барбера с причиной: account-bookings.spec.ts
--       проверяет, что клиент видит её в истории. Чётные — неявки без причины.
INSERT INTO bookings
    (id, user_id, barber_profile_id, barber_service_id, salon_id,
     start_at, end_at, price, status, idempotency_key, customer_note, barber_note,
     cancellation_reason, created_at, updated_at)
SELECT
    md5('e2e-customer-history:' || n)::uuid,
    '9e000000-0000-4000-8000-000000000003'::uuid,
    ('9e100000-0000-4000-8000-' || lpad((2 + ((n - 1) % 12))::text, 12, '0'))::uuid,
    ('9e300000-0000-4000-8000-' || lpad((2 + ((n - 1) % 12))::text, 12, '0'))::uuid,
    '9e400000-0000-4000-8000-000000000001'::uuid,
    now() - ((2 * n) || ' days')::interval + interval '15 hours',
    now() - ((2 * n) || ' days')::interval + interval '15 hours 30 minutes',
    30.00,
    CASE WHEN n % 2 = 0 THEN 5 ELSE 3 END,
    'e2e-customer-history-' || n,
    NULL,
    NULL,
    CASE WHEN n % 2 = 0 THEN NULL ELSE 'Барбер заболел' END,
    now(),
    now()
FROM generate_series(1, 9) AS n;

-- 5.5 Клиент e2e-mutations: 12 записей с отзывами (вторая страница — две строки)
--     и 2 свободные записи без отзыва — на них тест пересоздаёт удалённый отзыв.
INSERT INTO bookings
    (id, user_id, barber_profile_id, barber_service_id, salon_id,
     start_at, end_at, price, status, idempotency_key, customer_note, barber_note,
     created_at, updated_at)
SELECT
    md5('e2e-mutations-booking:' || n)::uuid,
    '9e000000-0000-4000-8000-000000000004'::uuid,
    ('9e100000-0000-4000-8000-' || lpad((n + 1)::text, 12, '0'))::uuid,
    ('9e300000-0000-4000-8000-' || lpad((n + 1)::text, 12, '0'))::uuid,
    '9e400000-0000-4000-8000-000000000001'::uuid,
    timestamptz '2026-04-01 09:00+00' + (n || ' days')::interval,
    timestamptz '2026-04-01 09:30+00' + (n || ' days')::interval,
    30.00,
    4,
    'e2e-mutations-booking-' || n,
    NULL,
    NULL,
    timestamptz '2026-04-01 10:00+00' + (n || ' days')::interval,
    timestamptz '2026-04-01 10:00+00' + (n || ' days')::interval
FROM generate_series(1, 12) AS n;

INSERT INTO bookings
    (id, user_id, barber_profile_id, barber_service_id, salon_id,
     start_at, end_at, price, status, idempotency_key, customer_note, barber_note,
     created_at, updated_at)
SELECT
    md5('e2e-mutations-spare:' || n)::uuid,
    '9e000000-0000-4000-8000-000000000004'::uuid,
    ('9e100000-0000-4000-8000-' || lpad((n + 1)::text, 12, '0'))::uuid,
    ('9e300000-0000-4000-8000-' || lpad((n + 1)::text, 12, '0'))::uuid,
    '9e400000-0000-4000-8000-000000000001'::uuid,
    timestamptz '2026-05-01 09:00+00' + (n || ' days')::interval,
    timestamptz '2026-05-01 09:30+00' + (n || ' days')::interval,
    30.00,
    4,
    'e2e-mutations-spare-' || n,
    NULL,
    NULL,
    timestamptz '2026-05-01 10:00+00' + (n || ' days')::interval,
    timestamptz '2026-05-01 10:00+00' + (n || ' days')::interval
FROM generate_series(1, 2) AS n;

-- 5.6 Две записи под отзывы на модерации (вкладка «На модерации»).
INSERT INTO bookings
    (id, user_id, barber_profile_id, barber_service_id, salon_id,
     start_at, end_at, price, status, idempotency_key, customer_note, barber_note,
     created_at, updated_at)
SELECT
    md5('e2e-moderation-booking:' || n)::uuid,
    ('9e000000-0000-4000-8000-' || lpad((201 + n)::text, 12, '0'))::uuid,
    '9e100000-0000-4000-8000-000000000002'::uuid,
    '9e300000-0000-4000-8000-000000000002'::uuid,
    '9e400000-0000-4000-8000-000000000001'::uuid,
    timestamptz '2026-07-01 09:00+00' + (n || ' days')::interval,
    timestamptz '2026-07-01 09:30+00' + (n || ' days')::interval,
    30.00,
    4,
    'e2e-moderation-booking-' || n,
    NULL,
    NULL,
    timestamptz '2026-07-01 10:00+00' + (n || ' days')::interval,
    timestamptz '2026-07-01 10:00+00' + (n || ' days')::interval
FROM generate_series(1, 2) AS n;

-- 5.7 Клиент поддержки и 24 брони админского списка (`/admin/bookings`).
--
-- Клиент отдельный (`e2e-bookings@…`): поиск по его email даёт ровно этот набор.
-- Брони `e2e-customer`/`e2e-mutations` меняют читающие и мутирующие спеки кабинетов,
-- и общий счётчик админского списка поехал бы от чужой отмены.
--
-- Дата фиксированная и в прошлом (2026-09-12 UTC). Прошлое — не оплошность:
--   * порядок строк и вторая страница не зависят от дня прогона;
--   * неявку домен разрешает только для `Confirmed` с началом в прошлом, поэтому
--     на «сегодняшних» данных кнопка «Отметить неявку» то появлялась бы, то нет.
-- Шаг 30 минут: интервалы не пересекаются, и exclusion-ограничение
-- `ex_bookings_no_overlap` (полуоткрытые `[start, end)`) не срабатывает.
--
-- 24 строки = две страницы общего пейджера (20 + 4) и все пять статусов сразу:
-- 18 Pending (вторая страница и отмена, оставляющая 17), 3 Confirmed (неявка),
-- 1 Completed, 1 Cancelled, 1 NoShow. Самая поздняя — NN 24: она первая в списке
-- «свежие сверху», и читающая спека проверяет на ней пояс салона (20:30Z → 23:30).
--
-- Id фиксированные (`9e900000-…-0000000000NN`): читающая спека проверяет точный id
-- первой строки, мутирующая открывает NN 01 и NN 19 поиском по id. md5() дал бы
-- стабильные, но непредсказуемые id, и эти проверки стали бы невозможны.
--
-- Идемпотентность: удаляются только брони этой секции (`9e900000-%`). Остальные
-- брони принадлежат `account-bookings*`, `barber-cabinet*` и мутирующим спекам —
-- их трогать нельзя.
INSERT INTO users
    (id, email, password_hash, first_name, last_name, phone_number, avatar_url,
     is_email_verified, is_phone_verified, role, is_active, last_login_at,
     barber_profile_id, created_at, updated_at, security_stamp, failed_login_attempts)
VALUES
    ('9e000000-0000-4000-8000-000000000009', 'e2e-bookings@barbersearch.test',
     'AQAAAAIAAYagAAAAEPI2KBIkNonT1XQgxFg9pgsJsMX97SCCS0mO/jLxqGuhFjZwh4sumyi/WPkW6vG6Ew==',
     'Ирина', 'Волкова', '+70000000009', NULL,
     true, false, 1, true, NULL, NULL,
     timestamptz '2026-09-01 10:04+00', timestamptz '2026-09-01 10:04+00',
     'e2e-stamp-bookings', 0)
ON CONFLICT (id) DO UPDATE SET
    email = EXCLUDED.email,
    password_hash = EXCLUDED.password_hash,
    is_active = EXCLUDED.is_active,
    is_email_verified = EXCLUDED.is_email_verified,
    role = EXCLUDED.role,
    failed_login_attempts = 0,
    lockout_end = NULL,
    updated_at = now();

DELETE FROM bookings WHERE id::text LIKE '9e900000-%';

WITH fixture AS (
    SELECT
        n,
        timestamptz '2026-09-12 09:00+00' + ((n - 1) * 30 || ' minutes')::interval AS start_at,
        timestamptz '2026-09-11 12:00+00' + ((n - 1) || ' minutes')::interval AS created_at
    FROM generate_series(1, 24) AS n
)
INSERT INTO bookings
    (id, user_id, barber_profile_id, barber_service_id, salon_id,
     start_at, end_at, price, status, idempotency_key, customer_note, barber_note,
     created_at, updated_at)
SELECT
    ('9e900000-0000-4000-8000-' || lpad(fixture.n::text, 12, '0'))::uuid,
    '9e000000-0000-4000-8000-000000000009'::uuid,
    '9e100000-0000-4000-8000-000000000001'::uuid,
    '9e300000-0000-4000-8000-000000000001'::uuid,
    '9e400000-0000-4000-8000-000000000001'::uuid,
    fixture.start_at,
    fixture.start_at + interval '30 minutes',
    30.00,
    CASE
        WHEN fixture.n <= 18 THEN 1 -- Pending: 18 строк, отмена оставляет 17
        WHEN fixture.n <= 21 THEN 2 -- Confirmed: неявка доступна, начало в прошлом
        WHEN fixture.n = 22 THEN 4 -- Completed
        WHEN fixture.n = 23 THEN 3 -- Cancelled
        ELSE 5                     -- NoShow: самая поздняя строка, первая в списке
    END,
    'e2e-admin-bookings-' || fixture.n,
    CASE WHEN fixture.n = 1 THEN 'Позвоните за час' END,
    CASE WHEN fixture.n = 1 THEN 'Клиент предупреждён' END,
    fixture.created_at,
    fixture.created_at
FROM fixture;

-- 6. Отзывы.

-- 6.1 Витринный барбер: 12 опубликованных (оценки 5/4 через одну → среднее 4.50).
INSERT INTO reviews
    (id, barber_profile_id, user_id, booking_id, rating, text, is_published,
     created_at, updated_at)
SELECT
    md5('e2e-b1-review:' || n)::uuid,
    '9e100000-0000-4000-8000-000000000001'::uuid,
    (ARRAY[
        '9e000000-0000-4000-8000-000000000201',
        '9e000000-0000-4000-8000-000000000202',
        '9e000000-0000-4000-8000-000000000203',
        '9e000000-0000-4000-8000-000000000204',
        '9e000000-0000-4000-8000-000000000205',
        '9e000000-0000-4000-8000-000000000206',
        '9e000000-0000-4000-8000-000000000207',
        '9e000000-0000-4000-8000-000000000208'
    ])[1 + ((n - 1) % 8)]::uuid,
    md5('e2e-b1-review-booking:' || n)::uuid,
    CASE WHEN n % 2 = 1 THEN 5 ELSE 4 END,
    'E2E отзыв витрины №' || n,
    true,
    timestamptz '2026-06-02 10:00+00' + ((n * 2) || ' days')::interval,
    timestamptz '2026-06-02 10:00+00' + ((n * 2) || ' days')::interval
FROM generate_series(1, 12) AS n;

-- 6.2 Клиент e2e-customer: 12 отзывов на B2…B13.
INSERT INTO reviews
    (id, barber_profile_id, user_id, booking_id, rating, text, is_published,
     created_at, updated_at)
SELECT
    md5('e2e-customer-review:' || n)::uuid,
    ('9e100000-0000-4000-8000-' || lpad((n + 1)::text, 12, '0'))::uuid,
    '9e000000-0000-4000-8000-000000000003'::uuid,
    md5('e2e-customer-booking:' || n)::uuid,
    5,
    'E2E отзыв клиента №' || n,
    true,
    timestamptz '2026-03-02 10:00+00' + (n || ' days')::interval,
    timestamptz '2026-03-02 10:00+00' + (n || ' days')::interval
FROM generate_series(1, 12) AS n;

-- 6.3 Клиент e2e-mutations: 12 СКРЫТЫХ отзывов на B2…B13. Скрытые, а не опубликованные,
--     потому что этот набор удаляет и пересоздаёт мутирующая спека: опубликованная
--     очередь админки не должна меняться между прогонами.
INSERT INTO reviews
    (id, barber_profile_id, user_id, booking_id, rating, text, is_published,
     created_at, updated_at)
SELECT
    md5('e2e-mutations-review:' || n)::uuid,
    ('9e100000-0000-4000-8000-' || lpad((n + 1)::text, 12, '0'))::uuid,
    '9e000000-0000-4000-8000-000000000004'::uuid,
    md5('e2e-mutations-booking:' || n)::uuid,
    4,
    'E2E отзыв для правок №' || n,
    false,
    timestamptz '2026-04-02 10:00+00' + (n || ' days')::interval,
    timestamptz '2026-04-02 10:00+00' + (n || ' days')::interval
FROM generate_series(1, 12) AS n;

-- 6.4 Два отзыва на модерации — вкладка «На модерации» не пуста.
INSERT INTO reviews
    (id, barber_profile_id, user_id, booking_id, rating, text, is_published,
     created_at, updated_at)
SELECT
    md5('e2e-moderation-review:' || n)::uuid,
    '9e100000-0000-4000-8000-000000000002'::uuid,
    ('9e000000-0000-4000-8000-' || lpad((201 + n)::text, 12, '0'))::uuid,
    md5('e2e-moderation-booking:' || n)::uuid,
    3,
    'E2E отзыв на модерации №' || n,
    false,
    timestamptz '2026-07-02 10:00+00' + (n || ' days')::interval,
    timestamptz '2026-07-02 10:00+00' + (n || ' days')::interval
FROM generate_series(1, 2) AS n;

-- Привязка профиля к аккаунту барбера: без `users.barber_profile_id` роль Barber
-- не получает claim, и кабинет закрывается гейтом «Профиль не привязан».
UPDATE users
SET barber_profile_id = '9e100000-0000-4000-8000-000000000001', updated_at = now()
WHERE id = '9e000000-0000-4000-8000-000000000002';

-- 7. Админский журнал: 25 строк `audit_log` на фиксированный день.
--
-- Вставка напрямую, а не через API: журнал пишет сервер на каждом `SaveChanges`,
-- отдельного write-эндпоинта у него нет и не будет — админский этап только читает
-- уже записанное. Строка аудита — инфраструктурная запись без доменных правил,
-- поэтому фикстура вставляет её SQL-ом.
--
-- Дата в прошлом (2026-09-10 UTC) намеренна: мутирующие спеки пишут свои строки
-- «сегодня», а проверки журнала идут под фильтром по этой дате. Так порядок,
-- вторая страница и фильтры не зависят от того, что успели записать другие спеки,
-- и их строки остаются нетронутыми.
--
-- 25 строк = страница 20 + пять строк на второй странице. Самая свежая запись —
-- 21-я (10:21), у неё `actor_user_id = NULL`: в UI это «Система». 20-я меняет роль
-- и служит раскрытием diff'а «Роль: Клиент → Администратор». Строки 15…01 — ещё
-- пятнадцать правок доступа. Строки 22…25 — новые типы #41 (салон, услуга,
-- специализация и бронь): их времена 10:16:10…10:16:40, то есть внутри того же
-- UTC-дня и раньше 10:17, поэтому 21-я и 20-я остаются первыми, а вторая
-- страница — это строки 05…01 (позиции 21–25).
--
-- Идемпотентность: удаляются только строки фикстур (`9e700000-%`), остальные записи
-- `audit_log` принадлежат мутирующим спекам и не трогаются.
DELETE FROM audit_log WHERE id::text LIKE '9e700000-%';

WITH fixture(n, action, entity_type, entity_id, payload) AS (
    VALUES
        (16, 'user.created', 'user', '9e000000-0000-4000-8000-000000000002',
         '{"values":{"email":"e2e-journal-created@barbersearch.test","role":"Customer","isActive":true}}'),
        (17, 'review.updated', 'review', '9e800000-0000-4000-8000-000000000001',
         '{"changes":{"rating":{"from":4,"to":5}}}'),
        (18, 'review.updated', 'review', '9e800000-0000-4000-8000-000000000001',
         '{"changes":{"isPublished":{"from":false,"to":true}}}'),
        (19, 'barber_profile.updated', 'barber_profile', '9e100000-0000-4000-8000-000000000001',
         '{"changes":{"isVerified":{"from":false,"to":true}}}'),
        (20, 'user.updated', 'user', '9e000000-0000-4000-8000-000000000002',
         '{"changes":{"role":{"from":"Customer","to":"Admin"}}}'),
        (21, 'user.updated', 'user', '9e000000-0000-4000-8000-000000000002',
         '{"changes":{"isActive":{"from":true,"to":false}}}')
    UNION ALL
    -- 15…01: пятнадцать правок доступа — вторая страница журнала.
    SELECT n, 'user.updated', 'user', '9e000000-0000-4000-8000-000000000002',
           '{"changes":{"isActive":{"from":true,"to":false}}}'
    FROM generate_series(1, 15) AS n
)
INSERT INTO audit_log
    (id, occurred_at, actor_user_id, action, entity_type, entity_id, payload)
SELECT
    ('9e700000-0000-4000-8000-' || lpad(fixture.n::text, 12, '0'))::uuid,
    timestamptz '2026-09-10 10:00+00' + (fixture.n || ' minutes')::interval,
    CASE
        WHEN fixture.n = 21 THEN NULL
        ELSE '9e000000-0000-4000-8000-000000000001'::uuid
    END,
    fixture.action,
    fixture.entity_type,
    fixture.entity_id::uuid,
    fixture.payload::jsonb
FROM fixture;

-- 22…25: типы, добавленные в allowlist #41. Отдельный INSERT, потому что схема
-- времени у них другая (10:16:10…10:16:40, а не «10:00 + n минут»), и строки
-- должны лечь между 16-й (10:16) и 17-й (10:17) записями основного блока.
INSERT INTO audit_log
    (id, occurred_at, actor_user_id, action, entity_type, entity_id, payload)
VALUES
    ('9e700000-0000-4000-8000-000000000022'::uuid,
     timestamptz '2026-09-10 10:16:10+00',
     '9e000000-0000-4000-8000-000000000001'::uuid,
     'salon.updated', 'salon',
     '9e400000-0000-4000-8000-000000000001'::uuid,
     '{"changes":{"phoneNumber":{"from":"+70000000009","to":"+70000000010"}}}'::jsonb),
    ('9e700000-0000-4000-8000-000000000023'::uuid,
     timestamptz '2026-09-10 10:16:20+00',
     '9e000000-0000-4000-8000-000000000001'::uuid,
     'hair_service.updated', 'hair_service',
     '9e500000-0000-4000-8000-000000000001'::uuid,
     '{"changes":{"isActive":{"from":true,"to":false}}}'::jsonb),
    ('9e700000-0000-4000-8000-000000000024'::uuid,
     timestamptz '2026-09-10 10:16:30+00',
     '9e000000-0000-4000-8000-000000000001'::uuid,
     'specialization.updated', 'specialization',
     '9e600000-0000-4000-8000-000000000001'::uuid,
     '{"changes":{"name":{"from":"E2E Фейд","to":"E2E Фейд 2"}}}'::jsonb),
    ('9e700000-0000-4000-8000-000000000025'::uuid,
     timestamptz '2026-09-10 10:16:40+00',
     '9e000000-0000-4000-8000-000000000001'::uuid,
     'booking.updated', 'booking',
     '9e900000-0000-4000-8000-000000000024'::uuid,
     '{"changes":{"status":{"from":"Confirmed","to":"NoShow"}}}'::jsonb);

COMMIT;

\echo '== e2e fixtures ready =='
