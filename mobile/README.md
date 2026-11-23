# BarberSearch mobile

.NET MAUI client for iOS and Android (`.NET 10`, `net10.0-android` / `net10.0-ios`).

The app is the **client** half of the product: search for a barber, the public card, the slot
grid, booking, the customer's bookings, favourites and — on Android — push reminders about a
confirmed visit. An administrator also gets the moderation queues on the phone (see «Admin
queues» below), and a barber gets the cabinet — bookings, schedule, salons and the public card
(see «Кабинет мастера» below). The web keeps the professional surface that is not a phone job:
importing old bookings, managing its barbers, and the
notification settings; the rest of the admin section stays there too.

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

The base address is resolved at startup by `DevelopmentEndpoints` (Core) from a single host: the
Android emulator reaches the host as `10.0.2.2:5243`, the iOS simulator and a physical phone as
`localhost:5243`. A phone has no way to see the developer machine's own address, so the port travels
over the cable (or Wi-Fi debugging) through `adb reverse` — the app itself stays on `localhost`:

```powershell
adb reverse tcp:5243 tcp:5243    # API
adb reverse tcp:5173 tcp:5173    # the site behind the Policy link
```

The forward lives as long as the connection: run it again after the cable, the phone or the adb
server restarts. The Android manifest allows cleartext HTTP for that local client only — remove it
when a stand with HTTPS appears.

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

## Search

The «Поиск» tab carries the web's six filters: city, date, language, specialization, hair service
and an additional-service substring (`GET api/barbers?city=&date=&language=&specializationId=&
hairServiceId=&additionalService=&pageSize=`). The form submits only on «Найти», and empty values
are omitted from the query, exactly as in `frontend/src/lib/barber-search.ts`. The server compares
the city exactly, the language is one enum name (`Ru`, `En`, …), and the additional service is a
case-insensitive substring of the barber's own list.

The city field suggests cities while typing: `GET api/addresses/cities` (the backend's DaData
proxy) is asked after a 250 ms pause once two characters are typed, and up to eight rows appear
under the field. A tap fills the bare city name — the server compares city strings exactly, so
«г. Москва» from the list would never match — and the list closes. Only this request leaves the
screen before «Найти»; a failed suggestion call (feature off, rate limit, no network) is silent and
leaves the field a plain input, and an empty answer shows «Ничего не найдено.».

The field also fills itself from the device position. On the tab's first appearance the screen
reverse-geocodes the position (`Geocoding.Default`, a five-second cap) and puts the placemark's
locality into the field, normalizing «г. Москва» to «Москва» for the exact-match filter. A denied
permission, a missing fix, no placemark or a geocoder error all write «Москва», the product's
default city; a city typed by hand or picked from the suggestions is never overwritten, and
«Повторить» on the location hint re-geocodes and replaces the default once access is granted.

The same request carries an optional `latitude`/`longitude` pair. When the tab first appears the
screen asks for the location permission and reads the device position (medium accuracy, 10-second
timeout, a fresh last-known fix is reused for five minutes); «Найти» reuses that read. A refusal or
a missing fix does not block the search — the request just goes without coordinates and the screen
says why, with «Повторить». With
the pair the server orders the page by the distance to the barber's nearest active salon, sets the
`distanceKm` of every row and the card shows «850 м» / «1,2 км». Without it the order stays the
web's rating one. The position is read once per screen and is never stored or sent anywhere except
that search request. The backend side is recorded in `../backend/AGENT.md`.

The two catalog selects are filled from the public reads `GET api/specializations` and
`GET api/hair-services` (bare arrays, no paging), requested in parallel once per screen. Their
failure does not block city, date or language: the form shows «Не удалось загрузить фильтры.» with
«Повторить», the selects stay disabled, and the search button keeps working. A catalog that loaded
empty hides its own select, as on the web.

The empty state keeps the web's wording and priority: a city is more specific than a date, a date
more specific than the other filters, and without filters «Мастеров пока нет». The counter above
the results repeats the web's plural (`Показан 1 мастер`, `Показаны 2 мастера`,
`Показаны 5 мастеров`). A finished search scrolls the page to the results, the way the web scrolls
to `#barbers` after submit.

The screen opens with the product's mark: the web header's scissors (`site-header.tsx`, lucide
`scissors`) in a `Primary` tile beside «Hairo», with the landing headline
(`home-hero.tsx`) as the tagline. The mark is the lucide path itself — the two circles as arcs, the
three cuts as segments — declared once in `Controls/ScissorsMark.xaml` (a `ContentView` around a
single `Path`, because `Path` is sealed); `StrokeColor` repaints it through `AppThemeBinding` like
the tab icons, where an SVG in `Resources/Images` would stay one colour. A `GeometryGroup` of
`EllipseGeometry`/`LineGeometry` reads better in XAML but draws a stray line and ring on Android
(MAUI 10.0.110, API 36), so the single path string is deliberate. The mark repeats where the web
header would be: `BrandTitleView` in `Shell.TitleView` puts it before the section name on each of
the four root tabs, and the signed-out «Профиль» opens its sign-in block with the same tile and
«Вход в Hairo». The empty results block copies the web's empty-state card
(`barber-results.tsx`): a dashed `Border`, a `Muted` circle with the mark and centred text. The web
puts its `Search` icon in that circle; the phone keeps the scissors on purpose, so the empty screen
carries the product's face instead of repeating the «Найти» button above it.

The date filter is a native `DatePicker`: a tap on the field opens the platform calendar, as the
web's `<input type="date">` opens the browser one. `Date = null` is «Любая дата», so the optional
filter stays empty; the calendar is bounded by today and the next 30 days, and a «×» button in the
field clears it. The bound and the dropped `<datalist>` hints of the additional-service field are
recorded in `AGENT.md` §5.

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
`nextCursor = null` means the last page. The history list is fetched on its first open, re-read
after a cancel, a reschedule or a review, and refreshed by the auto-refresh while it is the visible
tab; switching tabs inside the screen otherwise requests nothing.

