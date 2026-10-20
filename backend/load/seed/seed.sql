-- Load-test data for the separate database barbersearch_load.
--
-- The generator is set-based and resumable: every step is guarded by a row in
-- load_seed_state and every insert uses deterministic UUIDs plus ON CONFLICT DO NOTHING.
-- A failed step rolls back together with its state marker, so re-running the file
-- continues where it stopped and never duplicates rows.
--
-- Usage (from backend/):
--   psql -h 127.0.0.1 -p 5432 -U postgres -d barbersearch_load -f load/seed/seed.sql
--
-- The script assumes the four existing EF migrations have already been applied to
-- barbersearch_load (see load/README.md). It never touches the barbersearch database.

\set ON_ERROR_STOP on

SET TIME ZONE 'UTC';
SET synchronous_commit = off;
SET statement_timeout = 0;
SET work_mem = '64MB';
SET maintenance_work_mem = '512MB';

CREATE TABLE IF NOT EXISTS load_seed_state (
    key          text PRIMARY KEY,
    completed_at timestamptz NOT NULL DEFAULT now()
);

\echo '== 1/10 reference data: 400 salons, 20 hair services, 10 specializations =='

BEGIN;

INSERT INTO salons
    (id, name, address, city, postal_code, country, time_zone_id,
     latitude, longitude, phone_number, website_url, is_active, created_at, updated_at)
SELECT md5('salon:' || s)::uuid,
       c.city || ' Salon ' || (((s - 1) % 20) + 1),
       'Street ' || s || ', ' || c.city,
       c.city,
       lpad(s::text, 5, '0'),
       c.country,
       c.tz,
       40.0 + (s % 20),
       5.0 + (s % 30),
       '+4900000' || lpad(s::text, 4, '0'),
       'https://salon' || s || '.load.local',
       true,
       now() - interval '1 year',
       now() - interval '1 year'
FROM generate_series(1, 400) AS s
JOIN (VALUES
    (1,  'Berlin',     'Germany',     'Europe/Berlin'),
    (2,  'Munich',     'Germany',     'Europe/Berlin'),
    (3,  'Hamburg',    'Germany',     'Europe/Berlin'),
    (4,  'Cologne',    'Germany',     'Europe/Berlin'),
    (5,  'Frankfurt',  'Germany',     'Europe/Berlin'),
    (6,  'Vienna',     'Austria',     'Europe/Vienna'),
    (7,  'Zurich',     'Switzerland', 'Europe/Zurich'),
    (8,  'Paris',      'France',      'Europe/Paris'),
    (9,  'Lyon',       'France',      'Europe/Paris'),
    (10, 'Amsterdam',  'Netherlands', 'Europe/Amsterdam'),
    (11, 'Brussels',   'Belgium',     'Europe/Brussels'),
    (12, 'Madrid',     'Spain',       'Europe/Madrid'),
    (13, 'Barcelona',  'Spain',       'Europe/Madrid'),
    (14, 'Milan',      'Italy',       'Europe/Rome'),
    (15, 'Rome',       'Italy',       'Europe/Rome'),
    (16, 'Warsaw',     'Poland',      'Europe/Warsaw'),
    (17, 'Prague',     'Czechia',     'Europe/Prague'),
    (18, 'Copenhagen', 'Denmark',     'Europe/Copenhagen'),
    (19, 'Stockholm',  'Sweden',      'Europe/Stockholm'),
    (20, 'Oslo',       'Norway',      'Europe/Oslo')
) AS c(idx, city, country, tz)
  ON c.idx = (((s - 1) / 20) + 1)
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'reference-salons')
ON CONFLICT DO NOTHING;

INSERT INTO hair_services
    (id, name, description, category, default_duration_minutes, is_active, created_at, updated_at)
SELECT md5('hair-service:' || k)::uuid,
       v.name,
       'Load-test catalog service',
       v.category,
       v.duration,
       true,
       now() - interval '1 year',
       now() - interval '1 year'
FROM (VALUES
    (1,  'Classic Haircut',     1, 45),
    (2,  'Fade Haircut',        1, 45),
    (3,  'Beard Trim',          2, 30),
    (4,  'Hot Towel Shave',     2, 30),
    (5,  'Hair Coloring',       3, 90),
    (6,  'Highlights',          3, 120),
    (7,  'Keratin Treatment',   4, 120),
    (8,  'Scalp Treatment',     4, 60),
    (9,  'Kids Haircut',        1, 30),
    (10, 'Buzz Cut',            1, 20),
    (11, 'Pompadour Styling',   1, 45),
    (12, 'Hair Wash',           4, 15),
    (13, 'Perm',                3, 120),
    (14, 'Beard Coloring',      2, 45),
    (15, 'Eyebrow Shaping',     5, 20),
    (16, 'Bridal Updo',         5, 90),
    (17, 'Hair Extensions',     3, 180),
    (18, 'Curly Cut',           1, 60),
    (19, 'Clipper Cut',         1, 30),
    (20, 'Head Shave',          2, 30)
) AS v(k, name, category, duration)
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'reference-hair-services')
ON CONFLICT DO NOTHING;

