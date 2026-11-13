# issue-94 — «Расписание без сюрпризов»: 409 со списком визитов и честный поиск по дате

Ветка `features/issue-94` от `origin/main` (`b95b730`, merge #93). Backend + frontend в одном PR.

## 1. Итог

Пишущие ручки расписания больше не принимают молча правку, которая оставляет активный
будущий визит вне рабочего времени: они отвечают `409 barberSchedule.conflict` и списком
затронутых визитов, а осознанное «всё равно сохранить» — явный `?force=true`.

Поиск `GET /api/barbers?date=` больше не приравнивает дату к дню недели: учитываются
исключения (`ScheduleOverride`) и отпуск, накрывающий весь локальный день основного салона.

Миграций нет: `has-pending-model-changes` пуст, все таблицы и индексы существовали.
Контракты `GET .../schedule`, `GET .../slots` и публичные DTO не менялись.

## 2. Backend

- `Application/BarberProfiles/ScheduleConflicts/`: `ScheduleDayWindow`, `ScheduleConflictBooking`,
  `ScheduleConflictDetector` (чистые `FindOutsideHours` / `FindInsideTimeOff`),
  `ScheduleWindowFactory` (горизонт 60 дней; даты, перекрытые исключением, выбрасываются),
  `ScheduleConflictCheck` (чтение активных броней + фильтр `StartAt > now`), `ScheduleWriteOutcome`.
- `Contracts/BarberProfiles/BarberSchedule/ScheduleConflictResponse.cs` — только id, время, пояс,
  имя, услуга, статус; ни телефона, ни email, ни `userId`.
- Пять обработчиков (неделя, часы, выходной, исключение, отпуск): `IUnitOfWork` +
  `AcquireBarberLockAsync` (перегрузка с кодом `barberSchedule.lockTimeout`) →
  `GetWithScheduleAsync` **после** блокировки → `EnsureCanManage` → (если не `force`) чтение
  броней и проверка → запись. Проверяются только изменяемые дни; `force` не читает брони вовсе.
- `IBookingRepository.GetActiveForScheduleAsync` — плоская проекция: `users` LEFT, услуга,
  салон (`time_zone_id`), имя через `BuildCustomerName`, «Гость» при пустом имени.
- `FailureProblemDetails.CreateConflictResult` — второй фабричный метод: тот же заголовок 409 и
  `errorCodes`, плюс расширение `conflicts`. Успех остаётся `204`.
- `ApiErrorCodes.Schedule.{Conflict,LockTimeout}`, `Constants.SCHEDULE_CONFLICT_HORIZON_DAYS = 60`.
- Поиск по дате: предикат «эффективный день рабочий хотя бы в одном активном салоне, исключение
  важнее шаблона»; `GetDayBlockedProfileIdsAsync` читает отпуска (`time_offs` → основной
  `barber_salons` → `salons`) и считает границы дня в C#.
- Пять экшенов принимают `?force`; невалидное значение (`?force=maybe`) трактуется как `false`,
  отдельного `400` нет.

## 3. Frontend

- `ProblemDetails.conflicts?: unknown` + строгая проверка формы в `isProblemDetails`;
  `ScheduleConflict`, `readScheduleConflicts`, guard `isScheduleConflicts`.
- `useScheduleWrite` — один контроллер на три карточки: первая попытка `force=false`, `409`
  с читаемым списком открывает диалог, повтор с `force=true`, повторный `409` обновляет список
  и оставляет диалог открытым, прочие ошибки — текст в карточке.
- `ScheduleConflictsDialog` — `role="alertdialog"`, монтируется на открытие, до 20 строк +
  «и ещё N», `autoFocus` на «Отмена», «Всё равно сохранить» — не `destructive`.
- Три карточки (неделя, исключения, отпуска) подключены к хуку; подсказка в недельной карточке
  заменена на честную и добавлена в две другие.
- `DELETE` исключения/отпуска и снятие выходного не проверяются — снятие ограничения не может
  оставить визит вне часов.

## 4. Расхождения кода и текста этапа

Зафиксированы по правилу «прав код»:

1. `POST .../time-offs` отвечает `204`, а не `201` (§5 п.4): так было до этапа, `§3.3` сам
   требует «успех остаётся 204». Тесты утверждают `204`.
2. `force` принимается строкой (`/api/...?force=true`) и разбирается `ApiController.ReadForce`,
   а не биндером `bool`: биндер на `?force=maybe` отвечает `400`, а `§3.4` требует трактовать
   невалидное значение как `false`.
3. `CreateConflictResult` получил второй (необязательный) параметр `insideTimeOff`: текст
   `detail` для отпуска отличается от текста для часов.
4. `ScheduleConflictDetector` принимает необязательный `ILogger`: иначе `LogWarning`/`LogError`
   на DST-дыре и неразрешимом поясе не покрыть unit-тестом.
5. В репозитории остался фильтр ровно `StartAt < to && from < EndAt`, а «только будущие визиты»
   (`StartAt > now`, D5) применяется в Application до детектора.
6. `IUnitOfWork.ExecuteAsync<T>` уже существовал — интерфейс не менялся.
7. `AdditionalServiceSearchQueryTests` не содержал кейса для ветки `Date` — он добавлен, а не
   обновлён.
8. `§4.1` и `§4.4` противоречат друг другу насчёт текста «409 без списка»: `describeScheduleError`
   код `barberSchedule.conflict` не обрабатывает (как требует `§4.4`), а общий текст выдаёт
   `useScheduleWrite`.
9. `useScheduleWrite` принимает `kind`, но не использует его: заголовок диалога рендерит карточка,
   поэтому параметр назван `_kind` (`noUnusedParameters`).
10. `readScheduleConflicts` возвращает `null` и на пустой массив `conflicts`: диалог «0 записей»
    бессмысленен, это тот же общий текст «обновите страницу».

## 5. Evidence

### 5.1 Сырое тело `409`

`PUT /api/barber-profiles/{id}/salons/{salonId}/working-schedules` с часами 12:00–18:00 без
`force`; активная бронь 10:00 Europe/Moscow на завтра (сырой JSON, снят с работающего API):

```json
{"title":"The request conflicts with the current state of the resource.","status":409,"detail":"1 booking falls outside the new working hours.","errorCodes":["barberSchedule.conflict"],"conflicts":[{"id":"01a1d1b7-429e-71a9-b9cd-da9cd8e5377e","startAt":"2026-11-14T07:00:00+00:00","endAt":"2026-11-14T07:30:00+00:00","timeZoneId":"Europe/Moscow","customerName":"E2E Raw409","serviceName":"E2E Стрижка","status":"Confirmed"}]}
```

Поля и порядок совпадают с `§3.3`. После снятия тела бронь отменена, неделя восстановлена
через `?force=true`, сид применён заново.

### 5.2 Планы запросов (`EXPLAIN (ANALYZE, BUFFERS)`)

Полные распечатки — `issue-94-explain-search.txt`, `issue-94-explain-timeoffs.txt`. Замеры на
отдельной базе `barbersearch_load` того же контейнера (12 000 профилей, 400 салонов,
168 000 `working_schedules`, 4 000 исключений, 1.2M броней); рабочая база `barbersearch` пуста,
поэтому на ней чисел нет.

Поиск по дате, как его генерирует EF (`filter.Date`, `ORDER BY (rating IS NULL), rating DESC, id`,
`LIMIT 21`):

```
Limit  (cost=0.85..952.59 rows=21 ...) (actual time=0.287..1.250 rows=21 loops=1)
->  Index Scan using ix_barber_profiles_search_keyset on barber_profiles b  (actual time=0.071..0.195 rows=21)
->  Index Scan using ux_barber_salons_barber_profile_id_salon_id
->  Index Scan using ux_schedule_overrides_barber_salon_id_date  (rows=0, loops=21)
->  Index Scan using ux_working_schedules_barber_salon_id_day_of_week
Planning Time: 4.940 ms
Execution Time: 1.461 ms
```

С непустым `blockedProfileIds` (`NOT (… ANY (@p))`) — `Execution Time: 0.550 ms`, тот же
`ix_barber_profiles_search_keyset` перед `LIMIT 21`. Новый `EXISTS` по двум таблицам план
не сломал: keyset-скан останавливается на 21 строке.

Чтение отпусков (`time_offs` → основной `barber_salons` → `salons`): `Hash Join`,
`Execution Time: 4.941 ms`, 600 строк `time_offs` и 12 000 связей — таблицы маленькие,
`seq scan` + hash join дешевле индекса. Чтение активных броней барбера для проверки идёт
по `ix_bookings_barber_profile_id_start_at_active` (0.8 ms).

Индексы не добавлялись: чисел, которые бы этого требовали, нет.

### 5.3 Тесты

Backend (`issue-94-backend-tests.txt`):

| Команда | Результат |
|---|---|
| `dotnet build BarberSearch/BarberSearch.sln --nologo` | 0 предупреждений, 0 ошибок |
| `dotnet test tests/BarberSearch.Domain.UnitTests` | 588 passed |
| `dotnet test tests/BarberSearch.Application.UnitTests` | 907 passed |
| `dotnet test tests/BarberSearch.Infrastructure.UnitTests` | 348 passed |
| `dotnet test tests/BarberSearch.IntegrationTests` | 303 passed, 4 m 22 s |
| `dotnet ef migrations has-pending-model-changes` | `No changes have been made to the model since the last migration.` |

В Application добавлены `ScheduleConflictDetectorTests` и `ScheduleConflictHandlerTests`,
в Infrastructure — кейсы `Date` и `blockedProfileIds` в `AdditionalServiceSearchQueryTests`,
в Integration — `ScheduleConflictApiTests` (10 сценариев: сырое тело 409, `force` → `204`,
живая бронь, `/slots`, горизонт 60 против явной даты на 75-й день, гость без фамилии, поиск).

Оговорка среды: `dotnet test` без переопределения `BaseOutputPath` падает на старте `testhost`
(`Win32Exception (5)`, см. backend/AGENT.md §5.2), поэтому прогоны шли с
`-p:BaseOutputPath=$env:TEMP\bs-tests\<Project>\`.

Frontend (`issue-94-frontend-tests.txt`):

```
npm run lint                            Found 0 warnings and 0 errors (525 files)
npm test                                139 files / 1467 tests passed
npm run build                           tsc -b clean, ✓ built in 6.31s
npm run check:i18n -- --strict          t():1742, переводов 2056, нет перевода: 0, конфликтов: 0
npx tsc -p tsconfig.e2e.json --noEmit   exit 0
```

Новых Vitest-сьютов пять: `use-schedule-write`, `schedule-conflicts-dialog` и по одному на
карточку (неделя, исключения, отпуска).

E2E (`issue-94-e2e-full-4.txt`), полный прогон `npm run test:e2e` против `barbersearch_e2e`:

```
312 passed, 6 skipped (3.9m), 0 failed
```

Флаки, найденный по дороге (не этот срез): два первых полных прогона падали по одному тесту —
`email-change.spec.ts` (`page.goto: net::ERR_ABORTED` в момент редиректа после входа) и
`admin-queue-mutations.spec.ts` (в `finally` `DELETE /api/reviews/{id}/publication` отвечал `404`).
Второй воспроизводился в полном прогоне и проходил в одиночку: обе сцены файла читают и публикуют
первую строку одной очереди, а `fullyParallel: true` запускал их конкурентно — одна публиковала
строку, id которой уже запомнила другая. Файл переведён на `test.describe.serial` (остальные
мутационные сцены последовательны по той же причине), после чего полный прогон зелёный.

Мутационный сценарий `e2e/specs/barber-schedule-conflicts-mutations.spec.ts` (отдельный
`e2e-schedule-barber`, сид сбрасывает его неделю, брони, исключения и отпуска):
гостевая бронь на завтра 10:00 → часы 12:00–18:00 → диалог с визитом → «Отмена» → после
перезагрузки часы прежние → Escape закрывает диалог и возвращает фокус → «Всё равно сохранить» →
«Расписание сохранено.», `/barber` показывает визит живым; axe на открытом диалоге 1280 и 320,
на 320 нет горизонтального скролла; `finally` отменяет брони и возвращает неделю через API.

Найденный сценарием дефект: кнопка сохранения `disabled` на время запроса, поэтому к моменту
открытия диалога Base UI не помнил «предыдущий» элемент и фокус после Escape уходил в `body`.
Исправлено штатным `finalFocus` Base UI: карточки передают ссылку на свою кнопку, `ref.focus()`
вручную не вызывается, `src/components/ui/**` не правился.

Отдельный фикстурный барбер (`e2e-schedule-barber`, `Europe/Moscow`, неделя 09:00–18:00,
`is_active=false` + `is_verified=true`, чтобы не сдвигать счётчики поиска и сводки) живёт в
`e2e/fixtures/accounts.ts` и `seed.sql`; сид на каждом прогоне сбрасывает его неделю, брони,
исключения и отпуска. Витринный `barber` не мутируется: у него есть записи в горизонте, и любой
сценарий, который жмёт «Сохранить расписание», получил бы `409`.

### 5.4 Скриншоты диалога

- `issue-94-dialog-1280.png` — 1280×900: заголовок «Записи вне новых часов», «1 запись…»,
  строка визита в поясе салона, «Отмена» / «Всё равно сохранить».
- `issue-94-dialog-320.png` — 320×800: тот же диалог, кнопки в столбик, горизонтального
  скролла нет.

## 6. Что осталось (долг)

- Несколько интервалов в день, обед и split shift — по-прежнему вне среза (D16).
- Отпуск исключает барбера из поиска по поясу **основного** салона (D10): многозонный барбер
  на краях суток может отличаться. Записано в backend `§9.13` и frontend `§14 #28`.
- Клиентов не уведомляем: UI честно говорит, что визит остаётся в силе (D15).
- Побочное наблюдение, не этот срез: `POST /api/barber/bookings` с не-UTC `DateTimeOffset`
  (`+03:00`) отвечает `500` (`Npgsql: only offset 0 (UTC) is supported`) вместо `400`.
  Клиент всегда шлёт `toISOString()`, поэтому на срез не влияет.
- Среда: на хосте на `localhost:5432` слушает нативный PostgreSQL, а контейнер
  `barbersearch-db` публикует тот же порт. Замеры и сид этапа сделаны через `docker exec`;
  первый `dotnet ef database update` ушёл в нативный сервер и обновил схему его
  `barbersearch_load` (данные не тронуты).
