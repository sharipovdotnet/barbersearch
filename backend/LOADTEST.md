# LOADTEST — baseline 2026-10-19, follow-up 2026-10-20

Нагрузочный baseline на реалистичном объёме для `BarberSearch` (ветка
`features/issue-4`). Цель — измерить текущую реализацию **до** кэша и
оптимизаций, сравнить с бюджетом `AGENT.md` §15.7 и зафиксировать узкие места.

Baseline 2026-10-19 не менял код приложения; единственное изменение поведения —
исправление корректности contended booking: 50 одновременных вставок на один
слот могли отдать `40P01` (deadlock внутри GiST exclusion-constraint) и 500,
а под нагрузкой — вообще ни одного `201`. Подробности в разделе «Найденный
дефект».

Follow-up 2026-10-20 закрыл два пробела: (1) same-key идемпотентность после
advisory lock + explicit transaction теперь проверяется harness-сценарием и
SQL-ом; (2) search-план переведён на expression-индекс
`ix_barber_profiles_search_keyset`, после чего search перемерен 8 прогонами и
полным прогоном `Repeat=5`. Кэш не добавлялся.

## 1. Стенд

| Параметр | Значение |
|---|---|
| Машина | Intel Xeon E5-2670 v3, 12 ядер / 24 logical CPU, 16 GB RAM |
| ОС | Windows 10 Pro 19045 |
| .NET | SDK 10.0.303, runtime 10.0.11 |
| GC | Server GC (`System.GC.Server=true` в runtimeconfig) |
| API | `http://127.0.0.1:5243`, `ASPNETCORE_ENVIRONMENT=Development` |
| Пул Npgsql | `Maximum Pool Size=100`, `AddDbContextPool` |
| EF logging | `Microsoft.EntityFrameworkCore=Warning` (SQL не логируется в нагрузке) |
| PostgreSQL | 16.3, локально, порт 5432 |
| PostgreSQL settings | `shared_buffers=128MB`, `max_connections=100`, `effective_cache_size=4GB`, `work_mem=4MB`, `maintenance_work_mem=64MB`, `random_page_cost=4`, `max_parallel_workers_per_gather=2`, `synchronous_commit=on` |
| `shared_preload_libraries` | пусто (значит, `pg_stat_statements` не включён) |
| Load DB | `barbersearch_load`, 553 MB; база `barbersearch` не использовалась |
| Инструмент | свой .NET 10 `HttpClient` harness (`load/harness`), вне `.sln` |

Почему не k6/NBomber: `k6` в PATH отсутствует; NBomber нет в NuGet-кэше, а
restore с `nuget.org` в этой среде падает на SSL/package source mapping.
`dotnet-counters` установить по той же причине не удалось. Свой harness не
добавляет зависимостей и позволяет точно проверить статусы contended booking и
итоговый рейтинг.

## 2. Данные

Сгенерированы `load/seed/seed.sql` set-based SQL, без API. Генератор
идемпотентный: детерминированные UUID, `ON CONFLICT DO NOTHING` и маркеры в
`load_seed_state`. Повторный запуск не дублирует строки.

| Сущность | Количество |
|---|---:|
| Барберы | 12 000, из них 10 000 `is_active AND is_verified` |
| Салоны | 400 (20 городов × 20 салонов) |
| Связи барбер–салон | 24 000 |
| Рабочие расписания | 168 000 (7 дней × 24 000 связей) |
| Услуги барберов | 36 000 (3 на профиль) |
| Специализации барберов | 24 000 |
| Портфолио | 36 000 |
| Брони | 1 200 000 |
| — completed / cancelled / no-show / active | 840 000 / 240 000 / 84 000 / 36 000 |
| Отзывы | 80 060 (80 036 + 24 скрытых для барберов 10/11), из них 60 000 опубликованных |
| Пользователи | 2 000 клиентов + 12 000 владельцев-барберов + 2 login-пользователя |
| Время генерации | ~150 с |

Активные брони (status 1/2) — по 3 на барбера на разных будущих днях, поэтому
`ex_bookings_no_overlap` не ломается. Для review-сценариев выделены отдельные
барберы: 6 с 20 отзывами (6 опубликованных / 14 скрытых) и 6 с 50 завершёнными
бронями без отзывов; последний барбер в каждом списке — warm-up, поэтому
`Repeat=5` использует пять разных измеряемых барберов.