INSERT INTO specializations (id, name, description, is_active, created_at, updated_at)
SELECT md5('specialization:' || k)::uuid,
       v.name,
       'Load-test specialization',
       true,
       now() - interval '1 year',
       now() - interval '1 year'
FROM (VALUES
    (1,  'Barbering'),
    (2,  'Hair Coloring'),
    (3,  'Mens Grooming'),
    (4,  'Beard Design'),
    (5,  'Hair Treatment'),
    (6,  'Bridal Styling'),
    (7,  'Kids Haircut'),
    (8,  'Curly Hair'),
    (9,  'Hair Extensions'),
    (10, 'Scalp Care')
) AS v(k, name)
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'reference-specializations')
ON CONFLICT DO NOTHING;

INSERT INTO load_seed_state (key) VALUES ('reference-salons') ON CONFLICT DO NOTHING;
INSERT INTO load_seed_state (key) VALUES ('reference-hair-services') ON CONFLICT DO NOTHING;
INSERT INTO load_seed_state (key) VALUES ('reference-specializations') ON CONFLICT DO NOTHING;
INSERT INTO load_seed_state (key) VALUES ('reference') ON CONFLICT DO NOTHING;

COMMIT;

\echo '== 2/10 users: 2 000 customers and 12 000 barber owners =='

BEGIN;

INSERT INTO users
    (id, email, password_hash, first_name, last_name, phone_number, avatar_url,
     is_email_verified, is_phone_verified, role, is_active, last_login_at,
     barber_profile_id, created_at, updated_at, security_stamp)
SELECT md5('load-customer:' || i)::uuid,
       'customer' || i || '@load.local',
       'LOADTEST-NOT-A-REAL-PASSWORD-HASH',
       'Load',
       'Customer ' || i,
       NULL, NULL, true, false, 1, true, NULL, NULL,
       now() - interval '30 days',
       now() - interval '30 days',
       md5('customer-stamp:' || i)
FROM generate_series(1, 2000) AS i
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'users-customers')
ON CONFLICT DO NOTHING;

INSERT INTO load_seed_state (key) VALUES ('users-customers') ON CONFLICT DO NOTHING;

INSERT INTO users
    (id, email, password_hash, first_name, last_name, phone_number, avatar_url,
     is_email_verified, is_phone_verified, role, is_active, last_login_at,
     barber_profile_id, created_at, updated_at, security_stamp)
SELECT md5('load-barber-user:' || b)::uuid,
       'barber' || b || '@load.local',
       'LOADTEST-NOT-A-REAL-PASSWORD-HASH',
       'Load',
       'Barber ' || b,
       NULL, NULL, true, false, 2, true, NULL,
       md5('barber:' || b)::uuid,
       now() - interval '1 year',
       now() - interval '1 year',
       md5('barber-stamp:' || b)
FROM generate_series(1, 12000) AS b
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'users-barbers')
ON CONFLICT DO NOTHING;

INSERT INTO load_seed_state (key) VALUES ('users-barbers') ON CONFLICT DO NOTHING;

COMMIT;

\echo '== 3/10 barber profiles (10 000 visible, 1 000 unverified, 1 000 inactive) =='

BEGIN;

INSERT INTO barber_profiles
    (id, user_id, display_name, grade, bio, languages, years_of_experience,
     certificates_and_training, rating, reviews_count, achievements,
     payment_methods, communication_format, goodies, late_policy,
     is_active, is_verified, created_at, updated_at)
SELECT md5('barber:' || b)::uuid,
       md5('load-barber-user:' || b)::uuid,
       'Barber ' || lpad(b::text, 5, '0'),
       b % 11,
       'Load-test barber profile #' || b || '.',
       ARRAY[1, 2],
       1 + (b % 30),
       ARRAY['Certified barber'],
       NULL,
       0,
       ARRAY['Top rated'],
       ARRAY[1, 2],
       ARRAY['Friendly'],
       ARRAY['Coffee'],
       '10 minutes',
       b <= 11000,
       (b <= 10000 OR b > 11000),
       now() - ((b % 365) * interval '1 day'),
       now() - ((b % 365) * interval '1 day')