`BookingResponse` carries only the booking and the ids of the related aggregates — no barber,
service, salon or time zone names — so each row is completed from the public barber card
(`GET api/barbers/{id}`): the barber and salon names, the service by `barberServiceId`, the address,
and the IANA zone used to show the time. Cards are cached per profile for the lifetime of the screen,
at most four are requested at once, and a failed profile leaves the neutral labels («Мастер»,
«Услуга», «Салон») with the time in UTC plus a footnote under the list.

`POST api/bookings/{id}/cancellation` (no body — the customer never writes a reason),
`GET api/bookings/{id}/reschedule-slots?date=` (a salon calendar date; the grid uses the booking's
own duration) and `POST api/bookings/{id}/reschedule` (only `startAt`, exactly the instant the slot
response returned) go through `IBookingsApi`; `POST api/reviews` goes through `IReviewsApi`. A review
is created hidden and waits for moderation, so a booking with `hasReview` shows «Отзыв отправлен»
instead of the button.

There is a 30-second auto-refresh, not a button: while the tab is on screen and the app is in the
foreground, a page timer calls `BookingsViewModel.AutoRefreshAsync`, which re-reads the first page
of the active list silently — the rows stay until the answer and a failed refresh keeps them. The
timer pauses with `OnDisappearing`, on `Window.Stopped` and resumes on `Window.Resumed`, and it
yields while a cancel, a pull-to-refresh, the first load or a page append is running; a list where
a second page was loaded is left alone, because the replace would drop those rows. On top of it the
lists refresh when the tab appears, on pull-to-refresh and after an action. The «через 2 часа»
countdown is recomputed by a separate one-minute timer in the page and touches no network. That
deviation from the web, which polls «Предстоящие» once a minute, is recorded in `AGENT.md` §5.

## Account (the «Профиль» tab)

The tab is the hub and the only sign-in screen: without a session it shows the sign-in form, with
a session it shows initials instead of an avatar, the name, the email, its verification state, the
role and the entry points to «Личные данные», «Мои отзывы», «Безопасность» and «Выйти», plus —
for a barber — the «Кабинет мастера» row (see below) and — for a customer — the «Стать мастером»
row, whose title follows the state of the request (see below). Those sections are ordinary Shell
pages pushed over the profile stack (`account-profile`, `security`, `my-reviews`, `review-edit`,
`barber-request`, `barber-cabinet`), so the tab bar stays hidden there and the back gesture returns
to the hub. The web has no hub — every account route is wrapped in its own access gate — and that
adaptation is recorded in `AGENT.md` §5.

The hub and the account pages read `GET api/users/me`, not `GET api/auth/me`: the latter returns a
projection of the token claims and stays the session check and the `userId` source for «Записи»,
while the former comes from the database and shows the current name, phone and verifications. The
profile is read on every appearance: the first time with a spinner, afterwards silently, without
erasing what is already on screen. Without a session no request is made at all. A `401` clears the
session and returns to the sign-in form; a network failure, `429` or `5xx` shows the error with
«Повторить» and keeps the session — a connection problem does not sign anyone out.

The hub itself has no avatar management: nothing is uploaded, deleted or rendered here, and the
person card keeps initials. The barber cabinet's «Профиль» section owns that flow. `avatarUrl` is
still sent back unchanged on every profile save, `null` included: `PUT api/users/{id}/profile`
replaces the whole field set, so dropping the field would erase a photo uploaded on the web. An empty phone is sent as `null`, and the form's save button stays disabled
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

«Забыли пароль?» under the sign-in form opens `forgot-password` over the profile stack. It sends
one anonymous `POST api/auth/password-reset` with the address and shows the generic «Проверьте
почту» card for every address — the server answers `204` even for an unknown, inactive or malformed
one, so the screen never distinguishes them — with a local 60-second resend cooldown, because the
server's own cooldown is silent as well. The letter's `/reset-password?token=…` link opens the web
page: the app neither opens nor parses it and has no new-password form, exactly like email
verification and the address change. The card drops the web's «На главную» (the back gesture returns
to the sign-in form), and «Безопасность» has no second entry point; both deviations are recorded in
`AGENT.md` §5.

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

## «Стать мастером» (the barber request)

A customer applies for the master role from the «Профиль» hub: the row under the person card, before
the silent-refresh error, opens the `barber-request` page over the profile stack with the tab bar
hidden. Only a customer sees it (`Role == UserRole.Customer`): a barber and an administrator are not
offered an action the server would answer `403` to. The row's title is the application's state —
«Стать мастером», «Заявка на рассмотрении», «Заявка отклонена», «Заявка одобрена» — so the hub says
whether an application already exists; the details are on the page, which is the source of truth, and
`AGENT.md` §5 records why the row is a `Border` card-row instead of a `Primary` button.

The page has five states: loading while `GET api/barber-requests/mine` is in flight, the form when
there is no request, the status when there is one, a read error with «Не удалось проверить заявку»,
the reason and «Повторить», and an expired session — «Сессия истекла — войдите снова.» with
«К входу», shown when the read answers `401` after `TokenSession` has already been cleared.
`request: null` is the normal “no request yet”, not a `404`: the endpoint answers
`200 MyBarberRequestResponse` to any role and `401` to an anonymous call. The write is
`POST api/barber-requests` and answers `201` with the created request in `Pending`; `400` carries the
field codes (`barberProfile.displayName`, `barberProfile.yearsOfExperience`, `barberRequest.city`,
`barberRequest.about`) and each is shown under its field, while an unknown `400` stays a generic
line; `403 barberRequest.create.forbidden` means the account is no longer a customer;
`409 barberRequest.create.alreadyPending` means an active request already exists, and the page then
re-reads `mine` and shows the actual status instead of an error; `429` asks to wait a minute. The
`detail` of `ProblemDetails` is never rendered: it is English and diagnostic.

