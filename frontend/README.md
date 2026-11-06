# BarberSearch frontend

React 19 + TypeScript SPA for BarberSearch — barber discovery and online booking.
Runs against the ASP.NET Core API in `../backend`.

## Stack

- React 19, TypeScript 6, Vite 8
- Tailwind CSS v4 (`@tailwindcss/vite`, no `tailwind.config`)
- shadcn/ui components (Base UI primitives, `base-nova` style) in `src/components/ui`
- Oxlint for linting

## Commands

```bash
npm install
npm run dev      # http://localhost:5173 (Vite picks the next free port if busy)
npm run build    # tsc -b && vite build
npm run lint     # oxlint
npm run preview  # serve dist/

npm test           # Vitest unit suite: no API, no browser (see «Testing»)
npm run test:watch # the same suite in watch mode

npm run test:e2e:install   # once: download Chromium for Playwright
npm run test:e2e           # Playwright + axe against a real API (see «E2E»)
npm run test:e2e:ui        # the same in the Playwright UI mode
```

## Routes

| Path | Screen |
|---|---|
| `/` | landing page: real barber search by city, date, language, specialization and service, how it works, account CTA |
| `/barbers/{id}` | public barber profile: services, salons, weekly schedule, rating |
| `/account` | personal cabinet, «Мои записи»: «Предстоящие»/«История» as two server lists with the shared pager, hero, cancellation and the barber's cancellation reason, review |
| `/account/profile` | profile: first name, last name and phone; email is read-only with its verification status |
| `/account/security` | security: password change, sign out and email verification status |
| `/account/reviews` | author's own reviews: both statuses, edit and delete |
| `/barber` | barber cabinet: client bookings, confirm / complete / no-show / cancel with a required reason / note, and «Записать клиента» for a registered user of any role or a walk-in without an account |
| `/barber/onboarding` | creates the barber profile of a `Barber` who has none: every field of the create request, Russian validation, session refresh after `201` |
| `/barber/schedule` | barber schedule: weekly template, date exceptions (day off or custom hours) and time offs |
| `/barber/salons` | «Мои салоны»: linked salons with moderation status (approved / pending / rejected + reason), «Создать салон» for a barber with a profile (goes to admin review), owner address/contacts edits, picker for an existing salon |
| `/become-a-barber` | request for the barber role: public copy, the customer's form or status, links for a barber and an admin |
| `/register` | customer self-registration; a signed-in visitor sees the «Вы уже вошли» gate |
| `/privacy` | Политика обработки персональных данных: полный документ (152-ФЗ), содержание, перечень cookie, реквизиты Оператора |
| `/admin` | admin shell; redirects to `/admin/reviews` |
| `/admin/summary` | «Что требует внимания»: reviews on moderation with the oldest one, barbers without confirmation, disabled salons — counters only, no actions; a salon waiting for moderation is not counted as disabled |
| `/admin/reviews` | review moderation queue (pending / published tabs) |
| `/admin/reviews/{id}` | one review; the tab is in `?tab=published` |
| `/admin/barbers` | barber queue: pending / all / hidden (filter in `?status=all\|hidden`, `pending` by default), search by name or email |
| `/admin/barbers/{id}` | one barber: contacts, readiness checklist, verify/grade/hide; the current filter stays in `?status=` |
| `/admin/users` | user list: status/role filters, search by name or email |
| `/admin/users/{id}` | one user: role, block/unblock, email/phone confirmation |
| `/admin/barber-requests` | «Стать барбером» queue: pending requests oldest first, `?status=all` for every request newest first |
| `/admin/barber-requests/{id}` | one request: applicant, answers, submission/review dates and the rejection reason; approve (with the session warning) or reject with a required reason |
| `/admin/catalogue` | catalogue of services, specializations and salons (tab in `?tab=services\|specializations\|salons`, `services` by default): full dictionaries including hidden rows, status filter (salons add «На проверке»), search, hide/return, salon approve/reject |
| `/admin/bookings` | booking support list: customer email or exact booking id search, status filter, paged table/cards, split-view detail |
| `/admin/bookings/{id}` | one booking: time in the salon zone, customer, barber, service, salon, notes; cancel or mark a no-show |
| `/admin/audit` | read-only change journal: filters by entity type, author email and UTC dates, paged list with an expandable diff |

The cabinet is one personal space for every authenticated user: a customer, a barber and an
administrator see the same rail (Записи · Отзывы · Профиль · Безопасность), their own bookings and
reviews, and a role-specific link to the work area. The barber cabinet and the admin section stay
separate client-side gates; the API enforces their roles (`AdminOnly`, `BarberOnly`).

## List pagination

Lists that show records in rows share one controller (`src/hooks/use-paged-list.ts`) and one footer
(`src/components/list-pager.tsx`): ten or twenty rows per page, «Дальше» only by the opaque
`nextCursor` from the response, visited pages cached in memory so «Назад» makes no request, and the
range from `formatPageRange()` in `src/lib/paging.ts` («11–20 из 32» only where the server returns a
total, otherwise «21–40»). A next-page error keeps the visible rows and turns the button into
«Повторить»; filters, tabs and search start a new first page.

One list deliberately keeps «Показать ещё»:

- `/` — the search results are a grid of cards; appending keeps them comparable, while pages would
  hide barbers the visitor has already seen.

Every other list — the public reviews, `/account/reviews`, `/account`, `/barber`, `/admin/reviews`,
`/admin/barbers`, `/admin/users`, `/admin/bookings` and `/admin/audit` — pages with the same footer.
The cabinet's bookings list was the last one to move to the pager: it split one server list on the client,
which the server-side `scope` fixed (#36). The bookings list was built on the shared pager from the
start: the email search and the status filter are part of the request, so changing either starts a
new first page and the catalogue stays the only admin read without one.

The three admin catalogue dictionaries are the second, different exception: `/admin/catalogue` has
**no pager at all**. A dictionary is dozens of rows, the public `GET /api/hair-services` and
`GET /api/specializations` are bare arrays too, and a row the admin just created has to be visible
immediately instead of on «page 3». So `GET /api/admin/catalogue/*` returns `IReadOnlyList<T>`, the
screen has no «Показать ещё» and no row counters, and the number in the «Барберы» column is
`usageCount` from the API, not the length of the array. When a dictionary outgrows a screen, paging
it becomes a separate decision (a text keyset, which the project does not have yet) — not a quiet
change to this screen.

The journal is **not** an exception even though it is also a log: `audit_log` grows monotonically,
so `/admin/audit` pages through the same controller and footer. The catalogue remains the only
admin read without a pager.

## Barber search

The landing calls `GET /api/barbers?city=&specializationId=&hairServiceId=&date=&language=&cursor=&pageSize=`
(anonymous, 120 requests per minute per IP). The filters are a **city** (trimmed and compared
exactly, case-sensitive), an optional **date** — the barber has a working day on that weekday —
one **language** (an enum name such as `Ru`), one **specialization** and one **hair service**;
empty values are not sent at all. Results are ordered `rating DESC NULLS LAST, id`; `pageSize`
is 12 and the next page is fetched with the opaque `nextCursor` from the previous response.
There is no text search, and the card shows no price or service tags because the search DTO has
neither.

The language select always lists the six known languages. The specialization and service selects
come from `GET /api/specializations` and `GET /api/hair-services` (anonymous, active rows only,
bare arrays, no pagination): an empty catalogue hides its select instead of showing a dead list.
While they load the selects are disabled with «Загрузка…»; if the load fails they stay disabled
and the form shows a Russian `role="alert"` with «Повторить» — city, date, language and «Найти»
keep working. Only «Найти» sends a request; filters live in React state, not in the URL.

- `src/lib/barber-search.ts` — filters, `emptyBarberSearchFilters`/`normalizeBarberSearchFilters`, `BarberSearchItem`, `searchBarbers()`.
- `src/hooks/use-barber-search.ts` — pages, abort of stale requests, retry, load-more state.
- `src/hooks/use-search-catalogs.ts` — parallel catalogue read, abort on unmount, retry.
- `src/lib/specializations.ts`, `src/lib/hair-services.ts` — catalogue types and requests.
- `src/components/home/` — hero/search form, results grid, cards and the account CTA.

## Barber profile

`/barbers/{id}` calls `GET /api/barbers/{id}` (anonymous, 120 requests per minute per IP; `404`
for a missing, hidden or unconfirmed profile, `400` for an empty GUID). The page shows the name,
Russian grade, «Проверен» badge, rating or «Пока нет отзывов», experience, bio, languages,
certificates, achievements, comfort and communication lists, services with price and duration,
specializations, salons with addresses and the weekly schedule, payment methods and the late
policy. Empty blocks are hidden instead of rendering an empty heading.

«Свободное время» sits under the hero and calls `GET /api/barbers/{id}/slots?date=&hairServiceId=&salonId=`
(anonymous, 120 requests per minute per IP). `date` is the **salon-local** `YYYY-MM-DD` and is
required; `hairServiceId` comes from `services[].hairServiceId` and changes the grid step;
`salonId` is always sent and defaults to the primary salon. The date input opens on today in the
salon time zone (`min` = today, `max` = today + 30 days), the service select offers
«Любая услуга» plus the active services, and the salon select appears only when the barber has
more than one salon. Slots are shown in the salon time zone (`timeZoneId`), never recomputed into
the browser zone. A day off, a working day without slots, loading, a retryable
network/`429`/`5xx` error, `404` (profile/salon/service) and `409` (broken salon time zone) each
have their own Russian state; the previous request is aborted when the parameters change. There
is no slots cache: slots change with bookings and time off.

### Reviews

The profile also shows the published reviews of the barber:
`GET /api/reviews?barberProfileId=&cursor=&pageSize=` (anonymous, 120 requests per minute per
IP). `barberProfileId` is required, the UI always sends `pageSize=10`, and the next page is
fetched with the opaque `nextCursor` from the previous response. The API returns only published
reviews, newest first (`created_at DESC, id`). The DTO has no client names, avatars, service or
visit time, so a review card shows only five stars with a text alternative, the numeric rating,
the calendar date in the primary salon time zone and the text. The summary («4,0 · 2 отзыва»)
comes from `rating`/`reviewsCount` of the already loaded profile, never from the current page.

States: six skeleton cards while the first page loads, an empty list, a first-page error with
`describeApiFailure` and «Повторить», and a next-page error that keeps the shown cards and turns
«Дальше» into «Повторить». The list is paged ten cards at a time instead of growing: «Дальше»
replaces the visible page with the next ten, «Назад» returns to a page that is already in memory
without a request, and the profile keeps the same height instead of stretching with every click.
The range («1–10 из 32») lives in the shared pager footer, not in the section header; after
«Назад»/«Дальше» focus moves to the list, the section heading is scrolled into view and the pager
announces the new range through one `aria-live` region. The first and last pages disable the arrow
instead of removing it. A profile with `reviewsCount: 0` shows «Пока нет отзывов.» and makes no
request at all. Creating a review stays in the personal cabinet
(`POST /api/reviews` on a completed booking, hidden until moderation); editing and deleting go
through the author list at `/account/reviews`.

- `src/lib/reviews.ts` — review DTO, page size 10, `fetchBarberReviews()`.
- `src/hooks/use-paged-list.ts` — the shared cursor-page controller (cache, «Назад» without a
  request, abort + generation, next-page error, silent refresh).
- `src/components/list-pager.tsx` — the shared pager footer (range, focus, `aria-live`, retry).
- `src/components/barber/barber-reviews.tsx` — section, summary, states, stars and the pager.
- `src/components/review-stars.tsx` — the shared five stars, used by both review lists.

## My reviews

`/account/reviews` is a separate lazy screen inside the cabinet shell (not a tab of
`/account`). It calls
`GET /api/reviews/mine?pageSize=10` and lists the author's reviews in both statuses, newest
first: the rating is a badge and a text label («Опубликован» / «На модерации»), and each row
shows the barber's name as a real link, the visit date and time in the salon time zone, the
service, the salon, the price and the review date. The API response carries the whole visit
context, so the screen makes no `/bookings`, `/barbers/{id}` or public `/reviews` request;
`userId`, email and `bookingId` never reach the DOM.

Editing opens a dialog prefilled with the rating and text (rating 1–5, text up to 2000
characters, trimmed to `null` when empty) and sends `PUT /api/reviews/{id}` with
`X-CSRF-TOKEN`; `204` updates the row locally without a second read, and a published review
changes on the barber card immediately. Deleting asks for confirmation in an `alertdialog` and
sends `DELETE /api/reviews/{id}` with the CSRF header; a repeat `404` is treated as «уже
удалён», and a `403` keeps the row with an error. Only the author can edit or delete — the admin
has no bypass.

States: skeleton while the first page loads, first-page error with «Повторить», a next-page error
that keeps the rows and turns «Дальше» into «Повторить», and an empty state with «Перейти к
записям». The list shows ten rows per page instead of growing: the range («11–20») and the arrows
live in the shared pager footer. If deleting empties the visible page, the previous page is shown;
on the first page the list is loaded again, and when there is no next cursor the empty state
appears. Focus returns to «Изменить» after an edit and moves to the next row's «Удалить» (or the
list heading) after a delete; the pager moves focus to the list heading only after a page button
press.

