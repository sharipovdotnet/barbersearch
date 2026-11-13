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

Authentication is already designed for mobile on the server side: `POST /api/auth/login` returns
the access token and the refresh token **in the body** (`TokenResponse`), `POST /api/auth/refresh`
rotates the pair, `POST /api/auth/logout` accepts `Authorization: Bearer`. The cookie branch of the
same controller serves the browser web app. Wiring the token flow into the app is the next step,
not part of the current skeleton.

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

Covers the HTTP client (real `HttpClient` with a stub handler: JSON parsing, `ProblemDetails` →
`BarberSearchApiException`, empty filters omitted) and the search ViewModel (results, replacement,
API error, network error).

## iOS

Building and running iOS needs macOS with Xcode 26 and the `ios` workload. There is no Mac in the
project yet, so iOS is built in CI only until a machine appears (see the plan in the project
notes).
