-- Minimal database for the booking-idempotency CI check.
--
-- One active salon, one hair service, one barber profile with one active service
-- and a seven-day working schedule. No 1.2M-row seed and no manifest.sql: the
-- identifiers are fixed, so the CI script can build the harness context itself.
--
-- Usage (on a freshly migrated, isolated database):
--   psql -h 127.0.0.1 -p 5432 -U postgres -d barbersearch_load_ci \
--        -v ON_ERROR_STOP=1 -f load/seed/minimal.sql
--
-- The script is idempotent and never touches barbersearch / barbersearch_load.

\set ON_ERROR_STOP on
SET TIME ZONE 'UTC';

BEGIN;

INSERT INTO salons
    (id, name, address, city, postal_code, country, time_zone_id,
     latitude, longitude, phone_number, website_url, is_active, created_at, updated_at)
VALUES
    ('11111111-1111-1111-1111-111111111111',
     'CI Salon', 'CI Street 1', 'Berlin', '10115', 'Germany', 'Europe/Berlin',
     52.52, 13.405, '+490000000001', 'https://ci.local', true, now(), now())
ON CONFLICT (id) DO NOTHING;

INSERT INTO hair_services
    (id, name, description, category, default_duration_minutes,
     is_active, created_at, updated_at)
VALUES
    ('22222222-2222-2222-2222-222222222222',
     'CI Haircut', 'CI catalog service', 1, 30, true, now(), now())
ON CONFLICT (id) DO NOTHING;

-- Owner of the profile. The password hash is deliberately unusable: the CI
-- check authenticates as the customer, not as this user.
INSERT INTO users
    (id, email, password_hash, first_name, last_name, phone_number, avatar_url,
     is_email_verified, is_phone_verified, role, is_active, last_login_at,
     barber_profile_id, created_at, updated_at, security_stamp)
VALUES
    ('33333333-3333-3333-3333-333333333333',
     'ci-owner@barbersearch.test', 'CI-NOT-A-REAL-PASSWORD-HASH', 'CI', 'Owner',
     NULL, NULL, true, false, 2, true, NULL, NULL, now(), now(),
     'ci-owner-security-stamp')
ON CONFLICT (id) DO NOTHING;

INSERT INTO barber_profiles
    (id, user_id, display_name, grade, bio, languages, years_of_experience,
     certificates_and_training, rating, reviews_count, achievements,
     payment_methods, communication_format, goodies, late_policy,
     is_active, is_verified, created_at, updated_at)
VALUES
    ('44444444-4444-4444-4444-444444444444',
     '33333333-3333-3333-3333-333333333333',
     'CI Barber', 5, 'CI booking-idempotency profile', ARRAY[1, 2], 10,
     ARRAY['CI certificate'], NULL, 0, ARRAY[]::text[],
     ARRAY[1, 2], ARRAY['Friendly'], ARRAY['Coffee'], '10 minutes',
     true, true, now(), now())
ON CONFLICT (id) DO NOTHING;

INSERT INTO barber_salons
    (id, barber_profile_id, salon_id, is_primary, created_at, updated_at)
VALUES
    ('55555555-5555-5555-5555-555555555555',
     '44444444-4444-4444-4444-444444444444',
     '11111111-1111-1111-1111-111111111111',
     true, now(), now())
ON CONFLICT (id) DO NOTHING;

-- All seven weekdays are working days, so the CI slot date needs no calendar
-- arithmetic beyond "in the future".
INSERT INTO working_schedules
    (id, barber_salon_id, day_of_week, start_time, end_time, is_day_off, created_at, updated_at)
SELECT md5('ci-working-schedule:' || d)::uuid,
       '55555555-5555-5555-5555-555555555555'::uuid,
       d,
       '09:00'::time,
       '18:00'::time,
       false,
       now(),
       now()
FROM generate_series(0, 6) AS d
ON CONFLICT (id) DO NOTHING;

INSERT INTO barber_services
    (id, barber_profile_id, hair_service_id, price, duration_minutes,
     is_active, created_at, updated_at)
VALUES
    ('77777777-7777-7777-7777-777777777777',
     '44444444-4444-4444-4444-444444444444',
     '22222222-2222-2222-2222-222222222222',
     30.00, 30, true, now(), now())
ON CONFLICT (id) DO NOTHING;

COMMIT;

\echo '== minimal booking-idempotency data ready =='