- `src/lib/my-reviews.ts` — `MyReview` DTO, `fetchMyReviews`, `updateMyReview`, `deleteMyReview`.
- `src/hooks/use-my-reviews.ts` — the shared pager controller plus local update/delete, `401`
  and `404` handling.
- `src/pages/account-reviews-page.tsx` — gates, title, list composition and announcements.
- `src/components/account/my-reviews-list.tsx`, `my-review-row.tsx`,
  `review-status-badge.tsx`, `edit-review-dialog.tsx`, `delete-review-dialog.tsx`.
- `src/lib/account-path.ts` — the four cabinet paths and the `accountSection()` parser.

## Personal cabinet

`/account`, `/account/profile`, `/account/security` and `/account/reviews` share one shell
(`components/account/account-shell.tsx`): a sticky rail with the identity block (initials, name,
email, role, unverified-email note), four section links and — for a `Barber` or `Admin` — a link to
`/barber` or `/admin`. Below `lg` the rail becomes a 2×2 grid of labelled buttons, so all four
sections stay visible at 320 px. The whole cabinet is open to every role; the old `RoleGate` that
turned barbers and admins away is gone.

The shell loads `GET /api/users/me` once per session (module cache keyed by user id) and provides
the profile to the section screens through context, so the profile form does not issue its own
read. The header shows «Мой кабинет» for everyone, «Кабинет барбера» for a barber and
«Администрирование» for an admin; the avatar link points to `/account`.

### Bookings

`/account` shows the signed-in user's bookings. Both tabs are **server lists** from
`GET /api/bookings?scope=…&pageSize=20`: `upcoming` is `Pending`/`Confirmed` with `EndAt > now`
ordered by start ascending, `past` is everything else (a terminal status or `EndAt <= now`), newest
first; `all` stays the default for other clients of the endpoint. The client sends the scope and
never sorts or splits the rows: switching a tab starts the first page of its own scope, and the
shared pager (twenty rows, cached pages, «Назад» without a request) lives inside the tab panel. The
tab labels carry no counters — the response has no total, and the page length is not the count.

The hero card «Ближайшая запись» is the first row of «Предстоящие» (never of history), so it is
stable on the first page and moves after a cancellation. «История» is lazy: it is requested only
when the tab is opened for the first time. Cancelling waits for `204` and then removes the row from
«Предстоящие» under the shared empty-page policy; the cancelled visit appears at the top of
«История» the next time that tab loads, with the barber's `cancellationReason` when it was the
barber who cancelled (a customer cancellation or an older row says «Причина отмены не указана»). Barber names and salon addresses still come from
`GET /api/barbers/{id}` for the visible rows and the hero only, not for the hidden list.

States: hero/list skeletons, a first-page error with «Повторить», a next-page error inside the
pager (the rows stay), the sign-in gate on `401`, and per-tab empty states. An empty «Предстоящие»
shows one invitation card with «Найти барбера» instead of the duplicated hero/empty-list pair. A
next-page error in one tab does not clear the other.

- `src/pages/account-page.tsx` — session gate, shell, hero, tabs and the dialogs.
- `src/hooks/use-bookings.ts` — `useCustomerBookings`: two `usePagedList` controllers
  (`upcoming`/`past`), the lazy history switch and cancellation.
- `src/components/account/booking-list.tsx` — tabs, month groups per page, rows and the shared pager.
- `src/lib/bookings.ts` — `Booking`, `BookingScope`, `fetchBookings(scope, cursor, signal)`.

### Profile

`/account/profile` edits `firstName`, `lastName` and `phoneNumber` through the existing
`PUT /api/users/{id}/profile` with `X-CSRF-TOKEN`; `avatarUrl` is returned unchanged because the
endpoint replaces the whole profile, and avatar upload has no storage yet. Email is read-only: the
API has no self-service email-change flow, and the copy says so instead of showing a dead button.
The form enables «Сохранить» only when a field changed, trims values, maps
`user.updateProfile.firstName|lastName` to the field and everything else to the form-level Russian
error. `GET /api/users/me` supplies the initial values; the same response feeds the rail, so the
name updates immediately after a save without a second read.

### Security

`/account/security` reuses the existing `ChangePasswordDialog` (password from 12 characters,
`POST /api/auth/change-password`, all refresh tokens revoked) and adds sign-out. The copy explains
that changing the password ends the other sessions. The page also shows the email verification
status and the last login from `GET /api/users/me`; there is no active-device list because the API
does not expose one, and the screen does not invent it.

## Barber cabinet

`/barber` is a separate lazy screen for a signed-in `Barber` with a linked profile. The cabinet
has three sections — записи (`/barber`), расписание (`/barber/schedule`) and salons
(`/barber/salons`) — linked by `BarberCabinetNav` and sharing the same gates. It calls
`GET /api/barber/bookings?scope=upcoming&pageSize=20` (the profile comes from the
`barber_profile_id` claim, there is no profile query parameter) and switches to `scope=past`
for «История». The server orders both scopes (`start_at ASC` for upcoming, `DESC` for history),
so the client never sorts; the next page is fetched with the opaque `nextCursor`. Twenty rows are
shown per page instead of growing: a page replaces the visible rows, visited pages stay in memory
for «Назад» without a request, and the range («21–40», never «из N» — the server returns no total)
lives in the shared pager footer. Switching tabs starts from the first page and drops the cache.

A row shows the visit date and time in the salon time zone (`salon.timeZoneId`), the customer's
name and, when present, a `tel:` phone link, the service, the salon name/city/address, the price
and the status badge. Actions depend on status and time, not on the tab: `Pending` offers
«Подтвердить», «Отменить» and «Заметка»; `Confirmed` offers «Завершить» and «Неявка» only once
`startAt <= now` (the ticker updates every minute), «Отменить» in the upcoming tab and «Заметка»
always; terminal bookings are read-only. The customer's comment, the barber's note and the
`cancellationReason` of a cancelled booking render as text. Cancelling opens an `alertdialog`
with a required «Причина отмены» textarea (up to 500 characters): the client will see it in the
visit history, so an empty reason keeps the dialog open and sends nothing; the server repeats the
rule (`booking.cancel.reasonRequired`/`reasonTooLong`).

Mutations wait for the server and update the row locally — there is no second `GET`: confirm
changes the status to «Подтверждена»; complete, no-show and cancel remove the row from
«Ближайшие» and keep it in «История» with the new status and, for a barber cancellation, the
reason; the note dialog (1000 characters, an empty field clears the note) updates `barberNote`. Every write sends `X-CSRF-TOKEN`; the list
read does not. Double clicks are ignored while a request is in flight.

«Записать клиента» opens a dialog on the same screen. The barber chooses who the visit is
for. For a registered user he types at least three characters of the email or phone
(`GET /api/barber/clients`, debounced 300 ms, active users of any role, ten results). For
a walk-in he switches to «Новый клиент» and enters a required first name and phone (last
name optional); no account is created, and the contact is stored on the booking. Then he
picks a service and salon from his own profile (`GET /api/barber/booking-options` — works
before the profile is verified), chooses a salon-local date and a free slot
(`GET /api/barber/slots`), optionally adds a comment and submits. `POST /api/barber/bookings`
always creates a `Confirmed` booking with an idempotency key and CSRF; a registered user
sees it in their cabinet immediately, a guest does not have a cabinet. The dialog cannot
be closed while the request is pending; a `409` overlap, `400` start window or `404`
service/salon clears the chosen slot and reloads the grid, `404
booking.create.customerNotFound` asks to repeat the search, and `400 booking.create.guest*`
asks for the missing guest contact. On success the list reloads (or switches from «История»
to «Ближайшие») and the screen announces «Клиент записан: …».

