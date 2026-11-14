# BarberSearch mobile

.NET MAUI client for iOS and Android (`.NET 10`, `net10.0-android` / `net10.0-ios`).

The app is the **client** half of the product: search for a barber, the public card, the slot
grid, booking, the customer's bookings and favourites. The barber cabinet and the admin section
stay on the web — a barber does not fill in a weekly schedule from a phone.

## Layout

```
mobile/
├─ BarberSearch.Mobile.sln          solution (app, core, tests)
├─ Directory.Build.props            nullable/analyzers/warnings-as-errors, same rules as backend
├─ .editorconfig                    formatting and analyzer exceptions, same rules as backend
├─ global.json                      SDK 10.0.303
├─ NuGet.config                     nuget.org for everything under mobile/
├─ src/
│  ├─ BarberSearch.Mobile/          MAUI app
│  │  ├─ Pages/                     Shell tabs: Поиск · Карта · Записи · Профиль
│  │  ├─ AppShell.xaml              tab bar; tab content is assigned from DI
│  │  ├─ MauiProgram.cs             composition root: API address, HttpClient, ViewModels, pages
│  │  └─ Platforms/                 Android and iOS only
│  └─ BarberSearch.Mobile.Core/     net10.0: HTTP client and ViewModels, no MAUI references
└─ tests/
   └─ BarberSearch.Mobile.Core.Tests/
```

`BarberSearch.Mobile.Core` exists so that ViewModels are testable with plain xunit — no emulator,
no MAUI dispatcher. Pages and platform code stay in the app project.

## Contracts

DTOs are **not duplicated**: `BarberSearch.Mobile.Core` references
`backend/BarberSearch/src/BarberSearch.Contracts/BarberSearch.Contracts.csproj` directly. The
backend changes a contract once, and the mobile build fails immediately instead of at runtime.
`Contracts` references the domain for its enums (`Grade`, `Language`, `PlaceKind`), so the domain
assembly comes along transitively — it is types only, no EF Core and no database.

## API

Base address is chosen per platform in `MauiProgram.DevelopmentApiBaseAddress`: the Android
emulator reaches the host as `10.0.2.2:5243`, the iOS simulator as `localhost:5243`. The Android
manifest allows cleartext HTTP for that local client only — remove it when a stand with HTTPS
appears.

Authentication runs on the token branch of the auth controller: `POST /api/auth/login` returns the
access token and the refresh token **in the body** (`TokenResponse`), `POST /api/auth/refresh`
rotates the pair, `POST /api/auth/logout` revokes the refresh token. The cookie branch of the same
controller serves the browser web app.

The pair is stored by `ITokenStore` (Core) as one JSON value; the app implements it with
`SecureStorage` (`SecureStorageTokenStore`), and the Android backup rules exclude the encrypted
preferences file so a restored install cannot resurrect an undecryptable pair. `TokenSession` keeps
the pair in memory, and `AuthenticatedHttpHandler` adds `Authorization: Bearer`, refreshes once on
a single `401` (single-flight, so two requests never rotate the same refresh token) and repeats the
request once. A refused refresh ends the session. The Profile tab holds the sign-in form while
there is no session, and email with “Выйти” after it.

## Booking

The barber card (`GET api/barbers/{id}`) and the slot grid
(`GET api/barbers/{id}/slots?date=&hairServiceId=&salonId=`) are public reads and go through
`IBarberSearchApi` on a client without the auth handler. `POST api/bookings` goes through
`IBookingsApi` on a client with `AuthenticatedHttpHandler`, so the customer comes from the token.

A tap on a search row opens the `barber?id={id}` route; `PageRouteFactory` resolves the page from
DI. The card starts with the primary salon, “Любая услуга” and today in the salon's zone. Slot
times are UTC and are shown in the salon's IANA `timeZoneId` from the slot response — the device
zone takes no part. An unknown zone falls back to UTC instead of the device zone: the web uses the
browser zone, and a phone has no salon zone of its own.

The booking panel keeps an immutable snapshot of the chosen slot (date, salon, barber service,
UTC start/end, price) and an idempotency key created once per attempt. Every retry (network, `429`,
`5xx`, `lockTimeout`, expired session) reuses the key, so a broken connection cannot create a
second booking; a new key is generated only for `booking.create.idempotencyKeyConflict`. The
comment is trimmed, sent as `null` when empty and limited to 500 characters.

## Bookings (the «Записи» tab)

The tab reads two independent lists from `GET api/bookings?scope=upcoming|past&pageSize=20&userId=`,
where `userId` is always the signed-in customer: without it an administrator would receive every
booking in the system. The cursor is opaque — it is sent back exactly as the server returned it, and
`nextCursor = null` means the last page. The history list is fetched on its first open and re-read
only after a cancel, a reschedule or a review; switching tabs inside the screen requests nothing.

`BookingResponse` carries only the booking and the ids of the related aggregates — no barber,
service, salon or time zone names — so each row is completed from the public barber card
(`GET api/barbers/{id}`): the barber and salon names, the service by `barberServiceId`, the address,
and the IANA zone used to show the time. Cards are cached per profile for the lifetime of the screen,
at most four are requested at once, and a failed profile leaves the neutral labels («Барбер»,
«Услуга», «Салон») with the time in UTC plus a footnote under the list.

`POST api/bookings/{id}/cancellation` (no body — the customer never writes a reason),
`GET api/bookings/{id}/reschedule-slots?date=` (a salon calendar date; the grid uses the booking's
own duration) and `POST api/bookings/{id}/reschedule` (only `startAt`, exactly the instant the slot
response returned) go through `IBookingsApi`; `POST api/reviews` goes through `IReviewsApi`. A review
is created hidden and waits for moderation, so a booking with `hasReview` shows «Отзыв отправлен»
instead of the button.