Reads repeat themselves, the write does not. The page arms the same `AutoRetryLoop` +
`PageVisibility` pair as every other read, so a failed `mine` retries while the screen is visible
with «Пробуем снова…» under the reason; the `POST` is never repeated silently, because after a
failure its outcome is unknown and a second application would only meet `409`. It keeps a
«Повторить» button, the second tap is blocked while the request is in flight («Отправляем…»), and
the fields are checked on the client before sending — the same limits and texts as
`frontend/src/lib/barber-request-form.ts` — with an empty «О себе» sent as `null` rather than `""`.
`201` shows the status at once with the heading «Заявка отправлена», without a second read.

The status card repeats `frontend/src/components/barber/barber-request-status.tsx`: a heading and a
chip («На рассмотрении», «Одобрена», «Отклонена»), then «Имя», «Стаж», «Город», «Работает в
салоне», «Подана», «Рассмотрена» and «О себе». A rejected request adds «Причина отказа» and
«Подать заявку снова», which opens the same form without pre-filling, as on the web. An approved
one adds the text about the role and «Выйти и войти снова»: the button clears the session
(`TokenSession.ClearAsync`) and returns to the hub, because approval changes the role, the server
rotates `SecurityStamp` and revokes every refresh token, so the pair the app holds is already
invalid and only a new sign-in shows the new role.

The texts come from the web (`become-a-barber-page.tsx`, `barber-request-form.tsx`,
`barber-request-status.tsx`, `lib/barber-request-form.ts`) with «барбер» → «мастер»; the English
values follow the same rule — “professional”, never “barber” or “master” — and live in
`src/BarberSearch.Mobile.Core/Localization/messages/en.json`, like every other string.

## «Кабинет мастера» (the barber cabinet)

The barber's working screen: the visits of the day, the weekly schedule, the salons where the
person works and the public card. It is one screen with four sections, opened from «Профиль» and
pushed over its stack with the tab bar hidden — the role is rare, so it is not a fifth tab. The
web cabinet is the reference for structure, states and wording; the mobile adaptations are
recorded in `AGENT.md` §5.

### Getting there

- `GET api/users/me` answers `role: "Barber"` → a «Кабинет мастера» card-row in «Профиль», under
  the person card, with «Записи клиентов, расписание и прайс».
- The row is drawn for a barber without a profile too: the cabinet explains that the profile is
  missing and offers «Создать профиль мастера» (onboarding).
- The route `barber-cabinet` opens over the profile stack, so the back gesture returns to
  «Профиль» and the row keeps its place.
- The cabinet gates on `GET api/auth/me`: a customer sees «Это кабинет мастера», a barber without
  a profile sees the onboarding card, an anonymous or expired session sees the sign-in card with
  «Войти» (which goes to the «Профиль» tab, not automatically).

### Sections