## 3. Сценарии и бюджеты

Бюджеты зафиксированы **до** прогона и не менялись после.

| Сценарий | Что делается | Бюджет |
|---|---|---|
| search | `GET api/barbers`, микс city / specializationId / serviceId / date / cursor / pageSize, 16 клиентов | p95 < 200 ms, p99 < 500 ms (`AGENT.md` §15.7) |
| slots | `GET api/barbers/{id}/slots?date=...` для 1 000 случайных барберов, 16 клиентов | p95 < 300 ms, p99 < 800 ms (задан до прогона) |
| booking-contended | 50 одновременных `POST api/bookings` на один слот, разные `idempotencyKey` | p95 < 300 ms (`AGENT.md` §15.7); ровно 1 × 201 и 49 × 409, без 500 |
| booking-idempotency | warm-up + 10 одновременных `POST api/bookings` с одним ключом; повтор; отмена; новая вставка на тот же слот | 10 × 201, один id, одна строка; повторный запрос — тот же id; после отмены — 201 |
| review-publish-hide | 20 одновременных Publish/Hide отзывов одного барбера, 5 разных барберов | p95 < 500 ms, p99 < 1000 ms (задан до прогона) |
| review-create | 50 одновременных `POST api/reviews` по завершённым броням одного барбера, 5 разных барберов | p95 < 500 ms, p99 < 1000 ms (задан до прогона) |

Перед каждым измеряемым окном — прогрев: 5 с для search/slots, отдельные
warm-up-барберы/слоты для write-сценариев. Baseline 2026-10-19 — 3 повтора;
follow-up 2026-10-20 — 5 повторов (search дополнительно 8 прогонов по 20 с).
Ниже показан разброс, а не одно удачное число. HTTP-клиент — 16 параллельных
воркеров для read-сценариев; write-сценарии — 10/20/50 одновременных запросов.

## 4. Результаты

### 4.1 Baseline 2026-10-19 (3 повтора)

`error rate` — доля неожидаемых статусов (для contended booking 409 — ожидаемый
результат, а не ошибка). Числа — миллисекунды, RPS — запросы/с.

| Сценарий | Прогон | Запросов | RPS | p50 | p95 | p99 | max | Ошибки | Бюджет |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| search | 1 | 4 694 | 312.8 | 31.6 | 202.4 | 269.4 | 955.5 | 0 | **FAIL** (p95 +2.4 ms) |
| search | 2 | 5 753 | 383.1 | 26.9 | 180.5 | 234.0 | 456.8 | 0 | PASS |
| search | 3 | 5 666 | 377.5 | 27.5 | 181.5 | 232.6 | 598.3 | 0 | PASS |
| slots | 1 | 24 800 | 1 651.2 | 9.2 | 13.6 | 17.4 | 45.2 | 0 | PASS |
| slots | 2 | 25 606 | 1 706.0 | 8.8 | 13.0 | 16.5 | 35.3 | 0 | PASS |
| slots | 3 | 24 871 | 1 657.7 | 9.1 | 13.5 | 17.2 | 43.7 | 0 | PASS |
| booking-contended | 1 | 50 | 158.3 | 162.2 | 298.4 | 315.1 | 315.1 | 0 (49 × 409) | PASS (p95 −1.6 ms) |
| booking-contended | 2 | 50 | 191.6 | 136.9 | 250.9 | 260.8 | 260.8 | 0 (49 × 409) | PASS |
| booking-contended | 3 | 50 | 181.4 | 138.8 | 264.1 | 274.1 | 274.1 | 0 (49 × 409) | PASS |
| review-publish-hide | 1 | 20 | 204.7 | 58.7 | 92.0 | 97.3 | 97.3 | 0 | PASS |
| review-publish-hide | 2 | 20 | 215.2 | 51.8 | 85.5 | 92.7 | 92.7 | 0 | PASS |
| review-publish-hide | 3 | 20 | 213.8 | 49.6 | 86.8 | 93.5 | 93.5 | 0 | PASS |
| review-create | 1 | 50 | 177.1 | 148.4 | 265.6 | 281.8 | 281.8 | 0 | PASS |
| review-create | 2 | 50 | 201.5 | 123.2 | 239.1 | 247.8 | 247.8 | 0 | PASS |
| review-create | 3 | 50 | 218.3 | 121.8 | 220.3 | 228.9 | 228.9 | 0 | PASS |