States: skeleton while the first page loads (StrictMode may send a second, aborted request),
first-page error with «Повторить», a next-page error that keeps the rows and turns «Дальше» into
«Повторить», «Ближайших записей нет.» and «История пуста.». An action that empties the visible
page follows the shared policy: the previous page if it is not the first, otherwise the first page
is loaded again. A `401` on the list becomes the sign-in gate with
a retry after login. A `409` says «Запись уже изменилась — обновите список.» with an «Обновить»
button; `400 booking.markNoShow.tooEarly`, `403`, `404`, `429`, offline and `5xx` have their own
Russian copy, and the English `detail` is never shown. Gates cover anonymous, customer, admin and
a `Barber` without `barberProfileId`, and none of them sends a list request. Announcements and
errors use `role="status"`/`role="alert"`; focus returns to the row or the list heading after an
action, and the pager moves it only after «Назад»/«Дальше».

The header shows «Мой кабинет» for every authenticated user, plus «Кабинет барбера» for a barber
and «Администрирование» for an admin. The work area is also linked from the account rail; `/barber`
keeps its own role gates and `/account` no longer turns a barber away.

- `src/pages/barber-cabinet-page.tsx` — gates, title, list composition, announcements and the
  «Записать клиента» trigger.
- `src/components/barber/barber-cabinet-gates.tsx`, `cabinet-booking-list.tsx`,
  `cabinet-booking-row.tsx`, `barber-note-dialog.tsx`, `barber-booking-action-dialog.tsx`,
  `barber-booking-dialog.tsx` (manual booking) and `barber-slot-options.tsx` (shared slot
  chips/radios).
- `src/hooks/use-barber-bookings.ts` — scope, the shared pager controller, five local mutations,
  `401`, click guard; `use-barber-clients.ts` — debounced client search; and
  `use-barber-booking-options.ts` — own services and salons.
- `src/lib/barber-bookings.ts` — DTO, list read and the five write actions;
  `barber-booking-create.ts`/`barber-booking-options.ts`/`barber-clients.ts` — the manual
  booking transport; `src/lib/barber-cabinet-path.ts` — the `/barber`, `/barber/schedule` and
  `/barber/salons` path helpers.

### My salons

`/barber/salons` is the third cabinet section. It reads
`GET /api/barber-profiles/{id}/salons` (owner or admin; includes every linked salon, primary
first) and shows a card per salon with the name, city, address, «Основной» badge, time zone and
the moderation status: «Одобрен», «На проверке» or «Отклонён» with the administrator's reason.
An empty profile explains the consequence — «Салон не добавлен. Без салона расписание и запись
не работают.» — and offers «Добавить салон» and «Создать салон».

«Создать салон» is the barber's own path (`POST /api/salons/own`, cookie mutation with CSRF,
role `Barber`): the form is the shared `SalonDialogForm` (`src/components/salon/salon-form.tsx`,
the same fields the admin dialog uses) with name, address, city, country, time zone, coordinates,
phone and website. Coordinates are typed by hand — the form says to copy them from maps; there is
no map or geocoder in this slice. The name is fixed after creation and the form says so. The
server creates the salon in `Pending`, links it to the barber profile in one transaction (the
first salon becomes primary) and answers `201 { id }`; the screen re-reads the list and shows the
card with «На проверке» and «Салон появится в поиске после проверки администратором». Pending and
rejected salons are absent from the public directory, search, slots and the booking picker.

The owner edits the salon through two independent mutations, exactly like the admin dialog:
«Изменить адрес» sends `PUT /api/salons/{id}/location`, «Изменить контакты» sends
`PUT /api/salons/{id}/contact-info`. The dialog is pre-filled from the salon summary (country,
postal code, coordinates, phone, website), so saving does not wipe values the owner did not type.
Editing is allowed for a pending or rejected salon; the barber UI never activates, hides, renames
or resubmits it (there is no resend endpoint, so no dead button). Actions are shown only on salons
the user owns (`ownerUserId`; `null` for an admin-created salon linked to the profile).

«Добавить салон» opens a dialog over the public catalogue (`GET /api/salons?city=&search=`,
anonymous, only approved and active rows, no pagination): the search is debounced 300 ms and starts
at two characters, the city is optional, results are a native radio list in a `fieldset`, and every
new input or closing the dialog aborts the previous request. The first salon of an empty profile is
forced primary (`isPrimary: true`, the checkbox is checked and disabled); with existing salons
«Сделать основным» is the user's choice. `POST /api/barber-profiles/{id}/salons` is a cookie
mutation; after `204` the screen re-reads the list, announces «Салон добавлен.» and closes the
dialog. A `409` duplicate or `404` vanished salon keeps the dialog open with Russian copy and
re-reads the list; `403`, `401`, `429` and a network failure have their own messages. The route
does not change and «Настроить расписание» links to `/barber/schedule`.

- `src/pages/barber-salons-page.tsx` — gates, list, empty state, create/edit dialogs, announcements.
- `src/components/barber/barber-salon-card.tsx` — one linked salon: status, rejection reason,
  owner edit actions.
- `src/components/barber/barber-salon-dialog.tsx` — owner create/edit wrapper over the shared form.
- `src/components/barber/barber-salon-picker-dialog.tsx` — the shared catalogue picker (also the
  onboarding step).
- `src/components/salon/salon-form.tsx` / `src/lib/salon-form.ts` — shared fields, validation and
  payloads for the admin and owner dialogs; `src/lib/salon-moderation.ts` — status union, Russian
  labels and rejection-reason validation.
- `src/lib/salons.ts` / `src/lib/barber-salons.ts` — directory read, linked-salon read, attach and
  `createOwnSalon` transport; `src/hooks/use-barber-salons.ts` — re-read after a write.

### Creating the barber profile

Registration always creates a `Customer`; only an administrator grants the `Barber` role
(`PUT /api/users/{id}/role`). The grant used to be a dead end: `/barber` and `/barber/schedule`
said «Профиль барбера не привязан к аккаунту. Обратитесь к администратору», and the existing
`POST /api/barber-profiles` was reachable only by a `Barber` — exactly the person who could not
get to it. The missing-profile gate now leads to `/barber/onboarding`, a lazy screen with one form
for every field of `BarberProfileCreateRequest`: the display name (1–120), experience (1–70),
«О себе» (2000), at least one language, optional payment methods, and four optional one-per-line
lists (certificates, achievements, communication format, goodies) plus «Опоздания» (500). Empty
lines are trimmed away, an empty optional field is sent as `null`, and empty lists as `[]`;
`userId`, grade, rating and verification are never sent — the profile always belongs to the
current user. Client rules repeat the domain ones, and server codes
(`barberProfile.displayName.tooLong`, `barberProfile.yearsOfExperience`,
`barberProfile.languages.unknown`, …) are mapped to Russian field errors; `429`, a network
failure and `5xx` show a form error with «Повторить» (the form is not re-submitted automatically).
`409 barberProfile.create.alreadyExists` is not an error: the profile exists, so the screen
continues as if the `201` had just happened.

After `201` the client must **refresh the session**: `barber_profile_id` is a claim inside the
access token, and the token issued before the request still carries `null`. `retrySession()` alone
would read the same cookie and still see no profile, so the screen calls `refreshSession()` (a new
access token from the database), then `retrySession()`. The profile id comes from the `201` body,
so the screen then shows the «Добавьте салон» step: «Выбрать салон» opens the same
`SalonPickerDialog` as `/barber/salons`, and «Пропустить» (or «Перейти в кабинет») leads to
`/barber` — the step never blocks the cabinet, but without a salon the schedule is empty. If the
refresh is `terminal`, the profile is already created and the form must not return: the card says
«Профиль создан, войдите снова» and opens the login dialog with `/barber` as the destination. A
`transient` failure shows «Профиль создан, но сессия не обновилась» with «Повторить» (refresh
only) and «Войти»; after a successful retry the salon step appears. The form itself only validates
and sends; the session work lives in the page.

- `src/pages/barber-onboarding-page.tsx` — gates, the finishing/terminal/transient cards.
- `src/components/barber/barber-profile-form.tsx` — the reusable form (no effects, no navigation).
- `src/lib/barber-profile-create.ts` — `createBarberProfile` over `mutateWithCsrf`, domain limits
  and the Russian error mapping.
- `src/lib/barber-cabinet-path.ts` — `BARBER_ONBOARDING_PATH` / `isBarberOnboardingPath`.

## Barber schedule

`/barber/schedule` is the second section of the barber cabinet; `BarberCabinetNav` links it with
`/barber` and both screens share the same session, role and linked-profile gates. It reads
`GET /api/barber-profiles/{id}/schedule` (owner or admin, also for an **unverified** profile) and
shows the salons of the barber with a seven-day weekly template, upcoming date exceptions and
future time offs.

- **Week.** Seven rows, «рабочий/выходной» and two native time inputs per row; «Заполнить будни»
  applies Mon–Fri 10:00–20:00. «Сохранить расписание» sends one `PUT
  /api/barber-profiles/{id}/salons/{salonId}/working-schedules` with all seven days (no partial
  save), waits for `204` and re-reads the schedule. The button is dirty-only; a day whose end is
  not after its start is blocked with an inline message.
- **Date exceptions.** A native date input (today … +90 days), «Выходной» or «Свои часы», an
  optional 500-character note; one `PUT .../schedule-exceptions/{date}` per row and one `DELETE`
  to remove. An exception overrides the weekly template only in the selected salon.
- **Time offs.** A profile-wide period with an inclusive local «с … по …»; the client converts it
  to the exclusive UTC range the API expects (`startAt` = local midnight, `endAt` = midnight after
  the last day) in the primary salon zone, then `POST`/`DELETE .../time-offs`. The UI says that
  the period applies to every salon.
- **States.** Skeletons, a page-level first-read error with «Повторить», a refresh error that
  keeps the cards, `403`/`404` copy, `role="status"` announcements after each write and
  `role="alert"` for validation/server errors. Existing bookings are **not** cancelled by a
  schedule change; the week card says so explicitly.

Files: `src/pages/barber-schedule-page.tsx`, `src/components/barber/barber-cabinet-nav.tsx`,
`barber-week-schedule.tsx`, `barber-schedule-exceptions.tsx`, `barber-time-offs.tsx`,
`src/hooks/use-barber-schedule.ts`, `src/lib/barber-schedule.ts`.

## Booking

With «Любая услуга» the chips stay informational and a hint asks to choose a service: the API
needs `barberServiceId`, and `/slots` returns it only when `hairServiceId` is sent. After a
concrete service is chosen, the slots become native `<input type="radio">` chips inside a
`<fieldset>` (arrow keys and Space work, the selected chip has a border, weight and check icon,
and the focus ring stays visible). Selecting a slot mounts one panel under the list with the
salon-local day and time, service, salon and price.

