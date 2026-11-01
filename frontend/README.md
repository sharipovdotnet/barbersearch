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
| `/account` | customer cabinet: bookings, cancellation, review, password |
| `/account/reviews` | author's own reviews: both statuses, edit and delete |
| `/barber` | barber cabinet: client bookings, confirm / complete / no-show / cancel / note |
| `/register` | customer self-registration; a signed-in visitor sees the «Вы уже вошли» gate |
| `/admin` | admin shell; redirects to `/admin/reviews` |
| `/admin/reviews` | review moderation queue (pending / published tabs) |
| `/admin/reviews/{id}` | one review; the tab is in `?tab=published` |
| `/admin/barbers` | barber queue: pending / all / hidden, search by name or email |
| `/admin/barbers/{id}` | one barber: contacts, readiness checklist, verify/grade/hide |
| `/admin/users` | user list: status/role filters, search by name or email |
| `/admin/users/{id}` | one user: role, block/unblock, email/phone confirmation |

The customer cabinet, the barber cabinet and the admin section are client-side gated; the API
enforces roles separately (`AdminOnly`, `BarberOnly`).

## List pagination

Lists that show records in rows share one controller (`src/hooks/use-paged-list.ts`) and one footer
(`src/components/list-pager.tsx`): ten or twenty rows per page, «Дальше» only by the opaque
`nextCursor` from the response, visited pages cached in memory so «Назад» makes no request, and the
range from `formatPageRange()` in `src/lib/paging.ts` («11–20 из 32» only where the server returns a
total, otherwise «21–40»). A next-page error keeps the visible rows and turns the button into
«Повторить»; filters, tabs and search start a new first page.

Two lists deliberately keep «Показать ещё»:

- `/` — the search results are a grid of cards; appending keeps them comparable, while pages would
  hide barbers the visitor has already seen;
