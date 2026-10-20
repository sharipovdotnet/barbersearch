-- Emits the load-harness manifest as one JSON document on stdout.
-- Run after reset-scenarios.sql and after the API users have been created.
--
--   psql -h 127.0.0.1 -p 5432 -U postgres -d barbersearch_load -tA \
--        -f load/seed/manifest.sql > load/generated/manifest.json

\set ON_ERROR_STOP on
SET TIME ZONE 'UTC';

WITH days AS (
    SELECT date_trunc('day', now()) AS today
),
boundaries AS (
    SELECT today,
           today + interval '7 days'
               + (((3 - extract(dow FROM today + interval '7 days')::int) + 7) % 7)
                 * interval '1 day' AS next_wednesday
    FROM days
)
SELECT jsonb_build_object(
    'generatedAt', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
    'searchDate', to_char(next_wednesday, 'YYYY-MM-DD'),
    'slotsDate', to_char(next_wednesday + interval '7 days', 'YYYY-MM-DD'),
    'cities', (
        SELECT jsonb_agg(city ORDER BY city)
        FROM (SELECT DISTINCT city FROM salons WHERE is_active) AS c
    ),
    'specializationIds', (
        SELECT jsonb_agg(md5('specialization:' || k)::uuid ORDER BY k)
        FROM generate_series(1, 10) AS k
    ),
    'serviceIds', (
        SELECT jsonb_agg(md5('hair-service:' || k)::uuid ORDER BY k)
        FROM generate_series(1, 20) AS k
    ),
    'barbers', (
        SELECT jsonb_agg(
                   jsonb_build_object(
                       'id', md5('barber:' || b)::uuid,
                       'hairServiceId', md5('hair-service:' || (((b - 1) % 20) + 1))::uuid,
                       'salonId', md5('salon:' || (((b - 1) % 400) + 1))::uuid)
                   ORDER BY b)
        FROM generate_series(1, 1000) AS b
    ),
    'contentionReviewBarbers', (
        SELECT jsonb_agg(
                   jsonb_build_object(
                       'barberId', md5('barber:' || b)::uuid,
                       'reviews', (
                           SELECT jsonb_agg(
                                      jsonb_build_object(
                                          'id', r.id::text,
                                          'rating', r.rating,
                                          'initiallyPublished', r.is_published)
                                      ORDER BY r.id)
                           FROM reviews AS r
                           WHERE r.barber_profile_id = md5('barber:' || b)::uuid
                       ),
                       'expectedCount', (
                           SELECT count(*) FROM reviews AS r
                           WHERE r.barber_profile_id = md5('barber:' || b)::uuid
                             AND NOT r.is_published
                       ),
                       'expectedRatingSum', (
                           SELECT coalesce(sum(r.rating), 0) FROM reviews AS r
                           WHERE r.barber_profile_id = md5('barber:' || b)::uuid
                             AND NOT r.is_published
                       ),
                       'expectedReviewIds', (
                           SELECT jsonb_agg(r.id::text ORDER BY r.id) FROM reviews AS r
                           WHERE r.barber_profile_id = md5('barber:' || b)::uuid
                             AND NOT r.is_published
                       ))
                   ORDER BY b)
        FROM (VALUES (1), (4), (5), (8)) AS v(b)
    ),
    'createReviewBarbers', (
        SELECT jsonb_agg(
                   jsonb_build_object(
                       'barberId', md5('barber:' || b)::uuid,
                       'bookingIds', (
                           SELECT jsonb_agg(md5('booking:' || b || ':' || n)::uuid ORDER BY n)
                           FROM generate_series(8, 57) AS n
                       ),
                       'basePublishedCount', (
                           SELECT count(*) FROM reviews AS r
                           WHERE r.barber_profile_id = md5('barber:' || b)::uuid
                             AND r.is_published
                       ),
                       'baseRatingSum', (
                           SELECT coalesce(sum(r.rating), 0) FROM reviews AS r
                           WHERE r.barber_profile_id = md5('barber:' || b)::uuid
                             AND r.is_published
                       ),
                       'newRating', 5)
                   ORDER BY b)
        FROM (VALUES (2), (6), (7), (9)) AS v(b)
    ),
    'contendedBooking', jsonb_build_object(
        'barberId', md5('barber:3')::uuid,
        'barberServiceId', md5('barber-service:3:0')::uuid,
        'salonId', md5('salon:3')::uuid,
        'slots', (
            SELECT jsonb_agg(
                       jsonb_build_object(
                           'startAt', to_char((today + ((29 + n) * interval '1 day')
                                              + interval '10 hours') AT TIME ZONE 'UTC',
                                              'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
                           'endAt', to_char((today + ((29 + n) * interval '1 day')
                                            + interval '10 hours 30 minutes') AT TIME ZONE 'UTC',
                                            'YYYY-MM-DD"T"HH24:MI:SS"Z"'))
                       ORDER BY n)
            FROM generate_series(1, 20) AS n
        )
    )
)
FROM boundaries;