Сводка по разбросу p95:

| Сценарий | p95 min / median / max | p99 min / median / max | Вывод по бюджету |
|---|---|---|---|
| search | 180.5 / 181.5 / 202.4 | 232.6 / 234.0 / 269.4 | медиана проходит; худший прогон превышает p95 на 2.4 ms |
| slots | 13.0 / 13.5 / 13.6 | 16.5 / 17.2 / 17.4 | с большим запасом |
| booking-contended | 250.9 / 264.1 / 298.4 | 260.8 / 274.1 / 315.1 | проходит, запас 1.6 ms |
| review-publish-hide | 85.5 / 86.8 / 92.0 | 92.7 / 93.5 / 97.3 | с большим запасом |
| review-create | 220.3 / 239.1 / 265.6 | 228.9 / 247.8 / 281.8 | проходит |

Error rate во всех сценариях — **0 %**. Для contended booking каждый прогон дал
ровно `1 × 201` и `49 × 409`; `500` не было после исправления (см. ниже).

### 4.2 Search до/после индекса (2026-10-20)

Одинаковый микс, 16 клиентов, окно 20 с. «До» — 5 прогонов без нового индекса,
«после» — 8 прогонов с миграцией `AddSearchKeysetIndex`; числа — миллисекунды.

| Search | Прогон | Запросов | RPS | p50 | p95 | p99 | max | Ошибки |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| до | 1 | 6 569 | 328.1 | 30.4 | **202.8** | 276.9 | 843.5 | 0 |
| до | 2 | 7 881 | 393.7 | 26.2 | 170.6 | 202.9 | 516.8 | 0 |
| до | 3 | 7 685 | 384.0 | 26.8 | 180.9 | 219.1 | 383.3 | 0 |
| до | 4 | 7 661 | 382.9 | 26.7 | 174.1 | 234.4 | 373.9 | 0 |
| до | 5 | 7 071 | 353.3 | 28.8 | **199.9** | 249.2 | 548.9 | 0 |
| после | 1 | 13 876 | 693.1 | 20.2 | 40.6 | 50.6 | 663.6 | 0 |
| после | 2 | 19 773 | 988.0 | 12.9 | 32.7 | 37.9 | 51.8 | 0 |
| после | 3 | 20 559 | 1 027.4 | 12.0 | 32.2 | 37.0 | 63.5 | 0 |
| после | 4 | 21 608 | 1 079.3 | 11.3 | 30.8 | 36.0 | 60.1 | 0 |
| после | 5 | 19 838 | 991.4 | 12.6 | 32.6 | 37.6 | 354.9 | 0 |
| после | 6 | 20 299 | 1 014.4 | 12.2 | 31.9 | 37.3 | 114.4 | 0 |
| после | 7 | 21 622 | 1 080.4 | 11.4 | 30.3 | 34.9 | 146.3 | 0 |
| после | 8 | 22 241 | 1 111.4 | 11.1 | **29.8** | 34.4 | 49.5 | 0 |

| Search | p95 min / median / max | p99 min / median / max | RPS median | Бюджет |
|---|---|---|---:|---|
| до | 170.6 / 180.9 / **202.8** | 202.9 / 234.4 / 276.9 | 382.9 | **FAIL** (худший p95 +2.8 ms) |
| после | 29.8 / 32.1 / **40.6** | 34.4 / 37.1 / 50.6 | 1 020.9 | **PASS** (запас ≈ 5×) |

Первый прогон «после» содержит один холодный запрос (max 663.6 ms), но p95 всё
равно 40.6 ms. В полном прогоне `Repeat=5` search p95 28.5–31.5 ms,
p99 32.3–36.3 ms, RPS 986.6–1 172.6, ошибок 0.

### 4.3 Финальный прогон 2026-10-20 (`Repeat=5`, `ReadSeconds=15`)