- `/account` — «Мои записи» splits one server list into «Предстоящие»/«История» on the client, so
  paginating the already-split list would hide bookings on another page. It needs a server-side
  `scope` first (AGENT.md §14 #26).

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
request at all. Creating a review stays in the customer account
(`POST /api/reviews` on a completed booking, hidden until moderation); editing and deleting go
through the author list at `/account/reviews`.

- `src/lib/reviews.ts` — review DTO, page size 10, `fetchBarberReviews()`.
- `src/hooks/use-paged-list.ts` — the shared cursor-page controller (cache, «Назад» without a
  request, abort + generation, next-page error, silent refresh).
- `src/components/list-pager.tsx` — the shared pager footer (range, focus, `aria-live`, retry).
- `src/components/barber/barber-reviews.tsx` — section, summary, states, stars and the pager.
- `src/components/review-stars.tsx` — the shared five stars, used by both review lists.

## My reviews

`/account/reviews` is a separate lazy screen (not a tab of `/account`). It calls
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
- `src/lib/account-path.ts` — `/account` and `/account/reviews` path helpers.

## Barber cabinet

`/barber` is a separate lazy screen for a signed-in `Barber` with a linked profile. It calls
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
always; terminal bookings are read-only. The customer's comment and the barber's note render as
text.

Mutations wait for the server and update the row locally — there is no second `GET`: confirm
changes the status to «Подтверждена»; complete, no-show and cancel remove the row from
«Ближайшие» and keep it in «История» with the new status; the note dialog (1000 characters, an
empty field clears the note) updates `barberNote`. Every write sends `X-CSRF-TOKEN`; the list
read does not. Double clicks are ignored while a request is in flight.

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

The header shows «Кабинет барбера» for a barber and «Мои записи» for everyone else. On
`/account` a barber still sees the customer-cabinet gate, now with a button to the cabinet.

- `src/pages/barber-cabinet-page.tsx` — gates, title, list composition and announcements.
- `src/components/barber/barber-cabinet-gates.tsx`, `cabinet-booking-list.tsx`,
  `cabinet-booking-row.tsx`, `barber-note-dialog.tsx`, `barber-booking-action-dialog.tsx`.
- `src/hooks/use-barber-bookings.ts` — scope, the shared pager controller, five local mutations,
  `401`, click guard.
- `src/lib/barber-bookings.ts` — DTO, list read and the five write actions;
  `src/lib/barber-cabinet-path.ts` — the `/barber` path helper.

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
Review creation on the public profile is not implemented: it lives in the customer account. The
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
deliberately not `HttpOnly`. It is the only cookie JavaScript reads, and it carries nothing but
`'1'` — forging it only causes one failed refresh, and no `localStorage`/`sessionStorage` is
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

The form sends one `POST /api/auth/register` with `{ email, password, firstName, lastName }` and
**no** `X-CSRF-TOKEN` (the action has no antiforgery filter, like booking creation). The email
and names are trimmed; the password is not. Client checks before the request: names are not
empty, email matches `^[^\s@]+@[^\s@]+\.[^\s@]+$` and is at most 254 characters, the password is
12–128 characters and differs from the email, its local part and both names, and the repeat
matches. The top-10k password dictionary is a server check only — its public code is mapped to
Russian copy, the dictionary is not reproduced on the client.

On `201 { id }` the form signs in with the same credentials through the usual CSRF path
(`GET /api/auth/csrf` → `POST /api/auth/login/cookie` → `GET /api/auth/me`) and navigates to
`/account`; the role is always `Customer` and `isEmailVerified` is `false`. If the auto sign-in
fails, registration is **not** repeated (a second attempt would be `409`): the form is replaced
by the «Аккаунт создан» card with a `LoginDialog` (`redirectTo="/account"`).

Public codes are mapped at the field or the form: `user.create.emailTaken` (409),
`auth.password.required` / `tooShort` / `tooLong` / `common` / `equalsEmail` / `equalsName`,
`user.email`, `user.firstName`, `user.lastName`; `429`, offline, `5xx` and anything else have
their own Russian text. The English `detail` is never shown. Both registration and sign-in use
the auth rate limiter (10 requests per minute per IP), so a registration with its auto sign-in
spends two of them. The new account is active immediately; email confirmation is out of scope.

- `src/pages/register-page.tsx` — title, loading skeleton, anonymous card and the signed-in gate.
- `src/components/register-form.tsx` — fields, client/server errors, submit guard and the
  «Аккаунт создан» card.
- `src/lib/auth.ts` — `RegistrationInput` and `register()`; `src/lib/register-path.ts` —
  `REGISTER_PATH` / `isRegisterPath`.

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
drops the cached pages. Actions update the affected row locally (or remove it when it no longer
matches the filter) and then re-sync quietly; the selected barber in the URL is not reset by a page
change, and the mobile split keeps the list on its page.

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
  `retrySession`, sign-in/sign-out, the hint cookie and exactly one `/auth/me` under `StrictMode`.

Tests are colocated with the module (`src/lib/api.test.ts`, `src/components/login-dialog.test.tsx`)
and compare Russian copy as a whole string, saying in the test title that it is copy.

Not covered by the unit layer: `HeaderAuth` states, the screens and hooks, and everything that
needs the real API, router, cookies or focus — that is the E2E layer below.

## E2E

Playwright drives the real SPA against the real API and a real PostgreSQL: there is no second test
stack and no mocked success responses. The suite covers the six paged lists from #31, sign-in,
public reviews and an axe scan of four screens. `npm test` (Vitest) stays separate and fast; E2E
never runs inside it.

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
that edit and delete restore their own rows, but the seed is the authoritative reset.

### Running

```bash
npm run test:e2e          # desktop (1280×900), mobile (375×812) and the 320 px pager project
npm run test:e2e:ui
```

Playwright starts `npm run dev` itself when nothing is listening on 5173 and reuses a running server
outside CI. If the API is down, `e2e/global-setup.ts` fails immediately with the command to start it
instead of silently skipping.

| Project | Viewport | What runs there |
|---|---|---|
| `auth` | — | signs the four fixture accounts in through the API and writes `e2e/.auth/*.json` |
| `desktop-chromium` | 1280×900 | every spec except the 320 px and mutation ones |
| `mobile-chromium` | 375×812 | the same on a phone |
| `narrow-320` | 320×800 | `pager-320.spec.ts` only |
| `mutations-chromium` | 1280×900 | the specs that edit or delete rows, last, once |

### Fixtures and accounts

`e2e/fixtures/seed.sql` creates fixed UUIDs: one showcase barber with 12 published reviews, 52
history bookings and 21 upcoming ones, two customers with 12 reviews each, an admin, twelve extra
barbers in one city (so search has a second page) and 25 users in total (so `/admin/users` has two).
All accounts use the password `e2e-password` — a test constant, not a secret, and the hash is the
only one copied into the fixture. Emails end with `@barbersearch.test`; nothing real is used.

Credentials and the API URL can be overridden with `E2E_CUSTOMER_EMAIL`, `E2E_CUSTOMER_PASSWORD`,
`E2E_BARBER_*`, `E2E_ADMIN_*`, `E2E_MUTATIONS_*`, `E2E_BASE_URL`, `E2E_API_URL`.

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
