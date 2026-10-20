-- Restores only the rows mutated by the load scenarios, without touching the
-- 1.2M-row baseline. Run this before repeating the review/booking scenarios on an
-- already seeded barbersearch_load database.
--
-- It is idempotent.

\set ON_ERROR_STOP on
SET TIME ZONE 'UTC';

BEGIN;

-- 1. Publish/hide scenario: barbers 1, 4, 5 (measured) and 8 (warm-up).
--    Initial state: reviews n=0..5 published, n=6..19 hidden.
UPDATE reviews AS r
SET is_published = (x.n < 6),
    updated_at = now()
FROM (
    SELECT b, n, md5('review:' || b || ':' || n)::uuid AS review_id
    FROM generate_series(1, 8) AS b
    CROSS JOIN generate_series(0, 19) AS n
    WHERE b IN (1, 4, 5, 8)
) AS x
WHERE r.id = x.review_id;

DELETE FROM reviews
WHERE booking_id IN (
    SELECT md5('booking:' || b || ':' || n)::uuid
    FROM generate_series(2, 9) AS b
    CROSS JOIN generate_series(8, 57) AS n
    WHERE b IN (2, 6, 7, 9)
);

-- 2. Contended booking: remove the active rows created by the harness. The
--    idempotency key prefix is reserved for this scenario.
DELETE FROM bookings
WHERE idempotency_key LIKE 'load-contended-%';

-- Same-key idempotency scenario: one winner row plus the post-cancellation row per
-- repeat. The cancelled winner is also removed, so reset itself does not leave a
-- cancelled row holding the slot.
DELETE FROM bookings
WHERE idempotency_key LIKE 'load-idem-%';

-- 3. Recompute the denormalized rating for every touched profile from the
--    current published reviews. Two statements keep the check constraint
--    (rating IS NULL) = (reviews_count = 0) valid at every step.
UPDATE barber_profiles
SET rating = NULL,
    reviews_count = 0,
    updated_at = now()
WHERE id IN (
    SELECT md5('barber:' || b)::uuid
    FROM generate_series(1, 9) AS b
    WHERE b IN (1, 2, 4, 5, 6, 7, 8, 9)
);

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
      AND barber_profile_id IN (
          SELECT md5('barber:' || b)::uuid
          FROM generate_series(1, 9) AS b
          WHERE b IN (1, 2, 4, 5, 6, 7, 8, 9)
      )
    GROUP BY barber_profile_id
) AS s
WHERE p.id = s.barber_profile_id;

COMMIT;

\echo '== scenario rows reset =='