FROM generate_series(1, 12000) AS b
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'profiles')
ON CONFLICT DO NOTHING;

INSERT INTO load_seed_state (key) VALUES ('profiles') ON CONFLICT DO NOTHING;

COMMIT;

\echo '== 4/10 barber -> salon links (primary + secondary) =='

BEGIN;

INSERT INTO barber_salons
    (id, barber_profile_id, salon_id, is_primary, created_at, updated_at)
SELECT md5('barber-salon:p:' || b)::uuid,
       md5('barber:' || b)::uuid,
       md5('salon:' || (((b - 1) % 400) + 1))::uuid,
       true,
       now() - interval '1 year',
       now() - interval '1 year'
FROM generate_series(1, 12000) AS b
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'barber-salons')
ON CONFLICT DO NOTHING;

INSERT INTO barber_salons
    (id, barber_profile_id, salon_id, is_primary, created_at, updated_at)
SELECT md5('barber-salon:s:' || b)::uuid,
       md5('barber:' || b)::uuid,
       md5('salon:' || ((((b - 1) % 400) + 20) % 400 + 1))::uuid,
       false,
       now() - interval '1 year',
       now() - interval '1 year'
FROM generate_series(1, 12000) AS b
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'barber-salons')
ON CONFLICT DO NOTHING;

INSERT INTO load_seed_state (key) VALUES ('barber-salons') ON CONFLICT DO NOTHING;

COMMIT;

\echo '== 5/10 working schedules (7 days per barber-salon link) =='

BEGIN;

INSERT INTO working_schedules
    (id, barber_salon_id, day_of_week, start_time, end_time, is_day_off, created_at, updated_at)
SELECT md5('schedule:p:' || b || ':' || d)::uuid,
       md5('barber-salon:p:' || b)::uuid,
       d,
       '09:00'::time,
       '18:00'::time,
       d = 0,
       now() - interval '1 year',
       now() - interval '1 year'
FROM generate_series(1, 12000) AS b
CROSS JOIN generate_series(0, 6) AS d
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'working-schedules')
ON CONFLICT DO NOTHING;

INSERT INTO working_schedules
    (id, barber_salon_id, day_of_week, start_time, end_time, is_day_off, created_at, updated_at)
SELECT md5('schedule:s:' || b || ':' || d)::uuid,
       md5('barber-salon:s:' || b)::uuid,
       d,
       '10:00'::time,
       '20:00'::time,
       d NOT BETWEEN 2 AND 5,
       now() - interval '1 year',
       now() - interval '1 year'
FROM generate_series(1, 12000) AS b
CROSS JOIN generate_series(0, 6) AS d
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'working-schedules')
ON CONFLICT DO NOTHING;

INSERT INTO load_seed_state (key) VALUES ('working-schedules') ON CONFLICT DO NOTHING;

COMMIT;

\echo '== 6/10 barber services (3 per profile) =='

BEGIN;

INSERT INTO barber_services
    (id, barber_profile_id, hair_service_id, price, duration_minutes, is_active, created_at, updated_at)
SELECT md5('barber-service:' || b || ':' || k)::uuid,
       md5('barber:' || b)::uuid,
       md5('hair-service:' || (((b - 1 + k) % 20) + 1))::uuid,
       30 + ((b + k) % 5) * 10,
       30 + (k * 15),
       true,
       now() - interval '1 year',
       now() - interval '1 year'
FROM generate_series(1, 12000) AS b
CROSS JOIN generate_series(0, 2) AS k
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'barber-services')
ON CONFLICT DO NOTHING;

INSERT INTO load_seed_state (key) VALUES ('barber-services') ON CONFLICT DO NOTHING;

COMMIT;

\echo '== 7/10 barber specializations (2 per profile) =='

BEGIN;

INSERT INTO barber_specializations
    (id, barber_profile_id, specialization_id, created_at, updated_at)
SELECT md5('barber-specialization:' || b || ':' || k)::uuid,
       md5('barber:' || b)::uuid,
       md5('specialization:' || (((b - 1 + k * 3) % 10) + 1))::uuid,
       now() - interval '1 year',
       now() - interval '1 year'
FROM generate_series(1, 12000) AS b
CROSS JOIN generate_series(0, 1) AS k
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'barber-specializations')
ON CONFLICT DO NOTHING;

INSERT INTO load_seed_state (key) VALUES ('barber-specializations') ON CONFLICT DO NOTHING;

COMMIT;

\echo '== 8/10 portfolios and time offs =='

BEGIN;

INSERT INTO portfolio_photos
    (id, barber_profile_id, url, thumbnail_url, description, sort_order,
     is_published, created_at, updated_at)
