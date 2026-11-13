# issue-95 — «Перенос визита»: клиент и барбер двигают запись, а не отменяют её

## 1. Итог

Перенос визита появился как отдельная доменная операция: меняются только `StartAt`/`EndAt`,
услуга, салон, барбер, цена, статус и длительность остаются прежними. Клиент и барбер двигают
запись из своих кабинетов одним общим диалогом; администратор может вызвать ручку по API, но
кнопки в его интерфейсе нет.

- `POST api/bookings/{id}/reschedule` — `{ startAt }` → `204` без тела (как остальные переходы
  брони), ошибки — `booking.reschedule.*`, плюс остаточные `booking.update.overlap` /
  `booking.update.concurrencyConflict`.
- `GET api/bookings/{id}/reschedule-slots?date=` — существующий `BarberSlotsResponse`; сетка
  шагает по длительности **самой брони**, а не по текущему прайсу; своя бронь свой слот не
  занимает.
- Аудит: одна строка `booking.updated` с `startAt`/`endAt` (`from`/`to` в ISO). В журнале
  администратора время показывается в UTC с явной подписью.
- Писем, Telegram и push нет: клиент видит новое время в кабинете/баннером из опроса 30 с, и
  диалог барбера говорит об этом честно.
- Миграций и нового индекса нет: `start_at`/`end_at` и индексы `(barber_profile_id, start_at)` и
  GiST уже были. `dotnet ef migrations has-pending-model-changes` пуст; SQL не менялся по форме,
  поэтому `EXPLAIN` не снимался (записано в `backend/AGENT.md` §15.2).
- Публичные `/slots` и `/barber/slots` не получили нового параметра; `/privacy` не менялся —
  перенос уже назван в целях обработки (проверено: диффа нет).

## 2. Backend

- `Domain/Bookings/Booking.cs`: `Reschedule(startAt, endAt)` — только `Pending`/`Confirmed`
  (`409 booking.reschedule.status`), `endAt > startAt` (`400 booking.overlap.period`), присваивает
  оба поля и `MarkUpdated()`; больше ничего не трогает. Статус не меняется (D14).
- `Application/Common/SlotGrid.cs`: цикл сетки вынесен из `GetBarberSlotsHandler` в чистую
  функцию (часы дня, шаг длительности, отпуска, DST-дыра, дедупликация повторного часа).
  Публичные слоты переписаны на неё — поведение прежнее (регресс закрыт тестами слотов и E2E).
- `GetBookingRescheduleSlotsHandler` — права как у отмены (клиент-владелец, барбер, админ),
  порядок «дата → контекст дня → границы окна» соблюдён, длительность из брони, занятость дня
  без самой брони, границы дня через `SalonTimeZone.ToUtc` (`409 .slot` на дыре DST).
- `RescheduleBookingHandler` — `IUnitOfWork` → advisory-блокировка барбера
  (`booking.reschedule.lockTimeout`) → `GetByIdAsync` **после** блокировки → авторизация →
  проверки («сейчас», горизонт 90 дней, слот сетки, занятость) → `Reschedule` → `UpdateAsync`.
  Ключ блокировки читается узкой проекцией `GetBarberProfileIdAsync` (новый метод репозитория,
  `AsNoTracking`, отсутствие → `404 booking.notFound`).
- `BookingsController`: `POST {bookingId:guid}/reschedule` и `GET {bookingId:guid}/reschedule-slots`;
  `RescheduleBookingRequest { StartAt }`.
- `AuditTrail`: `StartAt`/`EndAt` в allowlist `Booking`, комментарий переписан, `AuditsCreation:
  false` сохранён.
- Тесты: домен (4 сценария), Application (права, окно/горизонт, часы/отпуска, занятость, порядок
  «блокировка → чтение», таймаут, `concurrencyConflict`), Infrastructure (проекция SQL и
  `notFound`), интеграционные 10 сцен (`TestApp`/реальный PostgreSQL).

## 3. Frontend

- Транспорт: `rescheduleBooking`, режим `bookingId` в `BarberSlotsParams` + путь
  `/bookings/{id}/reschedule-slots`, `RESCHEDULE_HORIZON_DAYS`, `salonDateOf`.
- `describeRescheduleFailure` — единый маппер кодов переноса и остаточных `booking.update.*`
  (`reloadSlots`), без английского `detail`.
- Новый общий `src/components/reschedule-booking-dialog.tsx`: обычный `role="dialog"`, нативный
  `input[type=date]` с `min`/`max`, тот же `BarberSlotOptions` (новый необязательный `legend`),
  `role="status"` с количеством слотов, `role="alert"` на ошибке, `isPending`, честная строка про
  цену/длительность и (для барбера) про письмо. Контент монтируется только при `target !== null`.