| Section | What it does |
|---|---|
| «Записи» | «Сегодня» (the day feed built from both scopes, with the free windows and the next client), «Ближайшие» and «История». Confirm, complete, no-show, cancel with a reason, reschedule, note and price; «Записать клиента» opens the create form from «Сегодня» and «Ближайшие», not from «История»; a client's phone is a `tel:` link. The day summary and «Записать клиента» stay fixed above the list while it scrolls. The active list re-reads itself every 30 seconds while the section is on screen. A failed next page keeps the rows and shows the reason; «Дальше» retries it. |
| «Расписание» | The weekly template per salon, Monday to Sunday (working day or day off, hours), «Заполнить будни», one `PUT` for the whole week, the `409` conflict panel with the affected visits and «Всё равно сохранить», date exceptions and time offs. |
| «Салоны» | The profile's attachments with their moderation status, the primary salon («Сделать основным»), a public-catalog search for a new place, «Создать салон» for the barber's own place (the web's five place-kind radio cards with their hints, catalogue flag, name, city/address with suggestions, country, time zone, coordinates, phone, website), and call / map / copy address / open this salon's schedule. |
| «Профиль» | The card (avatar, name, grade, rating, experience, languages, payment, bio, late policy), the photo grid — tap a frame to upload, caption, reorder, hide, restore or delete it — services and prices (add, edit, hide, restore), specializations, «Редактировать профиль», «Поделиться профилем» and «Открыть, как видят клиенты» — the last two only for a verified and active profile. The avatar has «Загрузить фото» / «Изменить фото» / «Удалить» (with a native confirmation) and shows «На проверке» or «Отклонено» with the rejection reason until moderation decides; the file is the account photo, not a portfolio frame. |

Reads retry themselves while the screen or section is visible; writes show their reason and are
never repeated silently (`AGENT.md` §5 lists both sides and the one picker catalog that keeps only
the button). A cabinet card has no «Повторить», and in «Записи» a failed next page keeps its reason
and is retried by «Дальше».

### Screens over the cabinet

| Route | Screen | Route parameters |
|---|---|---|
| `barber-booking-create` | Creates a booking for a registered client or a guest — service, salon, date within 30 days, a free slot, a comment up to 500 characters, an idempotency key per attempt | `owner` (the bookings section ViewModel), optional `preferredStartAt` |
| `barber-booking-reschedule` | Moves a visit to a free slot of the same duration | `item`, `owner` |
| `barber-booking-note` | The barber's note for a visit, 0 / 1000 characters | `item`, `owner` |
| `barber-onboarding` | Creates the profile, rotates the token pair and attaches the first salon (skippable) | — |
| `barber-notifications` | The notification inbox, keyset pages, «Прочитать все» | `owner` (the cabinet page), `barberProfileId` |
| `barber-profile-edit` | Edits the public card; grade stays read-only | `owner`, `profile` |
| `barber-service-editor` | Adds a service from the directory or edits its price and duration; hide and restore stay in the section | `owner`, `barberProfileId`, optional `item` |
| `barber-salon-create` | Creates an owned place (kind, catalogue flag, name, city/address with suggestions, country, time zone, coordinates, phone, website; listed → review, hidden → active at once) | `owner` (the salons section ViewModel) |
| `barber-photo` | One portfolio frame: upload (file picker, JPEG ≤ 1600 px and ≤ 5 MB prepared on the phone, caption up to 2000 characters) or edit (caption, «Выше»/«Ниже», «Скрыть»/«Вернуть», «Удалить» behind a confirmation); the 10-photo limit disables adding | `profileId`, `photos` (snapshot), optional `photoId` |

### API

Reads:

- `GET api/auth/me` — the gate; `GET api/users/me` — the hub role that draws the row and the
  avatar's moderation state (`avatarUrl`, `isAvatarBlocked`, `avatarModerationStatus`, reason),
  which the barber card masks to `null` for anything but an approved frame.
- `GET api/barber/bookings?scope=upcoming|past&cursor=&pageSize=`;
  `GET api/bookings/{id}/reschedule-slots?date=`.
- `GET api/barber/booking-options`, `GET api/barber/slots?date=&hairServiceId=&salonId=`,
  `GET api/barber/clients?search=`.
- `GET api/barber-profiles/{id}` (the own card, hidden services included),
  `GET api/barber-profiles/{id}/salons`, `GET api/barber-profiles/{id}/schedule?from=&to=`.
- `GET api/salons?city=&search=`, `GET api/specializations`, `GET api/hair-services`.
- `GET api/barber-requests/mine` — the onboarding prefill.
- `GET api/barber/notifications?cursor=&pageSize=`, `GET api/barber/notifications/unread-count`.

Writes:

- `POST api/barber/bookings`; `POST api/bookings/{id}/confirmation`, `/completion`,
  `/cancellation`, `/no-show`, `/barber-note`, `/price`, `/reschedule`.
- `PUT api/barber-profiles/{id}/salons/{salonId}/working-schedules?force=true`;
  `PUT api/barber-profiles/{id}/salons/{salonId}/schedule-exceptions/{yyyy-MM-dd}?force=true`,
  `DELETE` on the same exception path;
  `POST api/barber-profiles/{id}/time-offs?force=true`,
  `DELETE api/barber-profiles/{id}/time-offs/{timeOffId}`.
- `POST api/barber-profiles`, `PUT api/barber-profiles/{id}`, `POST api/barber-profiles/{id}/salons`,
  `PUT api/barber-profiles/{id}/salons/{salonId}/primary`.
- `POST api/barber-profiles/{id}/services`, `PUT|DELETE api/barber-profiles/{id}/services/{hairServiceId}`;
  `POST|DELETE api/barber-profiles/{id}/specializations/{specializationId}`.
- `POST api/barber-profiles/{id}/photos/upload` (multipart `file` + `description`),
  `DELETE api/barber-profiles/{id}/photos/{photoId}`,
  `PUT api/barber-profiles/{id}/photos/{photoId}/description`,
  `PUT api/barber-profiles/{id}/photos/order` (the full id list),
  `POST|DELETE api/barber-profiles/{id}/photos/{photoId}/publication`.
- `POST api/users/me/avatar` (multipart `file`, the prepared JPEG) and `DELETE api/users/me/avatar`
  — the account avatar behind the profile section's preview and delete confirmation.
- `POST api/barber/notifications/{id}/read`, `POST api/barber/notifications/read-all`.
- `POST api/salons/own` — the barber's own place from «Салоны»; `201` returns its id and a
  listed place starts in review.
- `POST api/auth/refresh` — the pair is rotated right after the profile is created, because the
  access token has no `barber_profile_id` claim yet.

### The bell and its badge

- The 44 pt bell in the cabinet header opens `barber-notifications`; the badge caps at «99+».
- The unread count is read after the gate succeeds, every 60 seconds while the cabinet is
  visible and ready, when the app returns to the foreground, and when a child screen pops back.
- A failed count is silent: the previous number stays, and the badge never becomes an error.
- A row in the inbox is marked read on tap and opens «Записи», not a single visit —
  `GET api/bookings/{id}` does not exist and the list already shows the visit.

### States

- **Loading** — «Загружаем кабинет…» for the gate; each section has its own indicator on the first
  read, and a later refresh keeps the content on screen.
- **Error and auto-retry** — the reason and «Пробуем снова…» while the loop runs; a cabinet card
  has no «Повторить», because the loop is the retry, and a failed attempt keeps the rows and the
  reason until the answer. A failed row action leaves only the reason in the row (a `409` says
  «Запись уже изменилась — обновите список.»); the list is reread by pull-to-refresh or the
  30-second refresh. A failed next page keeps the rows and the reason, and «Дальше» retries it —
  there is no second button. The specialization picker's catalogue arms its own loop while the
  picker is open, so its card shows the reason and «Пробуем снова…»; the service editor's
  hair-service catalogue keeps its own «Повторить», because it arms no loop.
- **Empty** — no bookings in the day or the scope, no attached salon, no exceptions, no time off,
  no notifications, no photos; each keeps the web's wording.
- **Session expired** — the sign-in card with «Войти»; the cabinet stays on the stack, only the
  tap navigates to «Профиль».
- **Not a barber** — «Это кабинет мастера» for a customer or an administrator.
- **Pending profile** — a barber without a profile gets «Профиль ещё не создан» and the
  onboarding button; a created but unverified card carries «Профиль на проверке…» and hides
  preview and share.
- **Onboarding session states** — «Профиль создан. Обновляем сессию…», «Профиль создан, но сессия
  не обновилась» (retry or sign in), «Профиль создан, войдите снова».

### What the cabinet cannot do

- Import old bookings (`/barber/import`) — a text/ICS parser and a bulk load stay on the web.
- Create a salon or manage its barbers.
- Change notification or Telegram settings — the «Профиль» tab carries the global «Присылать
  напоминания» switch.
- Open one booking from a notification — the row lands on «Записи».
- Appear for anyone but a barber: there is no fifth tab and no cabinet for a customer or an
  administrator.

## Admin queues

The phone carries the four admin queues where the job is a **decision on a record**, and nothing
else of `/admin`: reviews awaiting publication, photo pre-moderation (avatars and portfolio),
profiles awaiting verification, and requests to become a professional. Statistics, the catalogue,
salons, users, bookings and the audit log stay on the web (`AGENT.md` §5 records why).

The entry is a row in the «Профиль» hub, visible only when `GET api/users/me` answers
`role: "Admin"`, with a red badge that sums the pending queues of `GET api/admin/summary`. The hub
behind it is one list of four sections — the web's rail and its «Сводка» screen merged, because a
phone shows the numbers once, not twice — and every row states its queue in words as well as in
the badge. The hub re-reads the snapshot on every appearance and on pull-to-refresh; there is no
timer, because a queue only changes when somebody decides, and coming back from a section is that
moment.

| Screen | Route | Reads | Decisions |
|---|---|---|---|
| Hub | `admin` | `GET api/admin/summary` | — |
| Reviews | `admin-reviews` → `admin-review` | `GET api/admin/reviews?status=…`, `GET api/admin/reviews/{id}` | `POST` / `DELETE api/reviews/{id}/publication` |
| Photos | `admin-photos` | `GET api/admin/photos/avatars`, `GET api/admin/photos/portfolio` | `POST api/admin/users/{id}/avatar/approval`, `…/rejection`; `POST api/barber-profiles/{id}/photos/{photoId}/approval`, `…/rejection` |
| Professionals | `admin-masters` → `admin-master` | `GET api/admin/barbers?status=…`, `GET api/admin/barbers/{id}` | `POST` / `DELETE api/barber-profiles/{id}/verification` |
| Requests | `admin-requests` → `admin-request` | `GET api/admin/barber-requests?status=…`, `GET api/admin/barber-requests/{id}` | `POST api/admin/barber-requests/{id}/approval`, `…/rejection` |

The photo queues are read with the same opaque cursor as every other paged read of the app
(`RemainingItemsThreshold`), and media URLs become absolute through `Core/Media/MediaUrl` — the
media routes are anonymous, so `Image` can load them directly.

A decision never sits on the row that needs a reason: opening «Отклонить» / «Скрыть» / «Снять
подтверждение» expands a panel above the fixed bottom bar with the reason field, and the confirm
stays disabled until the reason satisfies the server's own rule (500 characters for photo and
request rejections; a hide and an unverify carry no reason at all, so their panels confirm
immediately). After a successful decision the page returns to the queue, whose `OnAppearing`
refresh drops the resolved row; the hub then refreshes in turn. The wording is the web's, with
«барбер» → «мастер» and “professional” in English, as in the rest of the app.