| Сценарий | Повторов | RPS median | p50 min–max | p95 min / median / max | p99 min / median / max | Ошибки | Бюджет |
|---|---:|---:|---:|---|---|---:|---|
| search | 5 | 1 145.5 | 10.3–13.3 | 28.5 / 29.1 / 31.5 | 32.3 / 33.5 / 36.3 | 0 | **PASS** |
| slots | 5 | 1 951.3 | 7.8–8.5 | 10.2 / 10.5 / 11.6 | 12.0 / 12.3 / 14.2 | 0 | PASS |
| booking-contended | 5 | 185.0 | 134.2–155.9 | 257.5 / 257.8 / **304.0** | 267.5 / 269.8 / 314.0 | 0 | **FAIL** (1 прогон +4.0 ms) |
| booking-idempotency | 5 | 191.8 | 26.9–28.5 | 51.2 / 51.9 / 52.2 | 51.2 / 51.9 / 52.2 | 0 | PASS (n/a) |
| review-publish-hide | 5 | 218.4 | 47.3–64.7 | 82.4 / 85.8 / 98.3 | 86.7 / 91.4 / 102.2 | 0 | PASS |
| review-create | 5 | 247.8 | 106.7–157.7 | 189.3 / 193.2 / 269.8 | 196.5 / 201.2 / 280.3 | 0 | PASS |

Booking остаётся тем же сознательным trade-off, что и в baseline: advisory lock
сериализует вставки одного барбера. Корректность не изменилась (см. §5), но
запас p95 почти нулевой, а один прогон дал 304.0 ms при бюджете 300. Это
booking headroom, а не регрессия search; следующий срез для запаса —
slot-claim/уникальная строка или fast-path, не кэш.

## 5. Корректность

### 5.1 Contended booking

Каждый из 3 измеренных прогонов (и warm-up) дал ровно одну активную бронь на
слот. SQL-проверка после прогона:

```
run 1 slot 2026-11-18T10:00:00Z active_rows=1
run 2 slot 2026-11-19T10:00:00Z active_rows=1
run 3 slot 2026-11-20T10:00:00Z active_rows=1
active_rows_with_harness_keys=4 (1 warm-up + 3 measured)
```

Follow-up 2026-10-20 (`Repeat=5`): 6 contended slots (1 warm-up + 5 measured),
каждый `active_rows=1`, всего `load-contended-*` строк 6. Для idempotency-слотов
(5 штук) — `active_rows=1`, `cancelled_rows=2` (warm-up + отменённый победитель),
`load-idem-after-cancel-*` активны все 5, `load-idem-same-*` отменены все 5.
Подробная SQL-таблица — §5.3.

### 5.2 Review publish/hide и CreateReview

После каждого прогона harness сравнил:

- `GET api/barbers/{id}` — сохранённые `rating` / `reviewsCount`;
- `GET api/reviews?barberProfileId=...` — фактический публичный список, его
  количество, сумму и **точный набор id**;
- ожидаемое множество из манифеста.

Для publish/hide итогом всегда были 14 ранее скрытых отзывов (6 исходно
опубликованных скрывались), для create — 56 опубликованных (6 исходных + 50
новых). Независимая SQL-проверка после прогона:

| Барбер | stored rating | stored count | actual published | actual avg |
|---:|---:|---:|---:|---:|
| 1 | 3.79 | 14 | 14 | 3.79 |
| 2 | 4.82 | 56 | 56 | 4.82 |
| 4 | 3.64 | 14 | 14 | 3.64 |
| 5 | 3.57 | 14 | 14 | 3.57 |
| 6 | 4.89 | 56 | 56 | 4.89 |
| 7 | 4.86 | 56 | 56 | 4.86 |

Lost update не воспроизведён: сохранённый рейтинг и счётчик совпадают с
фактическим набором опубликованных отзывов во всех шести случаях.

### 5.3 Same-key booking idempotency (2026-10-20)

Каждый из 5 прогонов `booking-idempotency` (плюс warm-up на отдельном слоте):

```
batch statuses=201:10, distinctIds=1
sequential=201 sameId=True
cancel=204
afterCancel=201 newId=True
p95=51.2–52.2 ms
```