- Кабинет клиента: `canRescheduleBooking`, иконка `CalendarClock` в строке перед отменой,
  `reschedule`/`isRescheduling` в `useCustomerBookings` (`replaceItem` без `reload`, чтобы не
  объявить клиенту его собственное действие), гейты опроса; дифф «Предстоящих» сравнивает ещё и
  `startAt` → баннер «Барбер перенёс запись на …».
- Кабинет барбера: действие в строке ленты дня/«Ближайших»/«Истории», `reschedule` через
  существующий `mutate` (single-flight), маршрутизация в `ownerOf`, второй список не
  перечитывается; баннер «Клиент перенёс запись на …».
- Админка — только журнал: подписи «Начало»/«Окончание» и UTC-значение с суффиксом. Кнопки
  «Перенести» у администратора нет, существующие проверки её отсутствия зелёные.
- i18n: новые строки в `account.json`, `barber-pages.json`, `errors.json` (включая `aria-label` и
  `title`); `check:i18n --strict` — 0 пропусков и 0 конфликтов.
- Документация: `frontend/AGENT.md` §9.3, §13, §14 #30, §15, §16 п.13; `frontend/README.md`
  (Bookings, Booking, Admin bookings, Notifications); `backend/AGENT.md` §7, §8, §15.2, §16.

## 4. Расхождения кода и текста этапа

1. **Кнопка отправки — «Перенести визит», а не «Перенести».** Ключ `Перенести` уже занят
   импортом календаря (`barber-import.json`: `"Перенести": "Import"`), а `check:i18n` запрещает
   разные переводы одного ключа. Форма отказалась бы от «Import» или от «Reschedule»; выбран
   отдельный, более точный ключ. Ключ `Перенести запись?` из таблицы этапа добавлен в каталог,
   но в коде не используется (в диалоге заголовок «Перенести запись»).
2. **Интеграционный тест конкуренции** (§5 п.5 ожидал «один `204`, второй `409`»). С
   advisory-блокировкой (D8) попытки сериализуются: проигравший перечитывает уже сдвинутую бронь
   и может законно получить `204` (цель свободна) или no-op D15. Тест проверяет реальную
   гарантию: нет `500`, нет второй строки, финальное время — одно из двух; проброс
   `concurrencyConflict`/`overlap` покрыт юнит-тестами и сценой «цель занята → `409 .overlap`».
3. **E2E использует фикстуры `mutations` + B1.** У клиентского аккаунта `customer` барберы B2–B13
   не имеют недели в сиде, поэтому `reschedule-slots` вернул бы пустую сетку; B1 — единственный
   сидовый барбер с рабочими часами. Слот для `409` занимается переносом уже существующей строки
   (а не новой бронью), чтобы соседние мутационные спеки не увидели лишнюю строку; всё
   возвращается в `finally`.
4. **Диалог получил проп `variant: 'customer' | 'barber'`** — только ради честной строки про
   письмо, которую видит барбер. Второй копии диалога нет.
5. **Край ленты дня (известное ограничение).** Если клиент переносит визит на другой день, пока
   барбер стоит на вкладке «Сегодня», дифф ленты дня видит строку как удалённую и говорит
   «Запись отменена…»; список «Ближайшие» в этот момент показывает верное «Клиент перенёс…».
   Это следствие того, что лента собирается из двух списков и фильтрует по дате; отдельного
   требования к этому краю этап не содержит.
6. **`dotnet test` из рабочей папки в этой среде падает не кодом, а testhost:**
   `Win32Exception (5): Отказано в доступе` в `testhost` при `OpenProcess(parent)` — тесты не
   стартуют (0 тестов). Тот же DLL из `%TEMP%` проходит полностью, поэтому юнит-проекты сняты с
   `-p:BaseOutputPath=$env:TEMP\bs-tests\...` (интеграционные и так запускаются с этим
   параметром). Сборка точной командой из §7 — 0 предупреждений / 0 ошибок. Это ограничение
   среды, не изменение кода; в CI команда обычная.

## 5. Evidence

### 5.1 Сырые запросы и ответы

`verification/issue-95-api-samples.txt` (скрипт `verification/issue-95-api-samples.ps1`, клиент
`e2e-mutations@…`, барбер B1; сцена закрыта обратным переносом):

- `GET /api/bookings/{id}/reschedule-slots?date=…` → `200`, `durationMinutes: 30`,
  `timeZoneId` салона, слоты по 30 минут;
- `POST /api/bookings/{id}/reschedule { startAt }` → `204`, тело пустое;
- после переноса `GET /api/bookings`: новые `startAt`/`endAt`, `price` и `status` прежние;
- занятое время → `409`, `errorCodes: ["booking.reschedule.overlap"]`;
- время вне сетки → `409`, `errorCodes: ["booking.reschedule.slot"]`;
- прошлое → `400`, `errorCodes: ["booking.reschedule.startAt.inPast"]`, поле `StartAt`;
- +91 день → `400`, `errorCodes: ["booking.reschedule.startAt.tooFar"]`;
- уборка: обратный перенос → `204`.

