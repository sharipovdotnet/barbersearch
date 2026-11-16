# AGENT.md — BarberSearch (mobile)

> Context for agents and developers. Read together with the `README.md` next to it and with
> `backend/AGENT.md` (§7 — domain, §16 — API, §17 — authorization, §20 — push) and
> `frontend/AGENT.md` (§7 — design system, §8 — UI kit): the mobile UI is designed after the web
> app, see §8 below.

---

**Contents:** [§1. What this is](#1-what-this-is) · [§2. Where things live](#2-where-things-live) ·
[§3. Stack](#3-stack) · [§4. Build and verify](#4-build-and-verify) ·
[§5. Decisions made and why](#5-decisions-made-and-why) ·
[§6. What the backend still lacks](#6-what-the-backend-still-lacks) ·
[§7. Working rules](#7-working-rules) ·
[§8. Design source: the web frontend](#8-design-source-the-web-frontend)

---

## 1. What this is

The BarberSearch client mobile app for iOS and Android on .NET MAUI (.NET 10).
The user searches for a barber, opens the card and the free slots, books an appointment, sees
their bookings, and — on Android — receives a push reminder an hour before a confirmed visit.

The barber cabinet and the admin section live on the web (`frontend/`). They will not exist in the
mobile app: a schedule and working hours are not filled in from a phone.

## 2. Where things live

| What | Path |
|---|---|
| Solution | `mobile/BarberSearch.Mobile.sln` |
| App (MAUI) | `mobile/src/BarberSearch.Mobile` |
| Core without MAUI (HTTP client, ViewModels) | `mobile/src/BarberSearch.Mobile.Core` |
| Interface language, English catalog | `mobile/src/BarberSearch.Mobile.Core/Localization` |
| App language switch and XAML `Translate` markup | `mobile/src/BarberSearch.Mobile/Localization` |
| Push contracts, registrar, options, payload (no MAUI) | `mobile/src/BarberSearch.Mobile.Core/Push` |
| Push platform code (Android FCM, notifications, tap router) | `mobile/src/BarberSearch.Mobile/Platforms/Android/Push`, `mobile/src/BarberSearch.Mobile/Push` |
| Firebase config (ignored by `mobile/.gitignore`) | `mobile/src/BarberSearch.Mobile/Resources/Raw/push.json`; template — `push.example.json` beside it |
| Core tests | `mobile/tests/BarberSearch.Mobile.Core.Tests` |
| API contracts (project reference) | `backend/BarberSearch/src/BarberSearch.Contracts` |
| Git root | `D:\Projects\barbersearch` — the mobile app sits inside the repository, not next to it |

## 3. Stack

| Component | Version / value |
|---|---|
| TFM | `net10.0-android`, `net10.0-ios` (core and tests — `net10.0`) |
| SDK | `10.0.303` (`global.json`) |
| MAUI | workload from Visual Studio 18.8 (`Microsoft.Maui.Controls` 10.0.20) |
| Typeface | Geist 1.9.2 — static TTF `Regular`/`Medium`/`SemiBold`, same as the web |
| MVVM | CommunityToolkit.Mvvm 8.4.0, `[ObservableProperty]` on fields |
| Android | `SupportedOSPlatformVersion` 24, `ApplicationId` `ru.barbersearch.mobile` |
| iOS | `SupportedOSPlatformVersion` 15.0 |
| Android push | `Xamarin.Firebase.Messaging` **125.0.0.1**, pinned exactly in an Android-only `ItemGroup`; no `google-services.json` |
| Firebase config | `Resources/Raw/push.json` (ignored by `mobile/.gitignore`), initialized programmatically; `push.example.json` is a template and is excluded from the APK |

## 4. Build and verify

```powershell
cd mobile
dotnet test tests/BarberSearch.Mobile.Core.Tests                       # core, no emulator
dotnet build src/BarberSearch.Mobile/BarberSearch.Mobile.csproj -f net10.0-android
dotnet build src/BarberSearch.Mobile/BarberSearch.Mobile.csproj -f net10.0-ios   # macOS only
```

On Windows only Android builds: the iOS target needs macOS with Xcode 26. The whole solution
(`dotnet build BarberSearch.Mobile.sln`) fails on iOS on Windows — that is expected, build the app
project with `-f net10.0-android`.

Adding or removing an Android package changes the resource ids that managed code sees, and an
incremental build can keep an old `_Microsoft.Android.Resource.Designer.dll` next to a freshly
linked APK. The app then starts and dies on the first Shell frame with
`java.lang.IllegalArgumentException: This component requires that you specify a valid TextAppearance
attribute` — `PlatformInterop.CreateNavigationBar` receives a stale `Resource.Attribute` id, which
resolves to an unrelated style, and `BottomNavigationView` refuses it. No code is involved: delete
`src/BarberSearch.Mobile/obj` and `bin` (or Rebuild) and install again. The same mismatch makes any
other `Resource.*` id wrong, so treat the crash as a build-cache symptom, not a theme bug.

Push needs no Firebase project at build time. Without `Resources/Raw/push.json` the Android app
compiles and behaves exactly as before: no notification permission, no «Присылать напоминания» row,
no Firebase initialization. A project is needed only to exercise real delivery; see §5 and the
README.

A local backend is required before running (`backend/docker compose up -d` +
`dotnet run --project BarberSearch/src/BarberSearch.Web`). The Android emulator reaches the host as
`10.0.2.2:5243`, the iOS simulator as `localhost:5243`. A physical phone cannot see the machine's own
address, so it reaches the host as `localhost:5243` too, and the port is forwarded by
`adb reverse tcp:5243 tcp:5243` (`tcp:5173` for the site) once per connection; a phone on the same
network may use the machine's address instead via `-p:DevApiHost=192.168.1.10`. `DevelopmentEndpoints`
in Core owns both base addresses.

## 5. Decisions made and why

- **MAUI, not Avalonia/Uno/Blazor Hybrid.** iOS and Android only, a native look and the need for
  background features (geofences, push) — native controls and first-party APIs win.
- **The core as a separate project.** ViewModels and the HTTP client are tested with xunit without
  an emulator; MAUI dependencies stay in the app.
- **Contracts by project reference, not a copy.** An API drift breaks the build immediately.
- **Location in the repository.** `mobile/` next to `backend/` and `frontend/`: one git root, one CI,
  the `ProjectReference` to the contracts works both locally and in GitHub Actions.
- **`NuGet.config` inside `mobile/`.** The machine's global config enables `packageSourceMapping`
  without the MAUI packages, which made restore fail with NU1100. The local config is scoped to the
  subdirectory.
- **`ApplicationId` `ru.barbersearch.mobile`** — change it before the first release; after shipping
  to the stores it cannot be changed.
- **Tokens are copied from the web by hand, not generated.** `Colors.xaml` is a transcription of
  `frontend/src/index.css` (oklch → sRGB hex, alpha kept), and Geist is the official release font.
  A design generator would introduce a third palette to reconcile; the rule in §8.3 is what keeps
  two clients one product.
- **Salon time, not device time.** Slot times are UTC and are shown in the salon's IANA zone from the
  slot response; an unknown zone falls back to UTC (the web uses the browser zone, a phone has no
  equivalent). The card's "today" is the salon's calendar date, and booking times go back as the
  same instants the API returned, together with an idempotency key per attempt.
- **Bookings are a list, not a hero and a timeline.** The «Записи» tab keeps the web's data and
  wording but drops the web's "next booking" hero and the request → confirmation → visit timeline:
  on a phone the first card of «Предстоящие» carries the «Ближайшая запись» badge and the countdown,
  and the status badge carries the state. The lists also refresh themselves every 30 seconds while
  the tab is on screen and the app is in the foreground (the page stops both timers on disappearing
  and on `Window.Stopped`, and restarts them on `Window.Resumed`), and there is no «Обновить»
  button: a second page timer calls `BookingsViewModel.AutoRefreshAsync`, which re-reads the first
  page of the active list silently — the rows stay until the answer, and a failed refresh keeps
  them — on top of the refresh on appearance, on pull-to-refresh and after an action, while the
  countdown still ticks once a minute. The auto-refresh yields to whatever else is running on the
  screen (a cancel, a pull, the first load, a page append) and leaves alone a list where the user
  loaded a second page: a silent replace would throw those rows away. This is a deliberate deviation
  from §8 "the web is the reference" — the web polls «Предстоящие» once a minute — recorded here
  rather than left as an omission.
- **The «Профиль» tab is a hub, not a copy of the web cabinet.** The web wraps every account route
  in its own access gate and renders the avatar card, the email card, the profile form and the
  account details on one page. Mobile keeps one tab: the sign-in form without a session (with a
  «Нет аккаунта? Зарегистрироваться» link under it), then a
  person card (initials, name, email, role, «Email не подтверждён») with «Личные данные», «Мои
  отзывы», «Безопасность» and «Выйти», and the sections as Shell pages over the profile stack.
  Avatars are out of this slice — nothing is uploaded, deleted or shown — but `avatarUrl` is still
  round-tripped on every profile save, because `PUT api/users/{id}/profile` replaces the whole file
  and `null` would erase a photo uploaded on the web. Email verification and the address change
  finish in the browser: the letter's link opens the web page, the app neither opens nor parses it,
  and the session simply ends after the confirmation rotates `SecurityStamp`. Changing the password
  does need an immediate second sign-in in the app, unlike the web: the server revokes every refresh
  token and rotates `SecurityStamp`, so the mobile pair is dead the moment `204` arrives, and the
  app re-authenticates with the new password instead of keeping a dead session.
- **Registration is a page over the profile stack, and it signs in by itself.** Until this slice a
  customer could only be created on the web, which made a phone-only sign-up a dead end. The screen
  repeats `frontend/src/components/register-form.tsx` — the same fields in the same order, the same
  client checks, the same `POST api/auth/register` followed by a `login` with the same password —
  and opens from the «Нет аккаунта? Зарегистрироваться» link under the sign-in form, so the
  «Профиль» tab stays the single entry screen and the return keeps it in place. Three details are
  mobile decisions. The Policy link opens the public site in the system browser
  (`Core/Legal/PrivacyPolicy.cs` mirrors `frontend/src/lib/legal.ts`; the dev base address is the
  web dev server, the counterpart of the backend's `App:PublicUrl`), because the Policy is the
  Operator's document and a copy inside the app would go stale between releases. An outdated
  consent version (`400 auth.register.consentVersion`) asks to update the app instead of reloading
  the page: the revision is a build-time constant and there is nothing to re-read. And when the
  account is created but the automatic sign-in fails, the form is replaced by a «Аккаунт создан»
  card with a plain sign-in, because a second `POST` would answer `409`.
- **A forgotten password is requested from the app and reset in the browser.** The sign-in form
  carries the web `LoginDialog`'s «Забыли пароль?» link, which opens `forgot-password` over the
  profile stack: one email field, `POST api/auth/password-reset`, the generic «Проверьте почту»
  card for every address — the server answers `204` for an unknown, inactive or malformed one, and
  the screen must not hint otherwise — and a local 60-second resend cooldown, because the server's
  own cooldown is silent too. The letter's `/reset-password?token=…` link opens the web page: the
  app neither opens nor parses it and has no new-password form, the same browser story as email
  verification and the address change. Two deviations from the web: the card has no «На главную»,
  because on a phone the back gesture is the return to the sign-in form, and «Безопасность» has no
  second link, because a signed-in person does not need the reset and the sign-in form is the app's
  only «forgot» entry.
- **The portfolio viewer is a page with a swipe, not a dialog with arrows.** The web opens a modal
  lightbox with «Назад»/«Дальше» buttons next to the photo; MAUI has no modal lightbox, and on a
  phone the thumb is on the picture, so the frames are paged by swiping a `CarouselView` and the
  system back button closes the page. The caption and the «N из M» counter stay; the arrow buttons
  and the keyboard shortcuts of the web are deliberately dropped.
- **Reviews live in a nested section, not in the card ViewModel.** Pagination, a per-page cache, an
  error on the *next* page that must keep the rows already shown and a retry with the same cursor is
  a life cycle of its own, and the card file was already over a thousand lines.
  `BarberReviewsSection` is still one ViewModel per screen: the page binds to
  `BarberCardViewModel.Reviews`, and the section owns only the review list and its pager — it is not
  a second `BindingContext`.
- **The tab bar carries the web's own lucide icons.** Four tabs need four marks, and the web has
  no tab bar to copy; so each tab takes the icon of the section it stands for — `search` for
  «Поиск», `map-pin` for «Карта» (the web's own mark for a place; the map screen itself has no
  counterpart), `calendar-check` for «Записи» and `user-round` for «Профиль» (the last two are the
  barber cabinet nav's own icons). The files are stroke-only SVGs at 24 in `Resources/Images`:
  the colour is written as `#171717` but nothing depends on it, because Shell tints the icons
  through `TabBarForegroundColor` and `TabBarUnselectedColor` — selected and unselected states
  follow `Primary` / `MutedForeground` in both themes, and no new colour, font or package enters
  the app.
- **The card shows the avatar, the account hub still does not.** The public card reads the already
  public `avatarUrl` from `GET api/barbers/{id}` and only renders it — nothing is uploaded, removed
  or moderated, and the initials from `BarberText.Initials` stay as the fallback. The account hub
  keeps initials because the account slice allows no avatar management at all; a read-only avatar
  there would promise an editing flow that does not exist.
- **Search filters are the web's six, with two touch adaptations.** City, date, language,
  specialization, hair service and the additional-service substring travel in one request, only on
  «Найти», and the catalogs for the two selects come from `GET api/specializations` and
  `GET api/hair-services`. Two things are not the web's: the date is a `Picker` of «Любая дата» plus
  the booking horizon (today + 30 days, device calendar), because MAUI's `DatePicker` cannot hold an
  empty value and the filter is optional; and the additional-service field has no `<datalist>`
  suggestions, because there is no native equivalent and the filter itself is free text. Both are
  recorded here instead of being left as silent omissions.
- **The search reads the device position, and the server puts the nearest first.** On the first
  «Найти» the screen asks for the location permission, reads the position with
  `GeolocationAccuracy.Medium` (a city block is enough for an order, and high accuracy costs battery
  and time), and sends the pair as optional `latitude`/`longitude` query parameters of the same
  `GET api/barbers` request — no second endpoint and no client-side re-sorting: the keyset page must
  stay ordered as it is returned. The server ranks by the distance to the barber's nearest active
  salon and returns `distanceKm`, so each row shows «850 м» / «1,2 км» next to the rating and the
  list says «Сначала ближайшие мастера»; without coordinates the order stays the web's rating one.
  The distance is a local flat approximation on the server, not a geodesic: at city distances the
  order is the same and the cost is one `sqrt` per row, which is recorded in `backend/AGENT.md`.
  Three mobile decisions follow from the phone. The permission is requested on the first search, not
  at launch, because the person has just asked for a barber nearby. A refusal or a missing GPS fix
  never blocks the search: the request goes out without coordinates, and the results section shows
  the reason with «Повторить», which resets the remembered attempt — a person may have allowed
  access in the phone settings. And the position is read once per screen (a successful fix lives as
  long as the page, a failed one is not retried by every «Найти»), because nobody moves across town
  between two searches and an Android permission dialog must not reappear on each tap. Android asks
  for `ACCESS_COARSE_LOCATION` and `ACCESS_FINE_LOCATION` (from 12 the person chooses the
  precision); iOS carries `NSLocationWhenInUseUsageDescription` with the Russian text, because the
  system dialog follows the device language while the interface defaults to Russian. The web has no
  geolocation at all, so this is a mobile-only feature with its own wording rather than a port.
- **The city filter suggests cities while typing, and that suggestion endpoint became public.** The
  field asks `GET api/addresses/cities` after a 250 ms pause (the web's debounce), from two
  characters and for at most eight suggestions, and a chosen row replaces the text with the bare city
  name: the server compares `Salon.City` exactly, so «Екатеринбург» from a suggestion finds salons
  while «г. Екатеринбург» does not. The list is a `BindableLayout` of rows under the field instead of
  a web-style popup — MAUI has no native combobox, and a hand-built overlay over a scrolling form
  would cost more than it adds on a phone. Its state lives in `CitySuggestions`, owned by
  `SearchViewModel` the way «Отзывы» is owned by the card: still one ViewModel per screen, not a
  second `BindingContext`. Everything about it is silent on failure: 409 (suggestions off), 401, 429,
  5xx and a dead network only close the list, the field stays a plain input, and an empty answer shows
  the web's «Ничего не найдено.». Two consequences are mobile-only. The endpoint used to be
  `[Authorize]`d, which would have hidden suggestions on the app's main tab because the search works
  before sign-in; the cities action is now `[AllowAnonymous]` while address suggestions stay
  authenticated, and anonymous calls share the IP partition of the `addresses` limiter (60 a minute,
  after which the list simply does not open). And the app never keeps `providerId`: the search
  filters by the city string, while the provider token belongs to the web salon form's address
  suggestions.
- **The interface speaks Russian and English, and the web is the source of both.** The approach is
  the web's gettext one: the key is the Russian source string, and `Localization/messages/en.json` in
  Core holds the translation. `Loc.T("…")` in code and `{loc:Translate Key='…'}` in XAML cover every
  visible label, placeholder, dialog and error; formatting helpers take the culture from the chosen
  language (`ru-RU` / `en-GB`), so dates stay day-month, time stays 24-hour and the price is
  «2 400 ₽» / «RUB 2,400». The default is Russian, and the choice is stored in `Preferences` under
  `barbersearch.locale` — the web's key, but the phone's own storage. The web puts the switcher in
  the header; mobile has no header, so the compact RU/EN pair lives at the top of «Профиль», the only
  settings screen. Changing the language persists the choice and **rebuilds Shell from DI**: the
  pages and ViewModels are transient, so their strings are created again with the new language, and
  the app returns to «Профиль». This is the phone's equivalent of a page reload — the alternative,
  an event in every ViewModel, would be a second, fragile localization mechanism. Because pages are
  rebuilt, the singleton session subscriptions of `ProfilePage` and `BookingsPage` are attached on
  appearing and detached on disappearing. The English catalog follows the frontend glossary and
  wording, including the browser's `en-GB` conventions; an untranslated string stays Russian rather
  than turning into a key. One surface stays outside the catalog on purpose: the calendar drawn by
  the native date picker keeps the device language, as the browser-drawn calendar does on the web;
  every date the app formats itself is localized.
- **The theme is a device setting with its own switch, and it follows the system by default.** The
  web's `index.css` already carries a `.dark` palette but no toggle — frontend/AGENT.md §7.4 leaves
  the default open — so the phone is where the choice becomes reachable. `Colors.xaml` is unchanged:
  its light/`…Dark` pairs were the port's starting point and every use site already picked a side
  through `AppThemeBinding`. `ThemePreference` in Core (`System`/`Light`/`Dark`, stored as
  `system`/`light`/`dark`) and `PreferencesThemeStore` under `barbersearch.theme` hold the choice;
  `ThemeService` maps it to `Application.UserAppTheme`, so `AppThemeBinding` repaints the open pages
  in place. Unlike the language, changing the theme does **not** rebuild Shell: no string changes,
  only the palette, so the tab and its state stay where they are. The three-segment switcher sits
  next to the RU/EN pair at the top of «Профиль» and shares `SegmentButton`/`SegmentButtonSelected`
  with it; those styles are theme-aware, while the old code-behind painted the selected language
  with `Muted`/`Foreground` directly and would have kept the light shade after switching to dark.
  Android's system bars sit outside `AppThemeBinding`: on API 24–34 `PlatformTheme` colours them and
  flips the icon appearance at startup (`MainActivity`) and on every `RequestedThemeChanged`. From
  Android 15 the system draws the status bar itself in edge-to-edge mode and ignores
  `SetStatusBarColor`, so `PlatformTheme` leaves the whole call out on 35+ — toggling only the icons
  there would put dark icons on the platform's dark bar. iOS takes the window's user-interface style
  from the same theme and needs no platform code.
- **A physical phone reaches the dev backend through `adb reverse`, not through the machine's LAN
  address.** The app has to name one address, and the machine's address is the one thing a phone does
  not know: `10.0.2.2` is the emulator's alias for the host and on a device it means the device
  itself. So the device build uses `localhost` and the port is forwarded over the cable
  (`adb reverse tcp:5243 tcp:5243`, `tcp:5173` for the site): it works on any network, needs no
  firewall rule and survives the machine changing its address. The LAN story is not dropped but moved
  behind a build parameter — `-p:DevApiHost=192.168.1.10`, written into the assembly as metadata and
  read in debug only — because a phone on the same Wi-Fi is a different topology, not a different
  default. `DevelopmentEndpoints` in Core owns the rule and both base addresses, so the choice is one
  tested function rather than two `#if` constants.
- **The reminder text is composed by the server, in Russian, and the channel is Android/FCM-first.**
  The app never formats reminder text. FCM delivers a `notification` payload that Android shows even
  when the process is dead, so the title and body must exist before any app code runs; a data-only
  message with a background handler would be unreliable on modern Android and would add a second
  localization path. The server also owns the salon's IANA zone and the «сегодня»/«завтра» word, and
  `PushMessages` is covered byte-for-byte by backend tests. Russian is deliberate: the server stores
  no device locale, and customer-facing booking notifications follow the product's Russian wording
  (the app's RU/EN switch does not translate push). Android is the only implemented platform;
  `IosPushPermission`/`IosPushTokenProvider` are stubs that answer «disabled», so an iOS build pulls
  no Firebase, asks for no permission and shows no reminder row. APNs is a later slice — the shared
  token table already reserves `PushPlatform.Ios`.
- **Firebase is a local file, not a build input: no `google-services.json`, and the app builds
  without a Firebase project.** `Resources/Raw/push.json` is camelCase
  `{ enabled, projectId, applicationId, apiKey, senderId }`, where `applicationId` is the Firebase
  Android app id (`mobilesdk_app_id`, not the package name), `apiKey` is the Firebase web API key and
  `senderId` is the GCM/FCM sender id. `PushBootstrap.Initialize` reads the asset synchronously at
  startup and builds `FirebaseOptions` programmatically; the `reminders` channel is created later by
  `App`, after localization (see the next bullet). Absent, invalid, `enabled: false` or a blank field
  all become `PushOptions.Disabled`: no Firebase init, no permission prompt, no channel and no row
  in «Профиль». The file is ignored by `mobile/.gitignore` because the key and ids are per stand;
  the checked-in `push.example.json` documents the shape and is excluded from `MauiAsset`, so it
  never enters the APK. No `GoogleServicesJson` item exists, by design: the configuration must stay
  optional for the repository and CI.
- **`Xamarin.Firebase.Messaging` is pinned to 125.0.0.1 and referenced only by the Android target.**
  Newer 125.0.1+/125.1.x lift AndroidX, and restore fails with NU1608 — an error under
  `TreatWarningsAsErrors`; 125.1+ also marks the token APIs (`GetToken`, `OnNewToken`) obsolete,
  while `FcmPushTokenProvider` is built on them. 125.0.0.1 restores and builds clean, so the exact
  version is intentional, not a leftover. The `ItemGroup` is conditioned on the Android platform, so
  the iOS build references no Firebase package at all.
- **The device token is a lifecycle, not one registration call.** Registration runs on
  `TokenSession.SessionStarted` (only the signed-out → signed-in transition, not on every refresh),
  on `Window.Resumed` (covers a session restored from storage and every return to the foreground) and
  on FCM `OnNewToken` (reinstall, cleared data, token rotation). An explicit sign-out unregisters
  **before** the logout request (with a 5-second cap), not merely before `TokenSession.ClearAsync`:
  `POST unregister` needs the Bearer pair, and logout revokes it. A failed unregister keeps the
  persisted marker and is retried by the next ensure while the toggle is off; a later sign-in always
  re-POSTs the token, so it moves to the new user instead of staying with the old one. An expired
  session cannot send unregister at all, so its marker simply stays. Every call is swallowed and
  never blocks a screen: after login or profile load the registration is fire-and-forget, with
  `IsLoading`/`IsSigningIn` reset first, so the permission dialog and the FCM round-trip cannot hold
  the spinner or the «Входим…» button. A failed registration clears the marker; a transient failure
  (HTTP, timeout, cancellation) is `PushTokenStatus.TransientFailure`, while only the device-level
  `Unavailable` means «this phone cannot receive push». A session generation counter stops a
  late registration from writing the marker after sign-out. `IPushStateStore` keeps the choice and
  the last token under `barbersearch.push.enabled` / `barbersearch.push.token`, and an in-session
  token field prevents a second `POST` for the same token.
- **«Присылать напоминания» exists only when push is configured.** The row sits between the email
  card and «Личные данные» in «Профиль», is a global `Switch` with a 44 pt target, and is hidden
  entirely when `IsPushConfigured` is false. It is on by default (`IPushStateStore.IsEnabled` defaults
  to `true`). A denied permission shows «Уведомления выключены в настройках телефона. Разрешите их,
  чтобы получать напоминания.» with «Открыть настройки» and keeps the person's switch on; no Play
  services / no FCM token shows «Уведомления недоступны на этом устройстве». A transient network
  failure shows no hint at all — the next ensure retries. Switching off calls `SetEnabledAsync(false)`,
  which attempts to unregister the device and keeps the marker if the request fails, so the next
  ensure retries while the toggle is off. The channel is `reminders`, importance `Default`; its
  visible name and description are localized keys «Напоминания» / «Напоминания о записях» and are
  created/updated by `App` **after** localization is initialized (not in `MauiProgram`), only when
  push is configured. Notifications use the monochrome lucide bell vector
  `Platforms/Android/Resources/drawable/notification_bell.xml` as the small icon, and the manifest
  sets `com.google.firebase.messaging.default_notification_icon` /
  `...default_notification_channel_id=reminders`, so a background notification shown by the system
  gets the same icon and channel. `POST_NOTIFICATIONS` is declared in the manifest and requested
  only after push is configured. A foreground message is posted locally (`AndroidNotifications`), a
  background one is shown by the system. A tap opens the «Записи» tab via `//bookings`: cold start
  reads the extras in `MainActivity.OnCreate` **only when `savedInstanceState is null`** (a restored
  process must not jump tabs), warm start in `OnNewIntent` (`LaunchMode.SingleTop`), and the router
  waits for `Shell.Current`. There is no per-booking deep link because `GET api/bookings/{id}` does
  not exist and the list already shows the visit. Tests add 47 cases (624 total): options parsing and
  the `Disabled` fallback, the registrar lifecycle (session events and generations, token refresh,
  sign-out-before-logout ordering, failed-unregister retry, transient status, one `POST` per token),
  the API client (`204`, no body), payload parsing/routing and the «Профиль» toggle/status texts
  (including the rule that a transient failure shows no device hint). iOS compiles after an iOS
  `PlatformTheme` no-op partial and the CA1711 suppressions: one explicit line in the existing
  `[src/BarberSearch.Mobile/Platforms/**/*.cs]` section (the MAUI-mandated `AppDelegate` and
  `AndroidPushPermission` names) plus the pointwise `IPushPermission.cs` entry. No real FCM delivery
  was exercised because there is no Firebase project. The production backend stays `Push:Enabled=false`
  until `/privacy` is amended and `PrivacyPolicy.Version` is bumped (`backend/AGENT.md` §20.9); a
  build without `push.json` sends nothing anywhere.

## 6. What the backend still lacks

The mobile MVP is blocked by two backend changes (`backend/`) that do not exist yet:

1. **Geo points for the map.** `Salon` has coordinates and the public search now ranks by the
   distance to the nearest one, but there is still no public endpoint of points within a radius or
   bbox for the map screen, and the search response exposes the distance rather than the
   coordinates. Needed: a points endpoint (and coordinates in the public projection) before the
   «Карта» tab can draw anything.
2. **Reading favourites.** `POST`/`DELETE /api/users/{id}/favorites/{barberProfileId}` exist, but
   there is no `GET` for the list.

Push reminders are **no longer** in this list: the backend has device/token tables, the FCM
transport and the worker (`backend/AGENT.md` §20), and the app registers the device, shows the
reminder and routes a tap to «Записи». A per-booking deep link is still impossible — there is no
`GET api/bookings/{id}` — but the list already contains the visit, so the slice does not need it.
Until the two items above land, the "Map" and "Favourites" screens have nothing to build on.

## 7. Working rules

- **Language.** Markdown files (`AGENT.md`, `README.md`, any docs) are written in **English**.
  Code comments and XML docs are written in **Russian**, as in `backend/`. Public types are
  documented with the why, not a restatement of the name.
- **Design.** UX and UI are not invented here: the web app is the reference, see §8. Colouring,
  typography, radii, states and wording come from `frontend/`.
- Any warning is a build error (`TreatWarningsAsErrors`). Suppressing a rule — only pointwise in
  `.editorconfig` with a justification.
- Test names — `Method_Scenario_Expected`; CA1707 is disabled for them.
- New screens: ViewModel in `BarberSearch.Mobile.Core`, the page in the app, dependencies through
  the constructor, registration in `MauiProgram`.
- `dotnet sln add` in `backend/` is forbidden (backend/AGENT.md §5.1); in `mobile/` the solution is
  maintained with a plain `dotnet sln`.
- **Branch first, then the first edit.** Before a single file in the working tree changes, the work
  must have a branch of its own; if there is none, create one from an up-to-date `main` —
  `git switch -c docs/… main`, with `docs/…`, `features/…` or `fix/…` named after the change. A
  session never edits on `main` and never picks up a branch left over from another task.
- **Session end: commit, push, pull request.** Finished work is never left in the working tree.
  When the slice is done, commit it on that branch, push the branch to `origin` and open a pull
  request against `main` without waiting to be asked. The PR description is one or two sentences in
  English about what this session changed — not a file list and not a retelling of the diff. Stage
  only the work of the session: files that were already modified before it started are not part of
  the commit.
- **PR conflicts are resolved by the agent, automatically.** A pull request that GitHub reports as
  conflicting with `main` is not handed back to anyone: fetch the current `main`, merge it into the
  branch, resolve every conflict by hand — keeping the intent of both sides instead of dropping one
  — re-run the checks in §4, push the resolution and confirm the PR is mergeable again before
  reporting it done. `git push --force` over someone else's commits and "please resolve the
  conflicts" both stay out of the workflow.

## 8. Design source: the web frontend

The web app (`frontend/`) is the single source of truth for UX and visual language. The mobile app
is the same product on a phone, not a second design: before building a screen, open its web
counterpart in a browser, copy the structure, the states and the wording, then adapt the input
model to touch.

### 8.1 Where to look

| What | Where | Details |
|---|---|---|
| Design tokens: colour, radius, type | `frontend/src/index.css` | frontend/AGENT.md §7.1–7.2 |
| Typography, layout, motion | `frontend/src/index.css` | §7.3–7.4 |
| Primitives: button, input, badge, card, field, checkbox, avatar, skeleton | `frontend/src/components/ui` | §8 |
| Screen structure and states | `frontend/src/pages/*.tsx` — `home-page` (search), `barber-page` (barber card), `account-page` (bookings, reviews) | §7.3 |
| Wording and terms | `frontend/src/i18n` — Russian source strings, glossary | i18n/README.md |

### 8.2 What porting means in MAUI

The port is done: this is the state to keep, not a plan.

- **Tokens are resources, not literals.** `Resources/Styles/Colors.xaml` holds the web names as
  semantic keys — `Background`, `Foreground`, `Card`, `Muted`, `MutedForeground`, `Primary`,
  `PrimaryForeground`, `Secondary`, `Accent`, `Border`, `Input`, `Ring`, `Destructive`,
  `DestructiveSolid`, `Success`, `Warning` — each with a light value and a `…Dark` twin, and every
  use site chooses the theme through `AppThemeBinding`. `Styles.xaml` styles controls only through
  those keys.
- **The palette has a switch, not a second set of styles.** `AppThemeBinding` follows the device
  theme; the three-segment control on «Профиль» stores an override in `Preferences`
  (`barbersearch.theme`) and applies it through `Application.UserAppTheme`. Its selected segment is
  the same `Muted`/`Foreground` pair and 44 pt target as the language pair, so the control adds no
  colour, size or state of its own; §5 records why the phone is where the switch exists.
- **The template palette is gone, not extended.** `Primary #512BD4`, `Secondary #DFD8F7`,
  `Tertiary`, `Gray*`, `Magenta`, `MidnightBlue`, `OffBlack`, `Danger #DC2626` and the `*Brush`
  duplicates were deleted. `Platforms/Android/Resources/values/colors.xml` carries the same
  `--primary` / `--background` values for the platform chrome, and the icon and splash colours in
  the `.csproj` use `#171717` for the same reason.
- **The launcher icon and splash are the Hairo wordmark, not the template's `.NET` marks.** The
  purple `#512BD4` square with the `.NET` logotype the MAUI template ships is gone: `appicon.svg`
  is a `#171717` square, and `appiconfg.svg` carries «Hairo» set in Geist SemiBold and converted to
  outlines, so rasterizing needs no font and no machine can shift the letterforms. The wordmark
  spans 54% of the icon's square — inside the 66% circle Android's adaptive masks cannot clip, and
  small enough that the `monochrome` layer the resizetizer derives from the same file still reads.
  The splash repeats the wordmark at 75% of the square with the app's scissors under it: a launch
  screen has no MAUI runtime to host `Controls/ScissorsMark`, so the mark is a static copy of the
  same path scaled from its 24 canvas to 45% of the wordmark's width, and the two share one
  centred lockup with equal margins, because the name alone left the black screen looking like a
  placeholder. The icon keeps the name alone — the adaptive mask has no room under the text. The
  name is the product's mark, not just a picture: `ApplicationTitle` and the Shell title are
  `Hairo`, and the sign-in heading reads «Вход в Hairo». Identifiers are not text and keep their
  names: the namespaces, the project files, `ru.barbersearch.mobile` and the URLs still say
  `BarberSearch`.
- **The in-app mark is the web header's scissors, and it is geometry rather than a picture.** The
  app wears the lucide `scissors` of `site-header.tsx` — the same five figures on a 24 canvas with
  stroke 2, the two circles as arcs and the three cuts as segments. The geometry is declared once in
  `Controls/ScissorsMark.xaml`; `Path` is sealed, so the mark is a `ContentView` that wraps it and
  exposes `StrokeColor`, and the inner `Path` repaints through `AppThemeBinding` exactly like the tab
  icons. An SVG in `Resources/Images` would be rasterized with one baked colour. The path is not a
  `GeometryGroup` of `EllipseGeometry`/`LineGeometry`: that reads better but on Android (MAUI
  10.0.110, API 36) renders a stray line and ring instead of the scissors — the string is a fix,
  not a preference. In the app it appears in four places. A masthead above the search form gives
  the app's empty top the web header's lockup at phone scale — a 48 pt `Primary` tile, «Hairo» in
  Geist SemiBold 24 and the landing headline (`home-hero.tsx`) as the tagline, taken from the
  catalog in both languages. The empty-results block ports the web's empty-state card from
  `barber-results.tsx` — dashed `Border`, `Muted` circle, centred title in `GeistMedium` and muted
  description — and keeps the scissors where the web draws its `Search` icon: the empty screen then
  carries the product's face instead of a second magnifier under the «Найти» button. The top bar of
  each of the four root tabs puts the same mark before the section name through
  `Controls/BrandTitleView` in `Shell.TitleView`, so the bar reads like the web header; Shell has
  no shared title view, so every root page sets its own, and the text repeats the catalog key of
  the page's `Title`. On the signed-out «Профиль» the sign-in block opens with a lockup — a 40 pt
  `Primary` tile with the mark and «Вход в Hairo» — so the form does not start on a bare line of
  text. The empty-state icon is the deliberate divergence from the web, recorded here rather than
  left silent.
- **Values are the web's `oklch`, converted to sRGB hex.** MAUI does not parse `oklch()`, so the
  hex is a lossless transcription, checked by contrast rather than by eye: `--destructive` `#CE0000`
  still gives 5.79:1 on white, as `index.css` records for issue #34. Translucent tokens keep their
  alpha as 8-digit ARGB (`--border` 10% → `#1AFFFFFF`). When a token changes in `index.css`, the
  same value changes here — nothing else.
- **Typeface is Geist**, registered in `MauiProgram` as three static faces: `Geist` (body),
  `GeistMedium`, `GeistSemiBold` (headings). The WOFF2 in `frontend/node_modules` is unusable in
  MAUI, so the TTFs come from the official release 1.9.2; Cyrillic and `№` were verified in the
  `cmap` of all three. Bold text uses `FontFamily="GeistSemiBold"`, not `FontAttributes="Bold"`.
  Sizes follow the web scale: screen title 20–24, card title 16, body 14–16, meta 12.
- **Radius from the scale.** Base `--radius: 0.625rem` → 10: sm 6, md 8, lg 10, xl 14. The
  hand-picked `12` in `SearchPage` is now 10.
- **Accessibility carries over.** AA contrast in both themes; the `muted-foreground` on `muted`
  case is a known threshold in the web (frontend §7.2, issues #33/#34) and the same values are
  reused here. The old placeholder colour — `Gray200 #C8C8C8` on white, 1.7:1 — is gone with it.

### 8.3 Rules

1. Nothing visual is invented locally. A colour, font, radius, spacing, icon or state that the web
   does not have is a bug, not a decision. If mobile genuinely needs something new, add it to the
   web design system first (three edits in `frontend/src/index.css`, §7.2) or record the deviation
   in §5.
2. A screen repeats the web structure and its three states — loading, content, error; the empty
   state keeps the web wording instead of a new sentence.
3. Wording is taken from the web, in Russian, and follows the frontend glossary term by term
   (booking, slot, no-show…). One concept — one word in both clients. English is taken from the
   same catalog where the web already has it (`frontend/src/i18n/messages/en`), and new mobile-only
   strings follow the same glossary and tone.
4. Port principles, not markup: no Tailwind classes, no shadcn/Base UI components, no `data-*`
   state attributes, no hover-only affordances. Touch instead: targets ≥ 44 pt, no hover, primary
   actions within thumb reach rather than pinned to the top edge.
5. The map screen has no web counterpart — the web renders no map. It keeps the same tokens, the
   same card anatomy and the wording of the web barber card; the layout itself is a mobile
   decision, recorded in §5.

