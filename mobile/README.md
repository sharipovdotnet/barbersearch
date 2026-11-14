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
and the salon clock/text helpers.

## iOS

Building and running iOS needs macOS with Xcode 26 and the `ios` workload. There is no Mac in the
project yet, so iOS is built in CI only until a machine appears (see the plan in the project
notes).