## Push notifications

The channel carries nine kinds of Android notification: seven for the customer and two for the
master. The hour-before reminder
(`booking.reminder`) announces a **confirmed** booking; the booking-creation event
(`booking.created`) covers both "you booked" and "the barber booked you"; and five booking-change
events announce a confirmation (`booking.confirmed`), a cancellation (`booking.cancelled`), a
reschedule (`booking.rescheduled`), and a customer's own cancellation or reschedule made outside
the app (`booking.cancelledByCustomer`, `booking.rescheduledByCustomer`). The two master events go
to the profile owner: `barber.booking.created` when a customer books on their own, and
`barber.booking.cancelled` when a customer or an administrator cancels; a barber's own create or
cancel sends none. The split is by event family, not by account role: the app is shared by
clients, masters and administrators, and the server never filters a recipient by `UserRole`. A
master or an administrator who books another master is that master's customer and receives all
seven customer events on the same device, while the profile they own keeps receiving the two
master events. The customer's own
action produces a push only when it happened outside the app: a booking, cancel or reschedule from
the app shows its result on the screen (a cancel or reschedule also queues an email), while the
same action from the web pushes. All texts are composed
by the server (`backend/AGENT.md` §20) and arrive ready — the app never formats push text, because
FCM shows a `notification` payload even when the process is dead, and the server owns the salon's
zone and the «сегодня»/«завтра» word. A typical reminder:

```text
Через час: салон «Барбершоп»
Мастер Иван · Стрижка · сегодня в 14:30
```

The body says `завтра` when the visit is on the salon's next local date; an unknown IANA zone falls
back to UTC, and missing snapshots simply drop out of the body (an empty salon gives the fallback
title «Через час — визит к мастеру»). A customer message carries no customer name, phone or note —
only the salon, master, service and time; the two master messages put the customer's name in the
title (`Новая запись: Анна Петрова` / `Запись отменена: Анна Петрова`) and keep the salon in
the body, and never a phone or a note. The data payload carries the event `type`
and, when the server has it, the `bookingId`: the reminder sends
`{"type":"booking.reminder","bookingId":"<guid>"}`, the creation event
`{"type":"booking.created","bookingId":"<guid>"}`, and the status events use
`booking.confirmed`, `booking.cancelled`, `booking.rescheduled`, `booking.cancelledByCustomer`
and `booking.rescheduledByCustomer` with the same shape; the master events use
`barber.booking.created` and `barber.booking.cancelled`, also with only `type` and `bookingId`, and
the client never requires the id. All nine arrive on the Android channel `reminders` /
«Напоминания», all reuse the «Присылать напоминания» toggle (switching it off unregisters the
device, so none of them can be delivered); the seven customer events open the «Записи» tab on a
tap, while either master event opens `barber-cabinet` — its default section, client bookings, not
the «Записи» tab. The creation and status events are
composed on the worker's next tick, so an event can trail the action by up to one tick, and a guest
booking (no account) gets no customer push — though an administrator's cancellation of one still
notifies the master. The cancellation text deliberately carries no reason: the customer message
stays free of customer data, and the reason would need a new snapshot and its own privacy
review.

