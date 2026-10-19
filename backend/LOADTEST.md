# LOADTEST — baseline 2026-10-19

Нагрузочный baseline на реалистичном объёме для `BarberSearch` (ветка
`features/issue-4`). Цель — измерить текущую реализацию **до** кэша и
оптимизаций, сравнить с бюджетом `AGENT.md` §15.7 и зафиксировать узкие места.

Кода приложения в этом срезе не оптимизировали. Единственное изменение
поведения — исправление корректности contended booking: 50 одновременных
вставок на один слот могли отдать `40P01` (deadlock внутри GiST
exclusion-constraint) и 500, а под нагрузкой — вообще ни одного `201`. Подробности
в разделе «Найденный дефект».

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
| Отзывы | 80 036, из них 60 000 опубликованных |
| Пользователи | 2 000 клиентов + 12 000 владельцев-барберов + 2 login-пользователя |
| Время генерации | ~150 с |

Активные брони (status 1/2) — по 3 на барбера на разных будущих днях, поэтому
`ex_bookings_no_overlap` не ломается. Для review-сценариев выделены отдельные
барберы: 4 с 20 отзывами (6 опубликованных / 14 скрытых) и 4 с 50 завершёнными
бронями без отзывов.

## 3. Сценарии и бюджеты

Бюджеты зафиксированы **до** прогона и не менялись после.

| Сценарий | Что делается | Бюджет |
|---|---|---|
| search | `GET api/barbers`, микс city / specializationId / serviceId / date / cursor / pageSize, 16 клиентов | p95 < 200 ms, p99 < 500 ms (`AGENT.md` §15.7) |
| slots | `GET api/barbers/{id}/slots?date=...` для 1 000 случайных барберов, 16 клиентов | p95 < 300 ms, p99 < 800 ms (задан до прогона) |
| booking-contended | 50 одновременных `POST api/bookings` на один слот, разные `idempotencyKey` | p95 < 300 ms (`AGENT.md` §15.7); ровно 1 × 201 и 49 × 409, без 500 |
| review-publish-hide | 20 одновременных Publish/Hide отзывов одного барбера, 3 разных барбера | p95 < 500 ms, p99 < 1000 ms (задан до прогона) |
| review-create | 50 одновременных `POST api/reviews` по завершённым броням одного барбера, 3 разных барбера | p95 < 500 ms, p99 < 1000 ms (задан до прогона) |

Перед каждым измеряемым окном — прогрев: 5 с для search/slots, отдельные
warm-up-барберы/слоты для write-сценариев. Каждый сценарий повторён 3 раза;
ниже показан разброс, а не одно удачное число. HTTP-клиент — 16 параллельных
воркеров для read-сценариев; write-сценарии — 20/50 одновременных запросов.

## 4. Результаты

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

### 7.1 Search: seq scan + sort вместо частичного индекса

10 % микса search — запрос только с `date`. Именно он даёт хвост p95.
`EXPLAIN (ANALYZE, BUFFERS, TIMING OFF)` для этого запроса (day_of_week=3,
LIMIT 101):

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
Planning Time: 20.748 ms
Execution Time: 78.639 ms
Buffers: shared hit=2088
```

Причина: EF сортирует `ORDER BY (rating IS NULL), rating DESC, id`, а частичный
индекс `ix_barber_profiles_search` построен как `(rating DESC NULLS LAST, id)`.
Планировщик не использует его для выражения `rating IS NULL` и сортирует все
10 000 видимых профилей, а уже потом проверяет EXISTS по 101 строке. Это не
«мало данных» — это несовпадение порядка с индексом. Оптимизация (упростить
ORDER BY или добавить совместимый индекс) сознательно не делалась в этом слоте.

### 7.2 Booking: очередь advisory lock

Во время contended booking `pg_waiting_locks` достигал **48** ожидающих,
`pg_conn_total` — 51 при пуле 100. Это ожидаемая цена сериализации, а не утечка
пула. p95 250.9–298.4 ms — почти весь бюджет.

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

- **slots** — да, с большим запасом (p95 ~13 ms). Кэш не нужен.
- **review publish/hide и create** — да, p95 85–266 ms при бюджетах 500/1000 ms.
  Кэш не нужен; запись и так сериализуется row lock-ом профиля.
- **booking contended** — корректность обеспечена (1 × 201 / 49 × 409, одна
  строка в БД), p95 250.9–298.4 ms при бюджете 300 ms. Кэш здесь запрещён;
  запас исчерпан из-за advisory lock. Следующий шаг — не кэш, а slot-claim
  (уникальная строка/индекс) или fast-path, если нужен запас.
- **search** — пограничный: медиана p95 181.5 ms проходит, худший прогон
  202.4 ms — нет. Узкое место — план (seq scan + sort), а не объём индексов.
  Сначала стоит сделать ORDER BY совместимым с `ix_barber_profiles_search`
  (или добавить эквивалентный индекс) и перемерить. Кэш поиска (output cache
  1–5 min) даст запас, но маскирует план и добавляет инвалидацию при
  publish/hide отзыва; вводить его раньше оптимизации плана нерационально.

Итог: **широкий кэш измерениями не оправдан**. Оправдана точечная оптимизация
search-плана; после неё — повторный load test. Если после этого p95 всё ещё
близко к 200 ms, тогда короткий output cache на search. Деплой — после этого.

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
  -Repeat 3 -ReadSeconds 15

# повторно в течение 15 минут, не тратя auth rate limit
powershell -NoProfile -ExecutionPolicy Bypass -File load/run.ps1 `
  -Repeat 3 -ReadSeconds 15 -SkipAuth
```

Результаты: `load/generated/results.json`, `metrics.csv`,
`booking-verification.txt`. Каталог не коммитится.

## 10. Ограничения измерения

- API, PostgreSQL и load generator — на одной машине; клиент конкурирует за CPU.
- `synchronous_commit=on`, локальный диск; цифры несопоставимы с продакшеном
  напрямую.
- Нет `pg_stat_statements` и GC/thread-pool счётчиков (см. §7.3).
- 3 прогона по 15 с — достаточный baseline, но не замена CI-бенчмарку; при
  изменениях search/booking повторить и сравнить распределение, а не одно число.