Независимая SQL-проверка после полного прогона:

| Проверка | Значение | Ожидание |
|---|---:|---:|
| `load-contended-*` rows | 6 | 1 warm-up + 5 measured |
| `load-idem-same-r*` rows | 5 | одна строка на прогон |
| из них cancelled | 5 | победитель отменён перед новой вставкой |
| `load-idem-after-cancel-r*` active | 5 | новая бронь на тот же слот |
| `load-idem-warmup-r*` cancelled | 5 | warm-up не держит слот |
| active rows на каждом contended-слоте | 1 | ровно 1 |
| active rows на каждом idempotency-слоте | 1 | ровно 1 (плюс 2 cancelled) |

Проверка 12 профилей отзывов (5 publish/hide + 5 create + 2 warm-up): сохранённые
`rating`/`reviews_count` совпали с фактическим набором опубликованных отзывов,
расхождений 0; `(rating IS NULL) <> (reviews_count = 0)` — 0 строк.

| Барбер | stored rating / count | actual published | match |
|---:|---:|---:|---|
| 1 | 3.79 / 14 | 14 | да |
| 2 | 4.82 / 56 | 56 | да |
| 4 | 3.64 / 14 | 14 | да |
| 5 | 3.57 / 14 | 14 | да |
| 6 | 4.89 / 56 | 56 | да |
| 7 | 4.86 / 56 | 56 | да |
| 8 | 3.50 / 14 | 14 | да |
| 9 | 4.86 / 56 | 56 | да |
| 10 | 3.71 / 14 | 14 | да |
| 11 | 3.79 / 14 | 14 | да |
| 12 | 4.82 / 56 | 56 | да |
| 13 | 4.56 / 16 | 16 | да |

## 6. Найденный дефект: deadlock в contended booking

Первый полный прогон (до исправления) на warm-up batch дал:

```
statuses=0:20, 500:30; createdIds=0
```

В логе API — `SqlState 40P01`, `при проверке ограничения-исключения ... "bookings"`,
`NpgsqlExecutionStrategy` → `InvalidOperationException`, `ExceptionHandlerMiddleware`
→ 500. Это не нехватка пула и не таймаут приложения: 50 одновременных INSERT в
один и тот же слот доводят GiST exclusion-check до взаимоблокировки; под
нагрузкой все 50 могут проиграть, и ни один `201` не создаётся.

Исправление (отдельный коммит, не оптимизация):

1. `BookingRepository.AddAsync` открывает явную транзакцию и берёт
   transaction-scoped advisory lock по `barber_profile_id`
   (`pg_advisory_xact_lock(hashtextextended(...))`) **до** вставки. Вставки одного
   барбера сериализуются; первый коммитит, остальные получают `23P01` → 409.
2. `40P01` дополнительно маппится в `409 booking.add.deadlock` — defensive path,
   чтобы будущая смена ключа блокировки не вернула 500.
3. Load-harness проверяет не только статусы, но и ровно одну активную строку в
   БД на слот.

Цена: per-barber сериализация; p95 вырос до 250.9–298.4 ms при бюджете 300 ms.
Это осознанный trade-off корректности; следующий срез может заменить его
slot-claim-строкой/уникальным индексом или fast-path проверкой, но не в этом
срезе.

## 7. Узкие места и доказательства

### 7.1 Search: seq scan + sort → expression-индекс

10 % микса search — запрос только с `date`. Именно он давал хвост p95.
`EXPLAIN (ANALYZE, BUFFERS, TIMING OFF)` для этого запроса (day_of_week=3,
LIMIT 101), **до** изменения (три прогретых прогона: 75.578 / 80.420 / 87.316 ms):

```
Limit
  -> Nested Loop Semi Join
       -> Gather Merge
            -> Sort  (Sort Key: ((b.rating IS NULL)), b.rating DESC, b.id)
                 -> Parallel Seq Scan on barber_profiles b
                      Filter: (is_active AND is_verified)
                      Rows Removed by Filter: 1000
       -> Nested Loop  (101 loops)
            -> Index Scan using ux_barber_salons_barber_profile_id_salon_id
            -> Index Scan using ux_working_schedules_barber_salon_id_day_of_week
            -> Index Scan using pk_salons
Planning Time: 19–22 ms
Execution Time: 75.578 / 80.420 / 87.316 ms
Buffers: shared hit=2088
```