On the three write requests it makes for a booking — create (`POST api/bookings`), cancel
(`POST api/bookings/{id}/cancellation`) and reschedule (`POST api/bookings/{id}/reschedule`) — the
app sends `X-Client-Surface: app`. The header tells the backend which surface is acting, so the
app's own action does not come back as a push; a cancel or reschedule also queues an email to the
verified address. A missing or unknown header means "another surface" and the backend pushes: the
header is diagnostic, never validated, and an older client without it keeps receiving
notifications.

### `push.json`

Firebase is configured by a local file, not by `google-services.json`; the real
`Resources/Raw/push.json` is ignored by `mobile/.gitignore`, and without it the app builds and
behaves exactly as before. The format:

```json
{
  "enabled": true,
  "projectId": "barbersearch-example",
  "applicationId": "1:000000000000:android:0000000000000000000000",
  "apiKey": "REPLACE_WITH_FIREBASE_WEB_API_KEY",
  "senderId": "000000000000"
}
```

The four values come from the Firebase console and the project's Android app settings:

- `projectId` — **Project settings → General → Project ID**;
- `applicationId` — the Firebase Android app id (`client[].client_info.mobilesdk_app_id` in
  `google-services.json`; **not** the `ru.barbersearch.mobile` package name — register the app in
  the project first);
- `apiKey` — **Project settings → General → Web API key** (`client[].api_key[0].current_key` in
  `google-services.json`);
- `senderId` — **Project settings → Cloud Messaging → Sender ID** (the project number).

The file is ignored because the key and ids are per stand. `Resources/Raw/push.example.json` shows
the shape, is excluded from the APK and is not read by the app. On startup `PushBootstrap` reads
`push.json` synchronously and builds `FirebaseOptions` programmatically; the notification channel is
created later by `App`, after localization, because its name and description are visible in the
phone settings. A missing file, broken JSON, `enabled: false` or a blank required field means
`PushOptions.Disabled`: no Firebase init, no permission prompt, no channel and no «Присылать
напоминания» row — the app is unchanged.

`Xamarin.Firebase.Messaging` is pinned to **125.0.0.1** in the Android-only `ItemGroup`. Do not
bump it casually: 125.0.1+ lifts AndroidX and restore fails with NU1608 (an error here, because
warnings are errors), and 125.1+ marks the token APIs obsolete.

### Device registration

The app registers the FCM token for the signed-in customer; the server takes the owner from the
Bearer token and never from the request body. Registration runs on sign-in
(`TokenSession.SessionStarted`), on `Window.Resumed` (a session restored from storage, or a return
to the foreground) and on FCM `OnNewToken` after a reinstall or token rotation. An explicit
«Выйти» first calls `POST api/push/devices/unregister` (with a 5-second cap) and only then sends
the logout request, because the unregister call needs the Bearer token and logout revokes it.
A failed unregister keeps the persisted marker and is retried by the next ensure while the toggle
is off; the next sign-in always re-POSTs the token, so it moves to the new account. An **expired**
session cannot call unregister at all, so its marker stays until retention or the next sign-in.
Every call is swallowed and never blocks a screen: after login the registration is fire-and-forget,
so the permission dialog and FCM round-trip cannot hold the profile spinner or the «Входим…» button.
A failed registration clears the marker so the next launch retries; a transient network/HTTP
failure is a separate `TransientFailure` state and shows no device hint. Re-registering the same
token on another account transfers it server-side.

### Toggle, channel and tap

«Профиль» shows a global «Присылать напоминания» switch (44 pt row, on by default) only when push
is configured; without `push.json` the whole row is hidden. The switch is shared by both events —
there is no separate booking-created toggle. If the permission is denied, the switch
stays where the person put it and the hint «Уведомления выключены в настройках телефона. Разрешите
их, чтобы получать напоминания.» appears with «Открыть настройки»; a device without Google Play
services says «Уведомления недоступны на этом устройстве». A transient network failure shows no
hint — the next ensure retries. Switching off attempts to unregister the device; if that request
fails, the marker stays and the next ensure retries while the toggle is off. `POST_NOTIFICATIONS`
is requested only after push is configured.

The channel id is `reminders`; its visible name and description are localized keys
(«Напоминания» / «Напоминания о записях») and are created/updated by `App` after the language is
initialized, not at `MauiProgram` time. Notifications use a monochrome lucide bell
(`Platforms/Android/Resources/drawable/notification_bell.xml`) as the small icon; the manifest also
sets `com.google.firebase.messaging.default_notification_icon` and
`...default_notification_channel_id=reminders`, so a background notification drawn by the system
gets the same icon and channel. A foreground message is posted locally, a background one is shown
by the system; a tap on either event opens the «Записи» tab, not a single booking — there is no
`GET api/bookings/{id}`, and the list already shows the visit. Cold-start extras are handled in
`MainActivity.OnCreate` only when `savedInstanceState is null` (a restored process must not jump
tabs), warm start in `OnNewIntent` (`LaunchMode.SingleTop`).