The panel sends a real `POST /api/bookings` (authenticated customer, 30 writes per minute per
user) with `barberProfileId`, `barberServiceId` and `salonId` from the `slots` response,
`startAt`/`endAt` exactly as the API returned them, the trimmed comment or `null`, and a UUID
`idempotencyKey` created once per panel. The client never derives the end time or converts the
instants into the browser zone. `X-CSRF-TOKEN` is **not** sent: the backend does not validate
antiforgery on this endpoint, so the booking path has no `GET /api/auth/csrf`.

- Anonymous visitor: the panel shows the summary, «Войдите, чтобы подтвердить запись» and
  `LoginDialog` with `redirectTo={null}`. Sign-in updates the session without leaving the page;
  the selected slot and the idempotency key survive, and the comment and «Записаться» appear.
- `201` returns only `{ id }`: the panel shows «Заявка отправлена» with the `Pending` badge
  («Ждёт подтверждения»), the submitted snapshot and links «Мои записи» / «Записаться ещё раз».
  Slots are reloaded so the booked time disappears; the confirmation stays until the visitor
  changes the date/service/salon or starts a new attempt.
- `409 booking.add.overlap` → «Это время только что заняли. Выберите другое.» with a slot
  reload; `409 booking.add.lockTimeout` → retry with the same key; `400 startAt.inPast`/`tooFar`
  → «Это время уже прошло…» / «слишком далеко» with a slot reload; `401` asks to sign in and
  then resubmits with the same key; `404` says the service or salon is no longer available;
  `429`, network and `5xx` use `describeApiFailure` and retry with the same key. The same key is
  reused for every retry of one attempt, so a repeated request returns `201` with the same `id`
  instead of creating a second booking. The submit guard ignores a second click while a request
  is pending.

- `src/pages/barber-page.tsx` — document title, back link, states and section composition.
- `src/components/barber/` — hero, slots, booking panel, about, services, specializations,
  salons, state views.
- `src/hooks/use-barber-profile.ts` — cache-first load, abort on id change/unmount, retry.
- `src/hooks/use-barber-slots.ts` — one slots request per parameter change, abort + generation,
  retry and `reload()` after a write.
- `src/hooks/use-create-booking.ts` — one `POST /bookings`, double-click guard, failure kinds
  (`retry`/`refresh`/`signIn`/`fatal`/`unavailable`).
- `src/lib/barber-path.ts`, `src/lib/catalog.ts` — route parsing and the shared profile cache.
- `src/lib/barber-slots.ts` — slots DTO, `fetchBarberSlots`, salon-local date helpers.
- `src/lib/bookings.ts` — `createBooking`, the booking list and cancellation.
- `src/lib/barber-profile-labels.ts` — Russian labels for categories, languages, payment and days.

Search cards on the landing are real links, so middle click and «открыть в новой вкладке» work.
Review creation on the public profile is not implemented: it lives in the personal cabinet. The
barber cabinet is at `/barber`. Services carry `barberServiceId` (sent to `POST /api/bookings`)
and `hairServiceId` (sent to `/slots`).

## API proxy

In development Vite proxies `/api` to `http://localhost:5243` (the backend `http` launch
profile). The target comes from `.env` via `loadEnv`; copy the committed example once:

```bash
cp .env.example .env
```

`.env.example` documents `VITE_API_PROXY_TARGET=http://localhost:5243`, and the same default
applies without a `.env`. `.env` and `.env.*` are git-ignored (`!.env.example` keeps the
example), so local values are never committed. A shell variable still overrides the file:

```bash
VITE_API_PROXY_TARGET=https://localhost:7175 npm run dev
```

The footer repeats the address, and only in development: the line is behind
`import.meta.env.DEV`, so a production build contains no `localhost:5243` in its DOM or JS.

## Authentication

The header button signs in through the browser (cookie) branch of the API:
`GET /api/auth/csrf` → `POST /api/auth/login/cookie` → `GET /api/auth/me`.
Tokens never reach JavaScript: the session cookie and the refresh cookie are both `HttpOnly`.
The cabinet adds one authenticated read, `GET /api/users/me` (`[Authorize]`, self-only, id from the
`sub` claim): unlike `/auth/me`, which is built from claims, it returns the current first name,
last name, phone, avatar URL, role, both verification flags and the last-login time from the
database. The profile form writes through the existing `PUT /api/users/{id}/profile`; the API
replaces the whole profile, so the client sends `avatarUrl` back unchanged.
Every state-changing request goes through `mutateWithCsrf` in `src/lib/csrf.ts`: it fetches a
fresh `X-CSRF-TOKEN` immediately before the request and, on `400 auth.csrf.invalid`, repeats the
same mutation **once** with a new token. The antiforgery filter rejects the request before the
action runs, so nothing was changed and the repeat is safe; a second failure shows the existing
Russian text and the dialog stays usable — there is no third attempt. There are two exceptions:
`POST /api/bookings` and `POST /api/auth/register`. The API does not validate antiforgery on
either, so the client does not fetch or send a token there (see `AGENT.md` §9.4 and §14).

Successful responses are checked too: `apiRequest` accepts a hand-written guard from
`src/lib/response-guards.ts` and turns a malformed `200`/`201` into a normal `ApiError`
(«Сервер вернул неожиданный ответ.»). A screen then shows its own Russian fallback with
«Повторить» instead of crashing on `undefined`, and a broken `/auth/me` ends in `unavailable`
rather than the sign-in gate. English `detail` and `HTTP …` are still never shown.

### Silent session refresh

A `401` on a data request is absorbed by the transport: `src/lib/api.ts` asks the session
provider for one refresh and repeats the same request exactly once. The refresh is
`GET /api/auth/csrf` → `POST /api/auth/refresh` with the CSRF header and **no body** → `204`
with both cookies rotated. It is single-flighted per tab in `src/lib/session-refresh.ts`, and
`navigator.locks` keeps two tabs from presenting the same refresh token — the API revokes the
whole chain and answers `401 auth.refresh.reused` on reuse.

- **refreshed** — the request is retried, `status` never leaves `authenticated`, the header and
  the screen do not flash.
- **terminal** — any `401 auth.refresh.*` (`missing`, `invalid`, `revoked`, `expired`,
  `inactive`, `reused`): the client becomes anonymous, the existing gates appear and the hint is
  removed. The terminal state is sticky until the next sign-in, so a burst of `401` cannot cause
  a refresh storm.
- **transient** — `429`, network failure or `5xx`: the session and the header email stay, the
  screen shows «Слишком много запросов…» / «Не удалось…» / «Нет связи…» with «Повторить», and the
  retry goes through the refresh again.
- **`400 auth.csrf.invalid`** — retried once with a freshly fetched token.

A successful refresh does not call `/auth/me`: the repeated data request brings the data, which
keeps the auth limiter (10 requests per minute per IP, shared with sign-in, sign-out and
registration) for real work.

### Session check failed (`unavailable`)

A session check that cannot reach the API is not a sign-out. When `GET /api/auth/me` answers
`5xx` or the network is down on mount, when the bootstrap refresh is `transient`, or when the
second `/auth/me` after a successful refresh fails, the provider sets
`status = 'unavailable'`: the visitor is not asked to sign in and the screens do not fetch data.

The screen shows a `SessionUnavailable` card («Не удалось проверить сессию» / «Сервер не
ответил. Проверьте подключение и повторите попытку.») with «Повторить» on `/account`,
`/account/reviews`, `/barber`, `/admin` and `/register`, a compact «Повторить» block in the
booking panel, and a compact retry button in the header (an icon with the accessible name «Нет
связи с сервером. Повторить проверку сессии» on narrow screens). No «Войти» button and no
sign-in/role gates appear while the state is unknown. The landing, the barber card and the slots
keep working: anonymous reads do not depend on the session.

`retrySession()` runs the same bootstrap again under the `loading` status, the «Запомнить меня»
hint and the one possible silent refresh included. It is single-flight, so StrictMode's double
mount and a double click produce exactly one request; `200` becomes `authenticated`, `401`
becomes `anonymous` and the normal gates return, another failure keeps `unavailable`. `401`
itself remains the only authoritative “not signed in”.

### «Запомнить меня»

The access cookie lives 8 hours (sliding), the refresh cookie 14 days. JavaScript cannot see
either once the access cookie is gone, so the client sets its own **non-credential** hint
`barbersearch.remember=1`: `Path=/`, `SameSite=Lax`, `Max-Age` 14 days, `Secure` on https, and
deliberately not `HttpOnly`. It is the only JavaScript-readable cookie that takes part in
authentication — the cookie notice adds a second client cookie, `barbersearch.cookie-notice`,
which only remembers that the strip was closed. The hint carries nothing but `'1'`: forging it
only causes one failed refresh, and no `localStorage`/`sessionStorage` is
used. The hint is set on a successful sign-in with «Запомнить меня», cleared on sign-out, on a
sign-in without it and on a terminal refresh, and re-written after a successful refresh.

On load `GET /api/auth/me` decides:

- `200` — authenticated, as before;
- `401` and **no** hint — anonymous, without a CSRF call or a refresh;
- `401` and the hint — one refresh, then `/auth/me` again: this is how a remembered session
  survives a browser restart;
- `401` even after a successful refresh — anonymous and the hint is removed (no second refresh);
- `5xx`, a network failure or a `transient` refresh — `unavailable` (see above); the hint is left
  intact, so «Повторить» or the next reload tries again.

Consequences worth knowing:

- without «Запомнить меня», a reload after 8 idle hours still asks to sign in, even though the
  refresh cookie remains alive; the asymmetry is deliberate (§6.2, §16);
- a successful refresh makes the access cookie persistent on the backend regardless of the
  choice — known backend behaviour, recorded as debt §14 #25, the client cannot change it.

Files: `src/lib/session-refresh.ts` (single-flight, Web Locks, outcome classification),
`src/lib/session-hint.ts` (the hint cookie), `src/lib/api.ts` (one retry after `401`, strict
ProblemDetails parsing),
`src/lib/auth.ts` (`requestSessionRefresh`), `src/components/session-provider.tsx` (bootstrap,
`retrySession` and the registered `401` handler) and `src/components/session-unavailable.tsx`
(the card and the header button).

## Registration

`/register` is the customer self-registration screen. It is not in the header navigation: the
entry points are the «Нет аккаунта? Зарегистрироваться» link in the login dialog, the
«Зарегистрироваться» action in `SignInGate` (so it appears on `/account`, `/account/reviews`
and `/barber`), and a direct address. The page reads the session only and mounts the form for an
anonymous visitor; a signed-in user gets «Вы уже вошли» with a button to `/account`, `/barber` or
`/admin` by role, and the page makes no data request.