SELECT md5('photo:' || b || ':' || k)::uuid,
       md5('barber:' || b)::uuid,
       'https://cdn.load.local/barbers/' || b || '/' || k || '.jpg',
       NULL,
       'Load-test portfolio photo',
       k,
       true,
       now() - interval '6 months',
       now() - interval '6 months'
FROM generate_series(1, 12000) AS b
CROSS JOIN generate_series(0, 2) AS k
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'portfolio-photos')
ON CONFLICT DO NOTHING;

INSERT INTO load_seed_state (key) VALUES ('portfolio-photos') ON CONFLICT DO NOTHING;

INSERT INTO time_offs
    (id, barber_profile_id, start_at, end_at, reason, created_at, updated_at)
SELECT md5('time-off:' || b)::uuid,
       md5('barber:' || b)::uuid,
       date_trunc('day', now()) + interval '7 days' + interval '12 hours',
       date_trunc('day', now()) + interval '7 days' + interval '14 hours',
       'Load-test vacation',
       now() - interval '1 month',
       now() - interval '1 month'
FROM generate_series(1, 12000) AS b
WHERE b % 20 = 0
  AND NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'time-offs')
ON CONFLICT DO NOTHING;

INSERT INTO load_seed_state (key) VALUES ('time-offs') ON CONFLICT DO NOTHING;

COMMIT;

\echo '== 9/10 bookings: 1 200 000 rows (100 per barber) =='

BEGIN;

INSERT INTO bookings
    (id, user_id, barber_profile_id, barber_service_id, salon_id,
     start_at, end_at, price, status, idempotency_key,
     customer_note, barber_note, created_at, updated_at)
SELECT md5('booking:' || x.b || ':' || x.n)::uuid,
       md5('load-customer:' || (1 + ((x.b * 100 + x.n) % 2000)))::uuid,
       md5('barber:' || x.b)::uuid,
       md5('barber-service:' || x.b || ':' || (x.n % 3))::uuid,
       md5('salon:' || (((x.b - 1) % 400) + 1))::uuid,
       x.start_at,
       x.start_at + interval '30 minutes',
       30 + (x.b % 5) * 10,
       CASE
           WHEN x.n <= 69 THEN 4
           WHEN x.n <= 89 THEN 3
           WHEN x.n <= 96 THEN 5
           ELSE 2
       END,
       'seed-b' || x.b || '-n' || x.n,
       NULL,
       NULL,
       CASE
           WHEN x.n < 97 THEN x.start_at - interval '7 days'
           ELSE x.start_at - interval '3 days'
       END,
       x.start_at
FROM (
    SELECT b,
           n,
           CASE
               WHEN n < 97 THEN
                   date_trunc('day', now())
                   - ((n + 1) * interval '1 day')
                   + ((9 + (n % 8)) * interval '1 hour')
               ELSE
                   date_trunc('day', now())
                   + ((n - 96) * interval '1 day')
                   + interval '10 hours'
           END AS start_at
    FROM generate_series(1, 12000) AS b
    CROSS JOIN generate_series(0, 99) AS n
) AS x
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'bookings')
ON CONFLICT DO NOTHING;

INSERT INTO load_seed_state (key) VALUES ('bookings') ON CONFLICT DO NOTHING;

COMMIT;

\echo '== 10/10 reviews and denormalized ratings =='

BEGIN;

-- 80 000 reviews: 8 per visible barber; the first 6 are published.
INSERT INTO reviews
    (id, barber_profile_id, user_id, booking_id, rating, text,
     is_published, created_at, updated_at)
SELECT md5('review:' || b || ':' || n)::uuid,
       md5('barber:' || b)::uuid,
       md5('load-customer:' || (1 + ((b * 100 + n) % 2000)))::uuid,
       md5('booking:' || b || ':' || n)::uuid,
       CASE ((b * 13 + n * 7) % 10)
           WHEN 0 THEN 1
           WHEN 1 THEN 2
           WHEN 2 THEN 3
           WHEN 3 THEN 4
           WHEN 4 THEN 5
           WHEN 5 THEN 5
           WHEN 6 THEN 4
           WHEN 7 THEN 4
           WHEN 8 THEN 3
           ELSE 5
       END,
       'Load-test review ' || b || '-' || n,
       n < 6,
       date_trunc('day', now())
           - ((n + 1) * interval '1 day')
           + ((9 + (n % 8)) * interval '1 hour')
           + interval '1 day',
       date_trunc('day', now())
           - ((n + 1) * interval '1 day')
           + ((9 + (n % 8)) * interval '1 hour')
           + interval '1 day'