### Development and iOS

The backend in Development runs `Push:Enabled=true` **without** credentials: it composes the text,
writes it to the log (without the token) and marks the row `Skipped` with `push is not configured`,
so nothing is sent to Google. This dev mode is derived from the host environment
(`IHostEnvironment.IsDevelopment()`), not from a config key. In any other environment an enabled
but unconfigured worker leaves rows `Pending` (they go out after the credentials are fixed) and
writes one warning instead of silently consuming reminders. Production keeps `Push:Enabled=false`
until `/privacy` is amended
(device token, purpose, Google as a processor, cross-border transfer) and `PrivacyPolicy.Version`
is bumped in the backend, the web frontend and `Core/Legal/PrivacyPolicy.cs` — see
`backend/AGENT.md` §20.9.

iOS has no push in this slice: `IosPushPermission`/`IosPushTokenProvider` are stubs that answer
«disabled», no Firebase package is referenced by the iOS target, and the reminder row never
appears. APNs is a later slice; the shared token table already reserves `PushPlatform.Ios`.

## Barber card (the public profile)

The card is one scroll: hero → «Работы» → «Услуги» → «Свободное время» → «О мастере» → «Отзывы» →
«Специализации» → «Оплата» → «Правила визита». Services stay above the slot grid, where the mobile
card already had them; every other section follows the web order, and an empty section is not drawn
at all — the web's rule that a heading without data is noise. «Отзывы» is the only section with an
empty state.

All of it comes from the same public `GET api/barbers/{id}` the card already reads: `avatarUrl`,
`isVerified`, `yearsOfExperience`, `bio`, `languages`, `certificatesAndTraining`, `achievements`,
`goodies`, `communicationFormat`, `additionalServices`, `paymentMethods`, `latePolicy`, `photos`
and `specializations`. The public response contains only approved, published and unblocked photos,
already ordered by `sortOrder` then `createdAt`, so the client never sorts or filters them.

Media URLs are relative (`/api/media/portfolio/...`, `/api/media/avatars/...`) or absolute legacy
links, and `Image.Source` needs an absolute URI: `MediaUrl.Resolve` joins the relative form with the
API base address from `BarberSearchApiOptions` (the port survives because the base ends with a
slash), and returns absolute links untouched — a legacy photo stays on its own host. `MediaUrl.Resize`
adds `?size=sm|md|lg` **only** to those two own prefixes and only when the URL has no query or
fragment yet: it mirrors `frontend/src/lib/media.ts`, because the server serves variants for its own
files and nothing else. Tiles use `thumbnailUrl` when the server sent one and `?size=md` otherwise;
the viewer uses the raw source. Caching is whatever `UriImageSource` does by default — no second
image cache, no extra package.

Reviews are paged from `GET api/reviews?barberProfileId=&pageSize=10&cursor=`. The size is 10, as on
the web; the cursor is opaque, sent back exactly as received, and `nextCursor = null` means the last
page. Pages are cached in memory by number, so «Назад» never touches the network, and «Дальше» only
follows the cursor from the response — the client invents no offsets. The summary (`4,5 · 12
отзывов`) and the page range (`1–10 из 12`) take the total from the profile, not from the loaded
page: the last page is shorter, and a mean over one page would be wrong. With `reviewsCount = 0` no
request is made at all. A failure on the first page replaces the list with an error and «Повторить»;
a failure on a later page keeps the rows already shown and turns «Дальше» into «Повторить» with the
same cursor, so a flaky connection cannot lose the reader's place.

Review dates are shown in the **primary salon's** zone, not in the zone of the salon selected in
«Свободное время»: a review belongs to the barber, and the current slot choice has nothing to do
with when the visit happened. An unknown zone falls back to UTC, as everywhere else.

The photo viewer is a separate route (`portfolio-photo`) over the search stack with the tab bar
hidden, not a dialog: frames are passed as route objects (there is no "photo by id" endpoint, and
the card already loaded the gallery), swiping pages them, the caption and the «N из M» counter
follow the current frame, and the system back button returns to the card. The web's arrow buttons
and keyboard shortcuts are deliberately dropped — on a phone the gesture is the control.

## Localization

The interface is Russian and English, like the web. The approach is gettext: the key is the Russian
source string, and `src/BarberSearch.Mobile.Core/Localization/messages/en.json` holds the English
translation. Code calls `Loc.T("…")`; XAML uses `{loc:Translate Key='…'}`. A missing translation
falls back to the Russian source instead of a key. The catalog holds **938 keys**, the cabinet
strings included.

The language is chosen by the compact RU/EN pair at the top of the «Профиль» tab (the web's header
switcher has no direct phone equivalent), defaults to Russian and is stored in `Preferences` under
`barbersearch.locale`. Changing it rebuilds Shell from DI, so pages and ViewModels are created with
the new language; the app returns to «Профиль», and in-tab state starts over as after a page reload.
Formatting follows the language: `ru-RU` / `en-GB`, 24-hour time, day-month dates and
«2 400 ₽» / «RUB 2,400». `tests/BarberSearch.Mobile.Core.Tests/LocalizationTests.cs` covers the
catalog, the Russian three-form and English two-form plurals, and the English date, price and
counter conventions.

## Theme

The app ships the web's two palettes: `Colors.xaml` holds every token twice (light value and a
`…Dark` twin), and each use site picks a side with `AppThemeBinding`. No third palette is introduced
on the phone.

`AppThemeBinding` follows the device theme until the person chooses otherwise. The three-segment
switcher at the top of «Профиль», next to RU/EN, offers «Системная» (the default), «Светлая» and
«Тёмная». The choice is stored in `Preferences` under `barbersearch.theme` and applied through
`Application.UserAppTheme`. Switching does not rebuild Shell — the open pages repaint in place, so
the tab and its state survive, unlike a language change. The selected segment is styled through
`SegmentButton` / `SegmentButtonSelected` in `Styles.xaml`, so it follows the palette instead of
remembering the colours of the click.