The form sends one `POST /api/auth/register` with `{ email, password, firstName, lastName, consentVersion }` and
**no** `X-CSRF-TOKEN` (the action has no antiforgery filter, like booking creation). The email
and names are trimmed; the password is not. `consentVersion` is `PRIVACY_POLICY_VERSION`
(`src/lib/legal.ts`, `2026-11-03`) — the policy edition the checkbox names. Client checks before
the request: names are not empty, email matches `^[^\s@]+@[^\s@]+\.[^\s@]+$` and is at most 254
characters, the password is 12–128 characters and differs from the email, its local part and both
names, the repeat matches, and the **consent to personal-data processing** is given. The consent
is a separate required checkbox (not pre-checked, with the edition and a link to `/privacy`):
without it the form shows «Отметьте согласие на обработку персональных данных.» and sends
nothing. The server is authoritative: it stamps the UTC time and the client IP and stores the
record in `users`, so no account is created without consent (`400 auth.register.consentRequired`)
or with an outdated edition (`400 auth.register.consentVersion`, shown as «Политика обработки
данных обновилась. Обновите страницу и подтвердите согласие снова.»). The top-10k password
dictionary is a server check only — its public code is mapped to Russian copy, the dictionary is
not reproduced on the client.

On `201 { id }` the form signs in with the same credentials through the usual CSRF path
(`GET /api/auth/csrf` → `POST /api/auth/login/cookie` → `GET /api/auth/me`) and navigates to
`/account`; the role is always `Customer` and `isEmailVerified` is `false`. If the auto sign-in
fails, registration is **not** repeated (a second attempt would be `409`): the form is replaced
by the «Аккаунт создан» card with a `LoginDialog` (`redirectTo="/account"`).

Public codes are mapped at the field or the form: `user.create.emailTaken` (409),
`auth.register.consentRequired` (the checkbox error), `auth.register.consentVersion` (the
form-level «Политика обновилась» alert), `auth.password.required` / `tooShort` / `tooLong` /
`common` / `equalsEmail` / `equalsName`, `user.email`, `user.firstName`, `user.lastName`; `429`,
offline, `5xx` and anything else have their own Russian text. The English `detail` is never
shown. Both registration and sign-in use the auth rate limiter (10 requests per minute per IP),
so a registration with its auto sign-in spends two of them. The new account is active
immediately; email confirmation is out of scope. Accounts created before the consent record
existed and the ones an administrator creates have no record — that is expected and the admin
card says «Записи нет».

- `src/pages/register-page.tsx` — title, loading skeleton, anonymous card and the signed-in gate.
- `src/components/register-form.tsx` — fields, client/server errors, the consent checkbox, submit
  guard and the «Аккаунт создан» card.
- `src/lib/auth.ts` — `RegistrationInput` and `register()`; `src/lib/register-path.ts` —
  `REGISTER_PATH` / `isRegisterPath`.

## Become a barber

`/become-a-barber` is the legal path from `Customer` to `Barber`: a request, an admin decision and
a new role. Until #46 the role could only be changed from the admin panel, so a customer had to
ask an administrator outside the product. The page is public and has four states, and only one of
them reads the server:

- **anonymous** — «что даёт статус барбера» (a public profile in search, client bookings, an own
  schedule), a `LoginDialog` with `redirectTo="/become-a-barber"` and «Зарегистрироваться»;
  **no request** is made, so the page works for a visitor and on a broken API.
- **loading / unavailable** — the usual skeleton and the `SessionUnavailable` card; nothing is
  mounted until the session is known.
- **`Customer`** — one `GET /api/barber-requests/mine`. `request: null` mounts the form; a request
  mounts the status card; «Подать заявку снова» reveals the form again after a rejection (the
  domain allows one new `Pending`).
- **`Barber`** — «Вы уже барбер» with `/barber`, or `/barber/onboarding` while
  `barberProfileId === null`; **`Admin`** — a link to `/admin/barber-requests`.

The form sends one `POST /api/barber-requests` with CSRF and `{ displayName, yearsOfExperience,
city, about: string | null, hasSalon }`. The body carries **no role**: `Barber` is granted only by
an approval. Client limits mirror the domain (name 1..120, experience 1..70, city 1..100, «о себе»
≤ 2000, an empty textarea becomes `null`). `201` returns the request itself, so the form is
replaced by the same status card under «Заявка отправлена» — no second read. `409
barberRequest.create.alreadyPending` (one active request per user) says «Заявка уже на
рассмотрении», `403` «Заявку может подать только клиент», `429` and a network failure offer
«Повторить». The status card shows «На рассмотрении», «Одобрена» or «Отклонена» plus the reason,
the submission date and, while the session still says `Customer`, «Войти снова»: approval rotates
the security stamp, so the old session is dead and cannot be repaired.

`/account` mounts a compact card for `Customer` only. It reads the same endpoint once, never
blocks the booking lists, shows a quiet «Не удалось проверить заявку.» with «Повторить» for `429`
or a network failure, and renders nothing on `401` (the cabinet gate is already above it).

The admin side is `/admin/barber-requests`: the `pending` queue (oldest first, the default) and
the `all` history (newest first) share one cursor list with no `total`, so the range comes from
`ListPager` and there are no counters. The filter lives in `?status=all` and the card in the path,
like `/admin/barbers`, so a reload keeps both. Approval opens an `alertdialog` that says the role
change ends **all** of the applicant's sessions and that the profile is created after signing in
again; rejection opens an `alertdialog` with a **required** reason (≤ 500) — an empty one is never
sent. After a decision the row is updated locally (and leaves the queue), then the list and the
card are silently re-read; success is announced in `role="status"`, errors in `role="alert"`. Empty
states are «Заявок нет» and «Заявок с таким статусом нет». Requests are **not** written to
`audit_log`: the row keeps the personal data and the decision, and the journal only sees the
`user.updated` role change.

- `src/lib/barber-request-path.ts` — `BECOME_A_BARBER_PATH` / `isBecomeABarberPath`.
- `src/lib/barber-requests.ts`, `src/lib/barber-request-form.ts` — DTOs, the read/create transport,
  error mapping and the client-side form bounds.
- `src/components/barber/barber-request-form.tsx`,
  `src/components/barber/barber-request-status.tsx`, `src/components/account/barber-request-card.tsx`.
- `src/lib/admin-barber-requests.ts`, `src/hooks/use-admin-barber-request-list.ts`,
  `src/hooks/use-admin-barber-request.ts` — the admin queue and the two decisions.
- `src/components/admin/barber-requests-screen.tsx` plus `barber-request-list.tsx`,
  `barber-request-detail.tsx`, `barber-request-action-dialog.tsx`; `src/lib/admin-path.ts` —
  `barberRequestsPath()` and `?status=pending|all`.

## Legal information and cookies

The service publishes its personal-data policy at `/privacy` and tells the visitor about cookies
on the first visit. Both are interface features, not API calls: the page renders statically,
needs no session and no request, and opens by a direct link.

- **The policy** (`src/pages/privacy-page.tsx`) is the operator's document under part 2 of
  article 18.1 of 152-ФЗ: eleven sections — general provisions, legal grounds, data categories,
  purposes and retention, processing order, cookie, transfers, security measures, the subject's
  rights, changes and the operator's details. It is a lazy chunk; `#cookies` is a public anchor
  used by the notice, the footer and the registration form.
- **One source of truth for cookies.** `COOKIE_REGISTRY` in `src/lib/legal.ts` lists every cookie
  the service sets — session, refresh, antiforgery, the «Запомнить меня» hint and the notice's own
  acknowledgement — with purpose, lifetime and attributes. The policy renders that array, so a new
  cookie cannot appear in code without appearing in the document.
- **The consent record.** `PRIVACY_POLICY_VERSION` equals `PRIVACY_POLICY_DATE` (`2026-11-03`) and
  the backend's `PrivacyPolicy.Version`; the registration form sends it and the server stores it
  with the UTC time and the IP. §2.2 of the policy states what is recorded, §4.1 that the record is
  stored with the account. Editing the policy text bumps both constants and the server constant —
  otherwise registrations would start failing with `auth.register.consentVersion`.
- **The notice** (`src/components/cookie-notice.tsx`) is informational, not a consent banner: the
  service sets only strictly necessary first-party cookies, so there is no «Отказаться» button and
  no cookie wall. It is a bar under the header with one «Понятно» action and a link to
  `/privacy#cookies`; the acknowledgement lives in `barbersearch.cookie-notice` for a year, so the
  bar is not shown again. It renders in `App.tsx` above `<main>`, so it appears on every route —
  including `/admin`, where the footer is hidden — and, being in flow, it never covers the bottom
  controls of a page.
- **The footer** carries «Политика обработки данных» and «Cookie» on every page that shows it.
- **Registration** requires the consent checkbox before the request (see above).

The operator's details are placeholders in `LEGAL_ENTITY` (`src/lib/legal.ts`): the full name,
ИНН, ОГРН/ОГРНИП, address, contact email and phone must be filled before the domain goes
public — the client cannot invent them. `PRIVACY_POLICY_DATE` changes with any edit to the text.

Files: `src/lib/legal.ts` (paths, cookie registry, operator details),
`src/lib/cookies.ts` (client cookie read/write, shared with the «Запомнить меня» hint),
`src/lib/cookie-notice.ts` (notice acknowledgement), `src/components/cookie-notice.tsx`,
`src/components/site-footer.tsx`, `src/pages/privacy-page.tsx`.

## Admin reviews

- `src/pages/admin-page.tsx` — route parsing, role gates, shell.
- `src/components/admin/` — rail, tabs, queue, detail, rules dialog, states.
- `src/lib/admin-reviews.ts` — `GET /api/admin/reviews`, `GET /api/admin/reviews/{id}`,
  `POST/DELETE /api/reviews/{id}/publication`.
- `src/lib/admin-path.ts` — `/admin` paths and the selected review in the URL.

The screen never shows a total count (the API does not provide one), never edits the client's
text, and never offers bulk publication. The queue is paged twenty rows at a time through the
shared pager instead of growing: the tab (`pending`/`published`) is its own server list, so
switching tabs starts from the first page, «Назад» uses the in-memory page and a next-page error
keeps the rows. The range is «21–40» without «из N». The selected review lives in the URL and is
not reset by a page change; on a narrow screen the list stays on its page when the detail card
opens and closes. The «Обновить» toolbar keeps its quiet refresh (`isRefreshing`, `refreshError`,
«Обновлено в HH:MM») without clearing the queue.

## Admin barbers

- `src/components/admin/barbers-screen.tsx` — filter, debounced search, split view, states.
- `src/components/admin/barber-list.tsx`, `barber-detail.tsx` — table/cards and the card with
  contacts, the readiness checklist and the actions.