FROM generate_series(1, 10000) AS b
CROSS JOIN generate_series(0, 7) AS n
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'reviews')
ON CONFLICT DO NOTHING;

-- Contention barbers (three measured + one warm-up) get 12 extra hidden reviews
-- each, 20 reviews in total.
INSERT INTO reviews
    (id, barber_profile_id, user_id, booking_id, rating, text,
     is_published, created_at, updated_at)
SELECT md5('review:' || b || ':' || n)::uuid,
       md5('barber:' || b)::uuid,
       md5('load-customer:' || (1 + ((b * 100 + n) % 2000)))::uuid,
       md5('booking:' || b || ':' || n)::uuid,
       CASE ((b * 13 + n * 7) % 10)
           WHEN 0 THEN 1
           WHEN 1 THEN 2
           WHEN 2 THEN 3
           WHEN 3 THEN 4
           WHEN 4 THEN 5
           WHEN 5 THEN 5
           WHEN 6 THEN 4
           WHEN 7 THEN 4
           WHEN 8 THEN 3
           ELSE 5
       END,
       'Load-test contention review ' || b || '-' || n,
       false,
       date_trunc('day', now())
           - ((n + 1) * interval '1 day')
           + ((9 + (n % 8)) * interval '1 hour')
           + interval '1 day',
       date_trunc('day', now())
           - ((n + 1) * interval '1 day')
           + ((9 + (n % 8)) * interval '1 hour')
           + interval '1 day'
FROM generate_series(1, 8) AS b
CROSS JOIN generate_series(8, 19) AS n
WHERE b IN (1, 4, 5, 8)
  AND NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'reviews-contention')
ON CONFLICT DO NOTHING;

-- Two more contention barbers (10 and 11) make five measured repeats possible
-- with a distinct warm-up barber. This is a separate marker so an already
-- seeded barbersearch_load can be upgraded by re-running seed.sql.
INSERT INTO reviews
    (id, barber_profile_id, user_id, booking_id, rating, text,
     is_published, created_at, updated_at)
SELECT md5('review:' || b || ':' || n)::uuid,
       md5('barber:' || b)::uuid,
       md5('load-customer:' || (1 + ((b * 100 + n) % 2000)))::uuid,
       md5('booking:' || b || ':' || n)::uuid,
       CASE ((b * 13 + n * 7) % 10)
           WHEN 0 THEN 1
           WHEN 1 THEN 2
           WHEN 2 THEN 3
           WHEN 3 THEN 4
           WHEN 4 THEN 5
           WHEN 5 THEN 5
           WHEN 6 THEN 4
           WHEN 7 THEN 4
           WHEN 8 THEN 3
           ELSE 5
       END,
       'Load-test contention review ' || b || '-' || n,
       false,
       date_trunc('day', now())
           - ((n + 1) * interval '1 day')
           + ((9 + (n % 8)) * interval '1 hour')
           + interval '1 day',
       date_trunc('day', now())
           - ((n + 1) * interval '1 day')
           + ((9 + (n % 8)) * interval '1 hour')
           + interval '1 day'
FROM generate_series(10, 11) AS b
CROSS JOIN generate_series(8, 19) AS n
WHERE NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'reviews-contention-extra')
ON CONFLICT DO NOTHING;

INSERT INTO load_seed_state (key) VALUES ('reviews') ON CONFLICT DO NOTHING;
INSERT INTO load_seed_state (key) VALUES ('reviews-contention') ON CONFLICT DO NOTHING;
INSERT INTO load_seed_state (key) VALUES ('reviews-contention-extra') ON CONFLICT DO NOTHING;

UPDATE barber_profiles AS p
SET rating = s.avg_rating,
    reviews_count = s.cnt,
    updated_at = now()
FROM (
    SELECT barber_profile_id,
           round(avg(rating)::numeric, 2) AS avg_rating,
           count(*)::int AS cnt
    FROM reviews
    WHERE is_published
    GROUP BY barber_profile_id
) AS s
WHERE p.id = s.barber_profile_id
  AND NOT EXISTS (SELECT 1 FROM load_seed_state WHERE key = 'ratings');

INSERT INTO load_seed_state (key) VALUES ('ratings') ON CONFLICT DO NOTHING;

COMMIT;

ANALYZE salons;
ANALYZE hair_services;
ANALYZE specializations;
ANALYZE users;
ANALYZE barber_profiles;
ANALYZE barber_salons;
ANALYZE working_schedules;
ANALYZE barber_services;
ANALYZE barber_specializations;
ANALYZE portfolio_photos;
ANALYZE time_offs;
ANALYZE bookings;
ANALYZE reviews;

\echo '== seed complete =='