Причина: EF сортирует `ORDER BY (rating IS NULL), rating DESC, id`, а частичный
индекс `ix_barber_profiles_search` построен как `(rating DESC NULLS LAST, id)`.
Планировщик не использует его для выражения `rating IS NULL` и сортирует все
10 000 видимых профилей, а уже потом проверяет EXISTS по 101 строке.

Выбран вариант **B** — additive expression index. Вариант A (`ORDER BY rating
DESC NULLS LAST, id` + курсор под null-группу) потребовал бы выразить
`NULLS LAST` в LINQ либо перейти на raw SQL и вручную переписать cursor-предикат;
это меняет контракт пагинации и null-группу ради того же плана. Индекс же
оставляет LINQ, курсор и порядок строк без изменений. Миграция
`AddSearchKeysetIndex`:

```sql
CREATE INDEX ix_barber_profiles_search_keyset
ON barber_profiles (((rating IS NULL)), rating DESC, id)
WHERE is_active AND is_verified;
```

План **после** (три прогона: 3.645 / 4.289 / 13.829 ms):

```
Limit
  -> Nested Loop Semi Join
       -> Index Scan using ix_barber_profiles_search_keyset on barber_profiles b
       -> Nested Loop  (101 loops)
            -> Index Scan using ux_barber_salons_barber_profile_id_salon_id
            -> Index Scan using ux_working_schedules_barber_salon_id_day_of_week
            -> Index Scan using pk_salons
Execution Time: 3.645 / 4.289 / 13.829 ms
```

Остальные два плана:

| Запрос | До | После |
|---|---|---|
| без фильтров, LIMIT 21 | Seq Scan + Sort: 8.152 / 8.567 / 9.477 ms | Index Scan: 0.054 / 0.068 / 0.130 ms |
| city + specializationId + serviceId + date, LIMIT 101 | Sort 50 строк: 10.210 / 10.214 / 11.871 ms | Sort 50 строк: 12.418 / 14.120 / 28.715 ms |

Комбинированный запрос по-прежнему сортирует 50 подходящих строк: индекс не
нужен и не помогает на таком маленьком наборе, это не хвост p95. Ключевой
результат — date-only перестал быть seq scan + sort. Семантика keyset-курсора и
порядок null-рейтингов не менялись; unit-тесты не добавлялись и не менялись.

### 7.2 Booking: очередь advisory lock

Во время contended booking `pg_waiting_locks` достигал **48** ожидающих,
`pg_conn_total` — 51 при пуле 100. Это ожидаемая цена сериализации, а не утечка
пула. Baseline p95 250.9–298.4 ms; follow-up 2026-10-20 (`Repeat=5`) — 257.5–304.0 ms.
Почти весь бюджет, один прогон вышел на 4 ms.

### 7.3 Ресурсы приложения и БД

| Метрика | Значение |
|---|---|
| App CPU (доля всех 24 logical CPU) | avg 14.69 %, max 32.55 % |
| App working set | min 131.2 MB, avg 191.6 MB, max 256.3 MB |
| PostgreSQL connections (load DB) | max 51, active max 49, idle max 50 |
| `pg_locks` not granted | max 48 (booking) |
| `xact_commit` за прогон | +1 175 658 |
| `blks_hit` / `blks_read` | +44 362 434 / +1 |
| `temp_bytes` | 0 |
| Npgsql pool | настроен 100; фактический максимум backend-соединений 51; исчерпания не видно |

Не удалось снять: CPU PostgreSQL (процессы не читаются через `Get-Process` —
access denied), `pg_stat_statements` (не включён в `shared_preload_libraries`),
GC gen2 / thread-pool queue length (`dotnet-counters` не установлен). Поэтому
нельзя утверждать, что хвост p95 — не GC/thread-pool; можно лишь сказать, что
пул не исчерпан, temp-файлов нет, а план запроса объясняет 78 ms на date-only.
Это ограничение стенда, а не вывод «GC ни при чём».

## 8. Вывод: достаточно ли текущей реализации?