There is no background polling: the lists refresh when the tab appears (silently — the rows stay
until the answer), on pull-to-refresh and after an action. The «через 2 часа» countdown is
recomputed by a one-minute timer in the page and touches no network. That deviation from the web,
which polls «Предстоящие» once a minute, is recorded in `AGENT.md` §5.

## Account (the «Профиль» tab)

The tab is the hub and the only sign-in screen: without a session it shows the sign-in form, with
a session it shows initials instead of an avatar, the name, the email, its verification state, the
role and the entry points to «Личные данные», «Мои отзывы», «Безопасность» and «Выйти». Those
sections are ordinary Shell pages pushed over the profile stack (`account-profile`, `security`,
`my-reviews`, `review-edit`), so the tab bar stays hidden there and the back gesture returns to the
hub. The web has no hub — every account route is wrapped in its own access gate — and that
adaptation is recorded in `AGENT.md` §5.

The hub and the account pages read `GET api/users/me`, not `GET api/auth/me`: the latter returns a
projection of the token claims and stays the session check and the `userId` source for «Записи»,
while the former comes from the database and shows the current name, phone and verifications. The
profile is read on every appearance: the first time with a spinner, afterwards silently, without
erasing what is already on screen. Without a session no request is made at all. A `401` clears the
session and returns to the sign-in form; a network failure, `429` or `5xx` shows the error with
«Повторить» and keeps the session — a connection problem does not sign anyone out.

Avatars are out of this slice: nothing is uploaded, deleted or rendered, and the header uses
initials. `avatarUrl` is still sent back unchanged on every profile save, `null` included: `PUT
api/users/{id}/profile` replaces the whole field set, so dropping the field would erase a photo
uploaded on the web. An empty phone is sent as `null`, and the form's save button stays disabled
until the fields differ from the last saved values.

Changing the email is a request, not a rename. `POST api/auth/email-change` with the new address
and the current password only puts two messages in the outbox — a confirmation link to the new
address and a notice to the old one — and `204` therefore means «letters queued», not «address
changed». The address changes when the link is opened in a browser
(`{App:PublicUrl}/confirm-email-change?token=…`); the app neither opens nor parses it, and the
displayed email stays the old one until the next profile read. A local 60-second cooldown follows
success and `409 auth.emailChange.cooldown`, and the button counts down.

Changing the password is the one action that needs an immediate second sign-in. `POST
api/auth/change-password` revokes every refresh token and rotates `SecurityStamp`, so the old access
token stops passing validation right away and the old pair is dead. After `204` the app signs in
again with the profile email and the new password and replaces the pair through
`TokenSession.ApplyAsync`; if that second sign-in fails, it says «Пароль изменён» / «Войдите снова
с новым паролем.» and ends the session, because half a pair is worse than a clean sign-out. The web
does not need this: its cookie session is not the revoked refresh token.

Email verification is the same browser story: `POST api/auth/email-verification` asks for a letter
to the current address and the button exists only while the address is unconfirmed (for a verified
address the server answers `204` and sends nothing). Its cooldown also covers `429` and `409
auth.emailVerification.cooldown`.

«Мои отзывы» reads `GET api/reviews/mine?pageSize=10` with the opaque cursor sent back verbatim;
`nextCursor = null` is the last page. There is no polling: the page loads once per visit,
`RefreshView` re-reads the first page and replaces the list, and scrolling appends the next page.
Edits and deletions change only their own row — a full `GET` would shift the pages — and a stale
answer, arriving after a refresh or a sign-out, is dropped by a generation counter. Deleting the
last row while a cursor exists loads the next page instead of showing the empty state; `404` on
delete means the review is already gone and removes the row with «Этот отзыв уже удалён.». Editing
applies `rating`, `text`, `isPublished` and `updatedAt` from the `PUT` response and never derives
the new status locally: a rating-only change keeps a published review published, a text change
returns it to moderation, and only the server decides.

## Build and run

Local backend first (see `../backend/AGENT.md` §5):

```powershell
cd backend
docker compose up -d
dotnet run --project BarberSearch/src/BarberSearch.Web    # http://localhost:5243
```

Then the app. On Windows build the Android target explicitly — the iOS target needs macOS:

```powershell
cd mobile
dotnet build src/BarberSearch.Mobile/BarberSearch.Mobile.csproj -f net10.0-android
dotnet build src/BarberSearch.Mobile/BarberSearch.Mobile.csproj -f net10.0-android -t:Run   # device/emulator
```

The APK lands in `src/BarberSearch.Mobile/bin/Debug/net10.0-android/` (`ru.barbersearch.mobile-Signed.apk`).

## Tests

```powershell
dotnet test tests/BarberSearch.Mobile.Core.Tests
```

Covers the HTTP clients (real `HttpClient` with a stub handler: JSON parsing of search, card and
slots, booking body and `201 { id }`, `ProblemDetails` → `BarberSearchApiException`, empty filters
omitted), the search and profile ViewModels, the barber card ViewModel (slot defaults in the salon's
zone, service/salon/date changes, stale slot responses, the booking state machine and its retries)
and the salon clock/text helpers. The bookings slice adds the list, cancellation, reschedule and
review clients (query string, opaque cursor, `204` without a body, error codes), `BookingText`, and
the three ViewModels: two independent lists with cursor loading and month groups, profile
resolution with a per-profile cache, cancel/reschedule/review state machines and their error texts,
all with fixed time and fake APIs — no emulator.

## iOS

Building and running iOS needs macOS with Xcode 26 and the `ios` workload. There is no Mac in the
project yet, so iOS is built in CI only until a machine appears (see the plan in the project
notes).