The web design system has the same `.dark` tokens but no toggle yet (frontend/AGENT.md §7.4), so the
mobile switch is the first place the dark palette is reachable. On Android 24–34 the status and
navigation bars are coloured from the same theme (`Theming/PlatformTheme` with the
`Platforms/Android` implementation, including the icon colour); from Android 15 the system draws the
status bar itself in edge-to-edge mode, so it keeps its own colour. iOS takes the window's
appearance from the same theme. `tests/BarberSearch.Mobile.Core.Tests/ThemingTests.cs` covers the
stored codes, the System default and the write/read round-trip.

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

On a physical phone, forward the two dev ports once per connection before `-t:Run` (the app is built
against `localhost`, see API):

```powershell
adb reverse tcp:5243 tcp:5243
adb reverse tcp:5173 tcp:5173
```

A green build followed by `ADB1000: … Попытка чтения после конца потока` is not a compile or backend
failure: the adb link to the phone died while Fast Deployment was still pushing assemblies to it.
Over Wi-Fi the deploy moves tens of megabytes onto the phone, so a weak 2.4 GHz link (`adb shell cmd
wifi status` shows the RSSI and link speed) drops the transfer halfway and the device turns `offline`.
The cable is the fix; on Wi-Fi, get the phone onto 5 GHz or near the access point, restart the server
(`adb kill-server`, `adb start-server`), make `adb devices -l` list the phone as `device`, re-select
the device in Visual Studio, re-apply the two forwards, and launch again — `AGENT.md` §4 has the
details, including why rebuilding neither the app nor the backend helps.

A phone on the same Wi-Fi can skip the forward and be built against the machine's own address
instead — the backend then has to listen on the LAN interface, and the firewall has to allow both
ports:

```powershell
dotnet build src/BarberSearch.Mobile/BarberSearch.Mobile.csproj -f net10.0-android -p:DevApiHost=192.168.1.10
```

If the app starts and immediately closes right after a package was added or removed, the cause is a
stale build cache rather than the code: the resource ids managed code uses and the ones in the APK
disagree, and the bottom tab bar throws `This component requires that you specify a valid
TextAppearance attribute`. Delete `src/BarberSearch.Mobile/obj` and `bin` (or Rebuild in the IDE),
then install again.

## Tests

```powershell
dotnet test tests/BarberSearch.Mobile.Core.Tests
```

Covers the HTTP clients (real `HttpClient` with a stub handler: JSON parsing of search, card and
slots, catalog arrays, booking body and `201 { id }`, `ProblemDetails` →
`BarberSearchApiException`, empty filters omitted), the search and profile ViewModels, the barber
card ViewModel (slot defaults in the salon's zone, service/salon/date changes, stale slot
responses, the booking state machine and its retries), the search ViewModel (every filter in one
request, the catalogs' loading/error/retry states and the empty-state wording) and the salon
clock/text helpers. The bookings slice adds the list, cancellation, reschedule and
review clients (query string, opaque cursor, `204` without a body, error codes), `BookingText`, and
the three ViewModels: two independent lists with cursor loading and month groups, profile
resolution with a per-profile cache, cancel/reschedule/review state machines and their error texts,
all with fixed time and fake APIs — no emulator. The barber-card slice adds `MediaUrl` (join with
the API base, variants only for own media), the new `BarberText` formatting, the review page cache,
cursor handling, both kinds of review errors and the «Дальше» → «Повторить» retry, the portfolio
viewer's counter and caption, and the card's new sections with their hide rules. The password-reset
slice adds the anonymous request client (body, `204`, `ProblemDetails`) and its screen ViewModel:
the client-side email checks, the exact «Проверьте почту» wording, the server's failures, the local
cooldown and its 60-second tick. The geo-search slice adds the invariant formatting of the
coordinate pair, the search ViewModel's location states (position sent, refusal and unavailable
with their hints, a single read per screen, the retry) and `BarberText.Distance` in both languages.
The push slice adds `PushOptions` parsing (missing, broken, `enabled: false` and blank fields all
give `Disabled`), the registrar lifecycle (session started/ended and generations, FCM token refresh,
sign-out-before-logout ordering, failed-unregister retry, transient failure, one `POST` per token,
marker clearing), the push API client (both calls are `204` without a body, `ProblemDetails` on
errors), payload parsing and the «Записи» route, and the «Профиль» switch with its
denied/unavailable texts and the no-hint rule for a transient failure — 47 new cases, **624 tests**
in total. The booking-creation event adds two payload-routing cases (a `booking.created` tap opens
«Записи», with or without `bookingId`), and the barber confirmation, cancellation and reschedule
events add three more, which brings the suite to **842 tests**. The cabinet slice adds the gate
and the four section ViewModels, the seven child screens' ViewModels and the five cabinet API
clients; it brings the suite to **1402 tests**. The page layer, the two page timers and the
platform intents are not covered — `AGENT.md` §5 says where the risk sits. The Android build
stays at 0 warnings; the iOS C# compile is clean after an iOS
`PlatformTheme` no-op partial and the CA1711 suppressions: one explicit line in the existing
`Platforms/**/*.cs` section (the `AppDelegate`/`AndroidPushPermission` names) plus the pointwise
`IPushPermission.cs` entry. No real FCM delivery was exercised: there is no Firebase project, so the
transport is covered by stubs and the backend integration tests use a capturing sender.

## iOS

Building and running iOS needs macOS with Xcode 26 and the `ios` workload. There is no Mac in the
project yet, so iOS is built in CI only until a machine appears (see the plan in the project
notes).