### 5.2 Тесты

| Команда | Результат |
|---|---|
| `dotnet build BarberSearch/BarberSearch.sln --nologo` | 0 предупреждений / 0 ошибок |
| `dotnet test tests/BarberSearch.Domain.UnitTests` | 595 passed, 0 failed |
| `dotnet test tests/BarberSearch.Application.UnitTests` | 955 passed, 0 failed |
| `dotnet test tests/BarberSearch.Infrastructure.UnitTests` | 351 passed, 0 failed |
| `dotnet test tests/BarberSearch.IntegrationTests` | 313 passed, 0 failed (реальный PostgreSQL, 4 m 17 s) |
| `dotnet ef migrations has-pending-model-changes` | «No changes have been made to the model…» |
| `npm run lint` | 0 warnings / 0 errors, 532 файла |
| `npm test` | 144 файла, 1525 тестов passed |
| `npm run build` | 2620 модулей, entry `354.60 kB` (gzip `114.72 kB`) |
| `npm run check:i18n -- --strict` | t() 1762, переводов 2078, нет перевода 0, конфликтов 0, видимых русских строк в JSX 0 |
| `npx tsc -p tsconfig.e2e.json --noEmit` | чисто |

Логи: `issue-95-backend-tests.txt`, `issue-95-unit-tests-temp.txt`,
`issue-95-frontend-tests.txt`, `issue-95-frontend-build.txt`.

### 5.3 E2E

`verification/issue-95-e2e-full-3.txt` — полный `npm run test:e2e` (с `--workers=4`):
**316 passed, 6 skipped, 0 failed** (4.1 m), включая новый
`e2e/specs/booking-reschedule-mutations.spec.ts`:

1. клиент переносит вторую строку «Предстоящих» на +2 дня, видит баннер «Визит перенесён на …»,
   и API подтверждает новые `startAt`/`endAt` при прежних `price`/`status`/длительности; в
   `finally` — обратный перенос;
2. барбер (`scheduleBarber`) переносит гостевой визит на сегодня: строка «Ближайших» и лента дня
   показывают новое время; в `finally` — отмена гостя;
3. `409`: слот занимается переносом существующей чужой строки через API уже после открытия
   диалога → «Это время уже занято. Выберите другое.», сетка перезапрашивается, выбор сброшен,
   исходная бронь не изменилась; в `finally` — обратный перенос;
4. axe, Escape и возврат фокуса на кнопку строки на 1280 и 320, горизонтального скролла нет.

`issue-95-e2e-full.txt`, `issue-95-e2e-full-2.txt` и `issue-95-e2e-full-4.txt` — три полных
прогона с дефолтным числом воркеров. Сцены переноса в них ни разу не упали; падали соседние
спеки, разные в каждом прогоне: a11y-отзывы (0 карточек за 7 с), мобильные списки записей и
«Дальше» без перехода, форма профиля в сценарии смены email, и в `-4` — 404 при
`DELETE /api/reviews/{id}/publication` из-за одновременной публикации/скрытия одного и того же
отзыва двумя мутационными спеками. Каждая из этих спек в изоляции зелёная
(`npx playwright test e2e/specs/a11y.spec.ts`, `account-bookings.spec.ts`,
`account-reviews.spec.ts`), то есть это предел 12 воркеров на этой машине, а не регресс переноса.
Прогон `-3` с 4 воркерами (в CI — 2) зелёный полностью.

### 5.4 Скриншоты

- `issue-95-dialog-1280.png` — диалог на 1280: заголовок, описание со старым временем и услугой,
  честная строка про цену, дата, 18 слотов, «Отмена» / «Перенести визит» (кнопка disabled до
  выбора слота).
- `issue-95-dialog-320.png` — тот же диалог на 320: дата на всю ширину, чипы в три колонки, нет
  горизонтального скролла (`scrollWidth == clientWidth`), кнопки столбиком.

Скриншоты сняты `verification/issue-95-shots.mjs` (аккаунт `mutations`) против живого API.

## 6. Что осталось (долг)

- Уведомлений о переносе нет: ни письма, ни Telegram, ни push. Клиент узнаёт из кабинета или
  баннера опроса; барберу диалог это честно сообщает. Отдельный срез с шаблонами и очередью.
- Админского действия в UI нет (API доступен); карточка брони у администратора остаётся
  справочной.
- Перенос серии/повторяющихся записей, каскадный перенос и drag-and-drop не делались.
- Правка цены по-прежнему вне журнала: allowlist пишет время, но не `Price`/`SalonId`/
  `BarberServiceId`.
- Публичный `POST /api/bookings` по-прежнему не сверяет сетку слотов и рабочие часы — это
  отдельный долг, не смешанный с этим срезом.