- `src/lib/admin-barbers.ts` — `GET /api/admin/barbers[?status&search]`,
  `GET /api/admin/barbers/{id}`, verification/grade/activation calls.
- `src/hooks/use-admin-barber-list.ts`, `use-admin-barber.ts` — list and card state.

The list is paged twenty rows at a time through the shared pager; the status filter and the
debounced search are part of the server request, so changing either starts from the first page and
drops the cached pages. The filter is in the URL (`?status=`), together with the card, so a reload
keeps the pair and the card can never show a profile from another filter: switching the filter
navigates to the list without an id (the first row of the new filter opens on a wide screen), and
an applied search drops the previous selection the same way. Actions update the affected row
locally (or remove it when it no longer matches the filter) and then re-sync quietly; the selected
barber in the URL is not reset by a page change, and the mobile split keeps the list on its page.

«Скрыть из поиска» reuses the existing activation flag — there is no separate hidden state in
the domain. Confirming a hidden profile is rejected by the domain, so the button explains that
the profile must be returned to search first.

## Admin users

`/admin/users` lists the users through `GET /api/admin/users` with status and role filters and a
debounced name/email search, twenty rows per page through the same shared pager. Filters and search
start from the first page; the selected user lives in the URL so paging does not close the card,
and the mobile split keeps the list on its page. Role change, block/unblock and email/phone
confirmation update the row locally (or remove it when it leaves the active filter) before the
quiet re-sync. The screen never shows a total count, because the API does not return one.

The card has a read-only «Согласие на обработку ПД» row: «Редакция 2026-11-03 · 03.11.2026, 14:05
UTC» for a self-registered account, or «Записи нет — аккаунт создан до 03.11.2026» for accounts
that predate the record and for users created by an administrator. The IP of the consent is not
returned by the API and is never displayed.

## Admin catalogue

`/admin/catalogue` shows three dictionaries — services, specializations and salons — in three
tabs. The tab lives in `?tab=services|specializations|salons` (`services` by default, so
`/admin/catalogue` is the canonical address) and survives a reload and «Назад»; an unknown value
falls back to services instead of «Раздел не найден». The catalogue is the fourth rail item, after
«Пользователи» (the users/catalogue order was aligned with the handoff), and the page is `noindex`
like the rest of the admin.

- `src/lib/admin-catalogue.ts` — three admin reads, all mutations of the existing endpoints, the
  labels and `describeAdminCatalogueError`; the four salon status filters and the
  approve/reject calls.
- `src/lib/salon-form.ts` / `src/components/salon/salon-form.tsx` — the salon fields, validation and
  the dialog form shared with the barber cabinet (the admin dialog is now a thin wrapper over it).
- `src/lib/response-guards.ts` — `isAdminHairServiceList`, `isAdminSpecializationList`,
  `isAdminSalonList`.
- `src/hooks/use-admin-catalogue.ts` — the list controller: `status`/`search` are part of the
  request, a late answer of a cancelled request is dropped by generation, quiet `refresh()`,
  `removeRow`/`replaceRow` for local moderation updates, and a discriminated union so a table only
  sees rows of its own tab.
- `src/components/admin/catalogue-screen.tsx` — header, tabs, status filter, debounced search, the
  active panel and the dialogs; `catalogue-tabs.tsx`, `catalogue-table.tsx`,
  `hair-service-dialog.tsx`, `specialization-dialog.tsx`, `salon-dialog.tsx`,
  `salon-moderation-dialog.tsx` (approve/reject), `catalogue-action-dialog.tsx` — the parts.
- `src/lib/admin-path.ts` — `catalogue` route, `CatalogueTab`, `cataloguePath()`,
  `parseCatalogueTab()`.

What it does:

- Shows the **full** dictionary, including rows that are switched off: `status=all` is the default
  precisely so a hidden row can be found and switched back on. The status filter is one segmented
  `role="group"` shared by the tabs; changing a tab resets the search but keeps the status. Search
  is debounced by 300 ms, capped at 100 characters, and hits the name (salons — name and city).
- Only the «Салоны» tab has a fourth filter «На проверке» (`status=pending`) — the moderation queue.
  Hair services and specializations have no moderation and show no such filter (the server rejects
  `pending` for them with `400 admin.catalogue.status`). `active` means `isActive && moderationStatus
  === 'Active'`, `hidden` means `!isActive && Active`, so the queue is never mixed into «Активные» or
  «Выключенные».
- Salon rows carry the owner email (`ownerEmail`, `null` for an admin-created salon) under the name
  and a moderation badge («Одобрен» / «На проверке» / «Отклонён», with the rejection reason). Pending
  rows offer «Одобрить» and «Отклонить» in an `alertdialog`: approval explains «Салон появится в
  поиске и станет доступен для записи», rejection requires a reason of at most 500 characters
  (`salon.reject.reasonRequired` / `salon.reject.reasonTooLong`). After `204` the row is removed
  from the `pending` filter (or replaced in `all`) and the list refreshes quietly; a `409
  salon.review.invalidStatus` means someone already decided and the list is re-read. Activation is
  hidden for non-approved rows: a pending salon cannot be switched on, it must be approved.
- «Барберы» is `usageCount` from the API: the number of **active and verified** barber profiles that
  use the row right now (for a service, only active `barber_services` links count). The screen never
  derives it from the array length and shows no other counters.
- The status is text («Активна»/«Выключена», «Активен»/«Выключен» for salons), not only a colour.
  On `lg` and wider the rows are a `<table>` with a `sr-only` `<caption>` and `th scope="col"`; on
  narrower screens they become cards. Neither produces horizontal scroll at 320 px.
- Create and edit run through existing endpoints: `POST/PUT /api/hair-services`,
  `POST/PUT /api/specializations`, `POST /api/salons` and the two independent salon `PUT`s. The
  service category cannot change after creation (the domain forbids it) and the salon name is
  read-only («Название задаётся при создании») because the domain has no rename. The time zone is
  a native `<select>` over `Intl.supportedValuesOf('timeZone')` with the current value always
  present — a typo would break slot calculation (`salon.timeZoneId.unknown`).
- A salon edit sends `PUT /salons/{id}/location` and then `PUT /salons/{id}/contact-info`. If the
  second call fails the first one has already been applied: the dialog shows the error, refreshes
  the list and does **not** claim «Сохранено».
- «Выключить» is an `alertdialog` that names the consequence and, for services and salons, the
  number of barbers; «Включить» is a plain confirmation. After `204` the list refreshes quietly, the
  row keeps its place, focus returns to its button and the action is announced in `role="status"`.
  `409`/`404` are shown in the dialog and the list is re-read.
- There is **no delete**: the domain only deactivates, and there are no bulk actions. Catalogue
  edits **are** written to `audit_log` as `salon.*`, `hair_service.*` and `specialization.*` rows,
  so they appear in «Admin audit»; retention and export remain separate slices. The catalogue
  screen itself adds no journal UI.

## Admin bookings

`/admin/bookings` is the support desk for every customer's visits: before this screen the admin saw
bookings only as identifiers from `GET /api/bookings` and could not find a record by the email the
customer quotes in a ticket. The rail gained the sixth item before «Журнал», the detail lives at
`/admin/bookings/{id}`, and the page is `noindex` like the rest of the admin.

- `src/lib/admin-bookings.ts` — types, `ADMIN_BOOKING_STATUS_FILTER_LABELS`, the list and detail
  reads (`GET /api/admin/bookings`, `GET /api/admin/bookings/{id}`), the two action calls
  (`POST /api/bookings/{id}/cancellation` and `/no-show` through `mutateWithCsrf`),
  `formatAdminBookingTime` and both error mappings.
- `src/lib/response-guards.ts` — `isAdminBooking` / `isAdminBookingPage`: a known status, string
  dates, a numeric price, nullable notes and `cancellationReason`, the customer/barber/service/salon
  nested shapes and a non-empty salon `timeZoneId`; extra fields are ignored.
- `src/hooks/use-admin-bookings-list.ts` — `usePagedList` with `requiresAuth: true`; the search and
  status filter are part of the request, so changing either starts the first page.
  `src/hooks/use-admin-booking.ts` — the detail by id with `loading/ready/notFound/error` and
  `reload()`.
- `src/components/admin/bookings-screen.tsx`, `booking-detail.tsx` — the list with its filters and
  the split-view card; `src/lib/admin-path.ts` — the `bookings` route, `bookingsPath()`,
  `isBookingId()`.

What it shows:

- The list is a `<table>` from `lg` («Время», «Клиент», «Барбер», «Услуга», «Статус», «Цена») and
  cards below it, with a link from each row to the detail. The time is rendered in the **salon's**
  `timeZoneId`, not the browser's. The page has no horizontal scroll at 320 px.
- Filters: a `type="search"` field for the customer email or the exact booking id (debounced by
  300 ms, capped at 100 characters), a native status select with six options («Все статусы»,
  «Ждёт подтверждения», «Подтверждена», «Завершена», «Отменена», «Неявка»), and «Сбросить фильтры»
  when at least one filter is off its default. Any change starts a new first page.
- The detail is a split-view panel on desktop and a full-screen card on mobile: status badge, start
  and end in the salon zone, price, the customer's name, email (plain text, not `mailto:`) and
  phone, the barber, the service, the salon with its city, address and time zone, both notes,
  `createdAt` and the booking id.
- The list is paged by the shared pager (twenty rows, «Назад» without a request); there is **no**
  total count, no «Показать ещё» and no row counters, because the API returns no `totalCount`.

What it can do:

- «Отменить бронь» is offered for `Pending` and `Confirmed`; «Отметить неявку» only for `Confirmed`
  bookings whose `startAt` has already passed. Both open an `alertdialog` that names the
  consequence, show a `role="status"` message after `204`, reload the card and the list, and map a
  refusal (`booking.cancel.invalidStatus`, `booking.markNoShow.tooEarly`,
  `booking.markNoShow.invalidStatus`) to Russian copy in `role="alert"`.
- The two actions are the existing `POST` endpoints; the screen adds **no new mutation**. There is
  no create for a customer, no reschedule, no edit of the price, service or barber note, no delete
  and no refunds or payments — the domain has none of them, and the barber's note is read-only for
  the admin. Terminal bookings show no actions, and rows have no action buttons at all.

What it cannot do, on purpose:

- The email search, the status filter and the text stay in React state and are **not** written to
  `?…`: the email is PII and does not belong in browser history or proxy logs. Only the booking id
  is in the address, because a direct link to a booking from a ticket is the point of the screen.