- **slots** — да, с большим запасом (p95 10.2–11.6 ms). Кэш не нужен.
- **review publish/hide и create** — да, p95 82.4–98.3 и 189.3–269.8 ms при
  бюджетах 500/1000 ms. Кэш не нужен; запись и так сериализуется row lock-ом
  профиля.
- **booking contended** — корректность обеспечена (1 × 201 / 49 × 409, одна
  строка в БД в каждом из 5 прогонов), p95 257.5–304.0 ms при бюджете 300 ms;
  один прогон вышел за бюджет на 4 ms. Кэш здесь запрещён; запас исчерпан из-за
  advisory lock. Следующий шаг — не кэш, а slot-claim (уникальная строка/индекс)
  или fast-path, если нужен запас.
- **booking-idempotency** — 10 × 201 с одним id, повторный запрос с тем же
  ключом вернул тот же id, после отмены новая вставка на слот дала 201; SQL
  подтвердил одну строку на ключ и одну активную бронь на слот. p95 51.2–52.2 ms.
- **search** — план исправлен: expression-индекс
  `ix_barber_profiles_search_keyset` используется и date-only, и запросом без
  фильтров. p95 29.8–40.6 ms на 8 прогонах и 28.5–31.5 ms в полном `Repeat=5`,
  p99 ≤ 50.6 ms, ошибок 0. Бюджет p95 < 200 / p99 < 500 пройден с запасом ≈ 5×.
  Кэш поиска не нужен и не вводился.

Итог: **кэш измерениями не оправдан ни для одного сценария**. Search закрыт
индексом, слоты и отзывы имеют запас, booking ограничен корректностью advisory
lock, а не планом. Деплой-срез выполнен 2026-10-21 (`DEPLOY.md`: Data Protection,
`Auth__SigningKey` + ротация, `UseForwardedHeaders`, CORS + `SameSite=None`, health
checks); booking headroom — отдельно, если понадобится.

## 9. Как повторить

Полные команды — `load/README.md`. Кратко:

```powershell
# 1. отдельная БД + существующие миграции (см. README)
# 2. данные
& 'C:\Program Files\PostgreSQL\16\bin\psql.exe' -h 127.0.0.1 -p 5432 `
  -U postgres -d barbersearch_load -v ON_ERROR_STOP=1 -f load/seed/seed.sql

# 3. harness
dotnet build load/harness/LoadHarness.csproj -c Release

# 4. прогон: сам поднимет API, залогинится, сбросит сценарии, соберёт метрики
powershell -NoProfile -ExecutionPolicy Bypass -File load/run.ps1 `
  -Repeat 5 -ReadSeconds 15

# повторно, пока токены живы (15 минут), не тратя auth rate limit
powershell -NoProfile -ExecutionPolicy Bypass -File load/run.ps1 `
  -Repeat 5 -ReadSeconds 15 -SkipAuth

# только search, 8 окон по 20 с
powershell -NoProfile -ExecutionPolicy Bypass -File load/run.ps1 `
  -Scenario search -Repeat 8 -ReadSeconds 20

# минимальная same-key проверка без 1.2M seed (отдельная *_ci БД)
powershell -NoProfile -ExecutionPolicy Bypass `
  -File load/ci/booking-idempotency.ps1
```

Результаты: `load/generated/results.json`, `metrics.csv`,
`booking-verification.txt`. Каталог не коммитится.

## 10. Ограничения измерения

- API, PostgreSQL и load generator — на одной машине; клиент конкурирует за CPU.
- `synchronous_commit=on`, локальный диск; цифры несопоставимы с продакшеном
  напрямую.
- Нет `pg_stat_statements` и GC/thread-pool счётчиков (см. §7.3).
  `pg_stat_statements` не включали: это требует рестарта общего локального
  инстанса PostgreSQL, а не только записи в `shared_preload_libraries` для
  `barbersearch_load`; `dotnet-counters` по-прежнему не установлен (SSL/package
  source mapping).
- 5 прогонов по 15 с (search — 8 по 20 с) — достаточный baseline, но не замена
  CI-бенчмарку; при изменениях search/booking повторить и сравнить
  распределение, а не одно число.
