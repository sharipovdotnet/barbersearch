-- Finds dangling cross-aggregate references (R-02).
--
-- The FK constraints added by migration AddCrossAggregateForeignKeys are NOT VALID,
-- so PostgreSQL enforces them for new rows but keeps pre-existing rows untouched.
-- Run this read-only check before validating the constraints (and daily until the
-- legacy rows are cleaned):
--
--   psql -h 127.0.0.1 -p 5432 -U postgres -d barbersearch `
--       -f deploy/find-dangling-references.sql
--
-- A non-zero dangling_count means the referenced aggregate was deleted while the
-- child row stayed behind. Fix the rows manually (delete, repoint, or recreate the
-- parent), then run the VALIDATE CONSTRAINT statements printed at the end.

\pset pager off

WITH dangling AS (
    -- Links protected by the new NOT VALID constraints.
    SELECT
        'barber_salons.salon_id -> salons.id' AS reference,
        bs.id AS row_id,
        bs.salon_id AS missing_id
    FROM barber_salons AS bs
    LEFT JOIN salons AS s ON s.id = bs.salon_id
    WHERE s.id IS NULL

    UNION ALL

    SELECT
        'barber_services.hair_service_id -> hair_services.id',
        service.id,
        service.hair_service_id
    FROM barber_services AS service
    LEFT JOIN hair_services AS hair_service ON hair_service.id = service.hair_service_id
    WHERE hair_service.id IS NULL

    UNION ALL

    SELECT
        'barber_specializations.specialization_id -> specializations.id',
        link.id,
        link.specialization_id
    FROM barber_specializations AS link
    LEFT JOIN specializations AS specialization
        ON specialization.id = link.specialization_id
    WHERE specialization.id IS NULL

    UNION ALL

    SELECT
        'favorite_barbers.barber_profile_id -> barber_profiles.id',
        favorite.id,
        favorite.barber_profile_id
    FROM favorite_barbers AS favorite
    LEFT JOIN barber_profiles AS profile ON profile.id = favorite.barber_profile_id
    WHERE profile.id IS NULL

    UNION ALL

    SELECT
        'users.barber_profile_id -> barber_profiles.id',
        u.id,
        u.barber_profile_id
    FROM users AS u
    LEFT JOIN barber_profiles AS profile ON profile.id = u.barber_profile_id
    WHERE u.barber_profile_id IS NOT NULL
      AND profile.id IS NULL

    -- Links that intentionally have no FK (booking history must survive catalogue
    -- deletion), but are still checked by CreateBookingHandler at write time.
    UNION ALL

    SELECT
        'bookings.salon_id -> salons.id',
        booking.id,
        booking.salon_id
    FROM bookings AS booking
    LEFT JOIN salons AS salon ON salon.id = booking.salon_id
    WHERE salon.id IS NULL

    UNION ALL

    SELECT
        'bookings.barber_profile_id -> barber_profiles.id',
        booking.id,
        booking.barber_profile_id
    FROM bookings AS booking
    LEFT JOIN barber_profiles AS profile ON profile.id = booking.barber_profile_id
    WHERE profile.id IS NULL

    UNION ALL

    SELECT
        'bookings.barber_service_id -> barber_services.id',
        booking.id,
        booking.barber_service_id
    FROM bookings AS booking
    LEFT JOIN barber_services AS service ON service.id = booking.barber_service_id
    WHERE service.id IS NULL

    UNION ALL

    SELECT
        'bookings.user_id -> users.id',
        booking.id,
        booking.user_id
    FROM bookings AS booking
    LEFT JOIN users AS u ON u.id = booking.user_id
    WHERE u.id IS NULL

    UNION ALL

    SELECT
        'reviews.booking_id -> bookings.id',
        review.id,
        review.booking_id
    FROM reviews AS review
    LEFT JOIN bookings AS booking ON booking.id = review.booking_id
    WHERE booking.id IS NULL

    UNION ALL

    SELECT
        'reviews.user_id -> users.id',
        review.id,
        review.user_id
    FROM reviews AS review
    LEFT JOIN users AS u ON u.id = review.user_id
    WHERE u.id IS NULL

    UNION ALL

    SELECT
        'reviews.barber_profile_id -> barber_profiles.id',
        review.id,
        review.barber_profile_id
    FROM reviews AS review
    LEFT JOIN barber_profiles AS profile ON profile.id = review.barber_profile_id
    WHERE profile.id IS NULL

    UNION ALL

    SELECT
        'barber_profiles.user_id -> users.id',
        profile.id,
        profile.user_id
    FROM barber_profiles AS profile
    LEFT JOIN users AS u ON u.id = profile.user_id
    WHERE u.id IS NULL
)
SELECT
    reference,
    missing_id,
    count(*) AS dangling_count,
    min(row_id::text) AS first_row_id
FROM dangling
GROUP BY reference, missing_id
ORDER BY reference, missing_id;

-- After cleaning every dangling row, validate the new constraints. Each command
-- takes SHARE UPDATE EXCLUSIVE, so it does not block reads and writes for long.
--
-- ALTER TABLE barber_salons
--     VALIDATE CONSTRAINT fk_barber_salons_salons_salon_id;
-- ALTER TABLE barber_services
--     VALIDATE CONSTRAINT fk_barber_services_hair_services_hair_service_id;
-- ALTER TABLE barber_specializations
--     VALIDATE CONSTRAINT fk_barber_specializations_specializations_specialization_id;
-- ALTER TABLE favorite_barbers
--     VALIDATE CONSTRAINT fk_favorite_barbers_barber_profiles_barber_profile_id;
-- ALTER TABLE users
--     VALIDATE CONSTRAINT fk_users_barber_profiles_barber_profile_id;