- **Booking changes are audited, booking creation is not.** `AuditTrail` writes a `booking.updated`
  row for every status or barber-note change — including the admin cancellation and no-show on this
  screen — so they appear in `/admin/audit` under the «Бронь» filter. Creating a booking writes
  **no** row: `AuditsCreation: false`, because the booking already stores the customer, the time
  and the price, and a line per booking would grow the journal without new information. Retention
  and export remain separate slices.
- Filters by date, barber and salon, bulk operations, a calendar/timeline and CSV export are not
  part of this screen.

The API contract behind it: `GET /api/admin/bookings?status=&search=&cursor=&pageSize=` returns
`PagedResponse<AdminBookingResponse>` ordered `start_at DESC, id ASC` with an opaque `(start_at, id)`
keyset cursor. `status` compares against the `BookingStatus` names without case (`all` is the
default, numbers are rejected); `search` is a customer-email substring with `%`, `_` and `\` escaped
**or** the exact booking id when the value parses as a GUID — not a name, a phone number or an id
prefix. `GET /api/admin/bookings/{id}` answers `404 admin.bookings.notFound` for a missing row. The
DTO carries the customer's email and phone: they exist only in this admin contract, and
`BookingResponse` used by the customer and barber reads is unchanged. Rows whose historical booking
points at a deleted user, profile, service or salon drop out of the list (the inner joins are the
same documented behaviour as the barber cabinet).

## Admin summary

`/admin/summary` is the «Что требует внимания» screen and the first rail item. It answers one
question — is anything waiting for an administrator — from a single aggregate read,
`GET /api/admin/summary`. The route is the exact `/admin/summary` (no card, no builder, no nested
paths) and the page is `noindex` like the rest of the admin. `/admin` still redirects to the review
queue: the rail is a map of sections, not a start page.

- `src/lib/admin-summary.ts` — `AdminSummary`/`AdminSummaryOldestReview` types,
  `fetchAdminSummary` (one parameterless `GET`, no `status`, `pageSize` or `cursor`) and
  `describeAdminSummaryError` (`403` → the role text, `429` → the rate-limit text, otherwise the
  shared Russian fallback; the English `detail` never reaches the DOM).
- `src/lib/response-guards.ts` — `isAdminSummary`: three mandatory blocks, `pendingCount`,
  `unverifiedCount` and `inactiveCount` must be non-negative safe integers, and `oldest` is either
  `null` or `{ id, rating 1..5, createdAt, barberDisplayName, customerDisplayName }`. Extra fields
  are ignored, so a wider contract keeps working.
- `src/hooks/use-admin-summary.ts` — one read with an `AbortController` and a request token:
  `reload` is the first load (skeleton), `refresh` is the quiet update that keeps the old numbers
  on screen and turns a failure into a `role="alert"` line; `401` sets `requiresSignIn`;
  `updatedAt` is the last success. StrictMode-safe, a stale response is dropped.
- `src/components/admin/summary-screen.tsx` — the `h1`, the subtitle, «Обновлено в HH:MM», the
  single «Обновить», and three `<section>`s separated by thin dividers.

What it shows:

- **«Отзывы на модерации»** — `pendingCount` («14 отзывов без публикации») and the oldest review:
  «Самый старый ждёт 3 дня», the rating, the client and the barber names, and «Открыть отзыв» →
  `/admin/reviews/{id}`; below, «К очереди отзывов» → `/admin/reviews`. The copy explains that both
  new reviews and reviews hidden by an administrator wait here — `is_published` is one flag.
- **«Барберы без подтверждения»** — `unverifiedCount` and «Открыть барберов» → `/admin/barbers`,
  which opens on the «На проверке» queue.
- **«Выключенные салоны»** — `inactiveCount` and «Открыть каталог салонов» →
  `/admin/catalogue?tab=salons`.
- Zeros are named honestly: «Очередь пуста — все отзывы проверены.», «Все профили подтверждены.»,
  «Выключенных салонов нет.».

The counters use the **same predicates** as the sections (`is_published = false`,
`is_verified = false` without `is_active`, `is_active = false`), so a number is exactly the size of
its queue; the oldest review is the first row of the same FIFO moderation page the queue uses.
There is deliberately no `total` in `PagedResponse` or in the existing lists: one screen needs the
number, and the shared pagination contract stays unchanged.

What it does **not** do: no mutations, no charts, periods, trends or comparisons with the past, no
KPI tiles, no «Показать ещё», no totals, no export and no audit writes. Customer email and phone
are not in the contract — only names; the full context stays in `GET /api/admin/reviews`.

## Admin audit

`/admin/audit` is the read-only change journal: when, who, what and on which entity changed, with
an expandable per-row diff. The journal is the last rail item, the route is the exact
`/admin/audit` (no card route and no builder — a row already carries its whole diff), and the page
is `noindex` like the rest of the admin.

- `src/lib/admin-audit.ts` — types, `ADMIN_AUDIT_ENTITY_TYPES`/`_LABELS`/`_ACTION_LABELS`/
  `_FIELD_LABELS`, `formatAuditAction` (the label comes from the tail after the last dot of
  `user.updated`), `formatAuditValue` (`null` → «—», booleans → «Да»/«Нет», `[redacted]` → «скрыто»,
  Russian role, grade, booking-status and service-category labels with the raw string as a
  fallback, via `bookingStatusLabel` and `SERVICE_CATEGORY_LABELS`), `fetchAdminAuditEntries` and
  `describeAdminAuditError`.
- `src/lib/response-guards.ts` — `isAdminAuditEntry`/`isAdminAuditPage`: non-empty ids and action,
  a nullable actor with a string id and nullable email, and changes whose `from`/`to` are JSON
  scalars or `null`. Extra fields are ignored.
- `src/hooks/use-admin-audit-list.ts` — `usePagedList` with `requiresAuth: true` and
  `mapError: describeAdminAuditError`; filters are part of the request, so any change starts the
  first page.
- `src/components/admin/audit-screen.tsx` — header, filters, states, list and pager;
  `audit-entry.tsx` — one row with the expandable diff.

What it does:

- Filters: a native `<select>` for the entity type («Все сущности», «Пользователь», «Барбер»,
  «Отзыв», «Салон», «Услуга», «Специализация», «Бронь»), a debounced (300 ms) author email search
  capped at 100 characters, and «С даты» / «По дату». The two dates are **inclusive UTC days** —
  the visible hint says so — while a row's time is shown in the browser's local time zone.
  «Сбросить фильтры» appears only when at least one filter is off its default. The filters live in
  React state and are **not** written to `?…`: the author email is PII and does not belong in
  browser history or proxy logs.
- The list is paged: twenty rows per page, «Дальше» only by the server's opaque `nextCursor`,
  «Назад» from the in-memory page cache, a next-page error that keeps the rows and turns the
  button into «Повторить». The range is «1–20» / «21–25» without «из N» — the API returns no total.
- A row shows `<time>` in local time, the actor (`actor.email`, «Система» when `actor` is `null`,
  «Удалённый пользователь» when the account is gone and `email` is `null`), the entity type plus
  the first 8 characters of `entity_id` (the full id is in `title`; the entity name is **not**
  resolved — it may have been renamed or deleted), and the action label from the tail of
  `action`. «Показать изменения» / «Скрыть изменения» is the only button in a row: it carries
  `aria-expanded` and `aria-controls`, keeps the focus and expands a `<dl>` of `field: from → to`
  with Russian field names («Роль: Клиент → Администратор», «Телефон: +70000000009 →
  +70000000010», «Статус: Подтверждена → Неявка»). Booking statuses and service categories are
  rendered in Russian, and `isActive` is «Активность» (it serves both a user and a catalogue row).
  An unreadable payload is honest: «Изменения не записаны.» Rows expand independently.
- States: skeleton with `aria-busy`, «Журнал пуст» without filters / «Ничего не найдено» with
  them, a first-page error with its reason and «Повторить» (never the English `detail` or the
  status code), a next-page error in the pager without clearing the rows, `401` → the sign-in gate
  with `returnTo="/admin/audit"`, `403` → «Раздел доступен только администратору.».
- The journal is **immutable**: there is no write endpoint, no «Изменить», «Выключить», «Удалить»
  or «Очистить журнал», and no «Показать ещё». `AuditTrail` writes the table on every
  `SaveChanges`; the screen only reads.
- The journal covers **seven** entity types — `User`, `BarberProfile`, `Review`, `Salon`,
  `HairService`, `Specialization` and `Booking`. Booking rows carry only `Status` and `BarberNote`
  changes: creating a booking is deliberately not audited (`AuditsCreation: false`), and
  `BarberProfile` children (services, salons, schedules, photos, time off) are not covered either.
  The journal stays immutable and read-only; retention, partitioning and export are still separate
  slices, and catalogue deletes do not exist in the API.

## Testing

Two layers, both in CI: Vitest for the logic, Playwright for the browser.

### Unit — `npm test`

Vitest + jsdom + Testing Library, with MSW as the only network boundary (`src/test/msw/**`;
`onUnhandledFrame: 'error'`, so an unmocked request fails the test). It runs in about 15 seconds,
needs no API and no database, and covers:

- `apiRequest` — successful and `204` bodies, broken JSON, `ProblemDetails` and the strict guard,
  transport failure, `validate`, and the single `401` retry (never on `/auth/*`);
- `describeSignInError` — the whole §9.5 table, including the order `429` → codes → `401`;
- `LoginDialog` — client validation, pending state, success, mapped server errors, labels and ARIA;
- `SessionProvider` — bootstrap outcomes, the «Запомнить меня» refresh, `unavailable`,
  `retrySession`, sign-in/sign-out, the hint cookie and exactly one `/auth/me` under `StrictMode`;
- `admin-catalogue` — the three list URLs (`status` always, `search` only when non-empty, `all` by
  default), the strict list guards accepting extra fields and rejecting a broken body,
  `DELETE /activation` with `X-CSRF-TOKEN` and one retry on `auth.csrf.invalid`, the complete
  `POST /api/salons` body, and the error mapping for a concurrency conflict, an unknown time zone
  and an unknown failure.
- `admin-audit` — the URL contract (`entityType` always, `actor`/`from`/`to` only when non-empty,
  the cursor last and no `pageSize`), the strict page guard accepting extra fields and rejecting a
  broken body and a non-scalar `from`, `formatAuditValue` (`null`, booleans, `[redacted]`, role and
  grade labels, the five Russian booking statuses, the service categories, unknown values),
  `formatAuditAction` by the action tail, the exact order of the eight entity types and their
  labels (the new four are appended), and the error mapping for `403`, `429` and
  `admin.audit.cursor.invalid`. `AuditEntry` (colocated with the component) covers `<time>`, the
  short id with the full id in `title`, «Система» and «Удалённый пользователь», the expandable
  `<dl>` diff «Роль: Клиент → Администратор», «Активность» for `isActive`, the booking diff
  «Статус: Ждёт подтверждения → Отменена», the service «Название: … → …» diff, the empty-diff
  text and independent expansion; `AuditScreen` is an MSW-backed screen test: the loaded rows and
  pager range, the entity-type filter starting a new first page without a cursor, a switch to
  «Салон» sending `entityType=salon`, «Журнал пуст» and the `403` text.
- `admin-bookings` — the list URL (`status` always, `search` and `cursor` only when set, no
  `pageSize`), the detail read by id, the CSRF `POST`s for cancellation and no-show, the strict
  guards (a bad status, a string price or a missing salon time zone is a broken body; extra fields
  are fine), `formatAdminBookingTime` in the salon zone, the filter labels and the server-matching
  page size and search limit, and both error mappings. `BookingsScreen` covers the rows, a filter
  change starting a new first page, the next-page error keeping the rows, the empty states and the
  `403` text; `BookingDetail` covers the salon-zone time, the actions by status, both confirmation
  texts, the `204` result and the Russian error.
- `admin-summary` — exactly one parameterless `GET /api/admin/summary`, the strict guard rejecting
  a negative or fractional counter, a missing block and a malformed `oldest` while accepting
  `oldest: null` and extra fields, and the `403`/`429`/fallback error mapping. `SummaryScreen`
  covers the three blocks with their numbers, links and copy, the honest zeros, the `401` gate, the
  first-page error with «Повторить», the refresh error that keeps the blocks, exactly one request
  per «Обновить» and the «Обновлено в …» label; the `formatProfilesCount`/`formatSalonsCount`
  plural forms are covered directly.

Tests are colocated with the module (`src/lib/api.test.ts`, `src/components/login-dialog.test.tsx`)
and compare Russian copy as a whole string, saying in the test title that it is copy.

Not covered by the unit layer: most screens and hooks (only the audit and admin-bookings screens have
direct tests), and everything that needs the real API, router, cookies or focus — that is the E2E
layer below.

## E2E

Playwright drives the real SPA against the real API and a real PostgreSQL: there is no second test
stack and no mocked success responses. The suite covers the paged lists, the admin journal, the
admin bookings desk and the admin summary, sign-in, public reviews, the keyboard paths and an axe
scan of every screen and role in two widths (1280 and 320). `npm test` (Vitest) stays separate and
fast; E2E never runs inside it.

### One-time setup

```bash
npm run test:e2e:install          # Chromium
```

A separate database is mandatory — never `barbersearch` and never `barbersearch_load`:

```bash
createdb -h localhost -U postgres barbersearch_e2e
```

The API must be running on `http://localhost:5243` (the `http` profile, which Vite proxies):

```bash
# from ../backend
ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=barbersearch_e2e;Username=postgres;Password=postgres" \
  dotnet run --project BarberSearch/src/BarberSearch.Web --launch-profile http
```

Migrations and both seed files (the backend's minimal seed plus the E2E fixtures):

```bash
# from ../backend, with ConnectionStrings__Postgres pointing at barbersearch_e2e
dotnet ef database update \
  --project BarberSearch/src/BarberSearch.Infrastructure.Postgresql \
  --startup-project BarberSearch/src/BarberSearch.Web

# from frontend
psql -h localhost -U postgres -d barbersearch_e2e -v ON_ERROR_STOP=1 -q \
  -f ../backend/load/seed/minimal.sql
npm run test:e2e:seed             # wrapper around e2e/fixtures/seed.sql
```

`npm run test:e2e:seed` reads `E2E_SEED_DATABASE_URL`, then `ConnectionStrings__Postgres`, then a
local default — and refuses to run against any database except `barbersearch_e2e` (the SQL file
checks the name itself as well). Rerun it whenever the fixtures could have been mutated: the tests
that edit, delete or publish restore their own rows, but the seed is the authoritative reset.

### Running

```bash
npm run test:e2e          # desktop (1280×900), mobile (375×812) and narrow (320×800)
npm run test:e2e:ui
```

Playwright starts `npm run dev` itself when nothing is listening on 5173 and reuses a running server
outside CI. If the API is down, `e2e/global-setup.ts` fails immediately with the command to start it
instead of silently skipping.

| Project | Viewport | What runs there |
|---|---|---|
| `auth` | — | signs the six fixture accounts in through the API and writes `e2e/.auth/*.json` |
| `desktop-chromium` | 1280×900 | every spec except the 320 px, `a11y-narrow` and mutation ones |
| `mobile-chromium` | 375×812 | the same on a phone, without the axe specs (the gate runs 1280 and 320) |
| `narrow-320` | 320×800 | `pager-320.spec.ts` and `a11y-narrow.spec.ts` |
| `mutations-chromium` | 1280×900 | the specs that edit, delete or publish rows, last, once |

### Accessibility gate

`e2e/specs/a11y.spec.ts` scans every screen and role at 1280, `e2e/specs/a11y-narrow.spec.ts` the
narrow branches at 320. Only `critical` and `serious` fail the run; `moderate`/`minor` findings are
written to the Playwright report as `axe-advisory` annotations. There is **no allow-list**: a known
finding is fixed or gets a new number in `AGENT.md` §14 and an entry that cites it — an exception
without a debt number is not allowed.

### Fixtures and accounts

`e2e/fixtures/seed.sql` creates fixed UUIDs: one showcase barber with 12 published reviews, 52
history bookings and 21 upcoming ones, two customers with 12 reviews each (the `e2e-customer` also
gets 21 upcoming and 21 history bookings, so both `/account` tabs have two pages), an admin, twelve
extra barbers in one city (so search has a second page), an applicant without a request for the
`/become-a-barber` flow, a barber without a profile for onboarding, a support customer for the
admin bookings and **28 users in total** (so `/admin/users` has two pages; the CI
`minimal.sql` adds `ci-owner`, making 29). One of them is the profile **without confirmation**
«E2E Барбер Без Подтверждения»
(no services and no salon, id `9e100000-…-14`): it is the single positive row of the summary's
«Барберы без подтверждения» block and of the «На проверке» queue, and because it is unverified it
never reaches the public search, cards or catalogue counters. For the admin catalogue it adds
**hidden** rows only — the service «E2E Борода», two
specializations and the salon «E2E Studio Nord»: an active row would land in the public filters and
cards and shift the counters of `search-login`, `a11y` and the barber cards, while hidden rows are
invisible to public reads.
For the admin journal it adds exactly **25 `audit_log` rows on 2026-09-10 UTC** (ids
`9e700000-…-01`…`-25`, the newest one without an actor): the original 21 rows cover users,
barber profiles and reviews, and rows 22…25 are the four types added in #41 — `salon.updated`
(`10:16:10`, a phone change), `hair_service.updated` (`10:16:20`, `isActive` true→false),
`specialization.updated` (`10:16:30`, a rename) and `booking.updated` (`10:16:40`, `Confirmed` →
`NoShow`). Their times are inside the same UTC day and before `10:17` on purpose: the freshest
system row (21st, `10:21`) and the role-change row (20th, `10:20`) must stay first, while the
second page becomes «21–25» and starts at row 05 (`…-0005`). The fixed past date keeps the
journal checks away from the rows that mutating specs write «today», and the section deletes only
its own ids before inserting, so the seed stays idempotent. There are no other changes to
`audit_log`.
For the admin bookings screen it adds one customer (`e2e-bookings@barbersearch.test`, id
`9e000000-…-09`) and exactly **24 bookings on 2026-09-12** with fixed ids `9e900000-…-01`…`-24` at
30-minute steps: 18 `Pending`, 3 `Confirmed`, one `Completed`, one `Cancelled` and one `NoShow`.
The fixed past date makes the order deterministic, keeps «Отметить неявку» available (the visit has
started) and gives two pages of 20 + 4; booking 01 carries both notes. The section deletes only its
own ids before inserting, so the seed stays idempotent, and the other fixture bookings stay
untouched — `account-bookings*`, `barber-cabinet*` and the mutating specs depend on them.
For the barber onboarding it adds `e2e-new-barber@barbersearch.test` (id `9e000000-…-05`) with the
role `Barber` and **no profile** — the only account that sees the missing-profile gate and
`/barber/onboarding`. Its block is idempotent in a second, stronger sense: the seed first clears
`users.barber_profile_id` and then deletes the profile created by the previous run (the FK is
`ON DELETE RESTRICT`), otherwise a run without a reseed would get `409
barberProfile.create.alreadyExists` instead of the form. Because the barber has no profile it
changes none of the admin counters. The onboarding spec signs in through the dialog rather than
reusing `storageState`: creating the profile rotates the refresh token, and a stored state would
carry an already rotated one.
All accounts use the password `e2e-password` — a test constant, not a secret, and the hash is the
only one copied into the fixture. Emails end with `@barbersearch.test`; nothing real is used.

Credentials and the API URL can be overridden with `E2E_CUSTOMER_EMAIL`, `E2E_CUSTOMER_PASSWORD`,
`E2E_BARBER_*`, `E2E_NEW_BARBER_*`, `E2E_ADMIN_*`, `E2E_MUTATIONS_*`, `E2E_APPLICANT_*`,
`E2E_BASE_URL`, `E2E_API_URL`.

### Artifacts

`playwright-report/` (html) and `test-results/` (traces, screenshots, video of failures) are written
on every run and are git-ignored. `e2e/.auth/` holds the `storageState` files and is ignored too.

### Notes

- Each test gets its own `X-Forwarded-For` so the API read limiter (120 requests per minute per IP)
  does not fail an otherwise healthy run; the limiter itself is untouched (`e2e/fixtures/test.ts`).
- Russian copy is asserted on purpose in a few tests; they say so in a comment.
- Mutating specs restore what they change, so a second `npm run test:e2e` is green without a reseed.

## Layout

- `AGENT.md` — durable frontend rules and reference for AI agents; read it before changing the
  project
- `src/App.tsx` — shell: header, page switch, footer
- `src/pages/` — `home-page`, `account-page`, `account-reviews-page`, `barber-cabinet-page`,
  `register-page`, `admin-page`, `barber-page`
- `src/components/ui` — shadcn/ui components, add more with `npx shadcn@latest add <name>`
- `src/lib/utils.ts` — `cn` helper
- `components.json` — shadcn/ui config and aliases (`@/`)
- `ADMIN_PAGE_HANDOFF.md` — admin design concept and stage plan

## Agent skills

Project skills live in `../.agents/skills` (React, TypeScript, Tailwind, shadcn, Playwright).
Update them with `npx skills check` / `npx skills update` from the repository root.
