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

An administrator opens the moderation queues and decides from the phone (§5): reviews, photos,
profiles awaiting verification and requests to become a professional.

The professional cabinet lives on the web (`frontend/`) and will not exist in the mobile app:
a schedule and working hours are not filled in from a phone. The rest of the admin section —
statistics, the catalogue, salons, users, bookings and the audit log — also stays on the web;
§5 records what the phone got and why the remainder did not move.

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
  person card (initials, name, email, role, «Адрес не подтверждён.») with «Личные данные», «Мои
  отзывы», «Безопасность» and «Выйти», and the sections as Shell pages over the profile stack.
  The card carries the hub's only verification line and there is no separate warning link: two texts
  about the same address read as two different facts.
  Avatars are out of this slice — nothing is uploaded, deleted or shown — but `avatarUrl` is still
  round-tripped on every profile save, because `PUT api/users/{id}/profile` replaces the whole file
  and `null` would erase a photo uploaded on the web. Email verification and the address change
  finish in the browser: the letter's link opens the web page, the app neither opens nor parses it,
  and the session simply ends after the confirmation rotates `SecurityStamp`. Changing the password
  does need an immediate second sign-in in the app, unlike the web: the server revokes every refresh
  token and rotates `SecurityStamp`, so the mobile pair is dead the moment `204` arrives, and the
  app re-authenticates with the new password instead of keeping a dead session.
- **An account moment shows its year only when it is not the current one.** «Аккаунт создан» and
  «Последний вход» are the only dates in the app with neither a horizon nor a month group above
  them, and an account lives for years: «ср, 12 марта в 10:15» read in 2027 would look like this
  year's date. `AccountText.Moment` therefore formats «вс, 15 ноября в 18:30» inside the current
  year and «вс, 15 ноября 2026 в 18:30» outside it, comparing the year in the device zone the
  moment is shown in — east of UTC the first of January has already come while UTC still shows the
  old year. Nothing else changes: a booking row keeps the yearless «сб, 15 нояб.» because its
  month group title carries the year, the slot picker and visit moments live within ±90 days, and
  a review and the policy edition keep an explicit year. The clock is injected as `TimeProvider`,
  as on «Записи», so the rule is tested without waiting for a new year.
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
- **A long press on a tab names the section in full; the tab label stays short.** Android draws the
  Material tooltip from the menu item's title, so the hint repeated the visible label («Поиск»).
  Four full names do not fit a phone tab bar, so the labels stay «Поиск», «Карта», «Записи»,
  «Профиль» while the tooltip says «Поиск мастера», «Карта с мастерами», «Мои записи», «Мой
  профиль» (English: «Find a barber», «Barbers on the map», «My bookings», «My profile»).
  `Navigation/TabHints.cs` owns the route → hint mapping, and
  `Platforms/Android/TabHintShellRenderer.cs` — a `ShellRenderer` that returns its own
  `ShellItemRenderer` — writes the hint to the menu items and item views of the bar. The renderer
  is registered for `AppShell` on Android only; iOS has no long-press tooltip on a tab bar and
  keeps the labels as they are.
- **The card shows the avatar, the account hub still does not.** The public card reads the already
  public `avatarUrl` from `GET api/barbers/{id}` and only renders it — nothing is uploaded, removed
  or moderated, and the initials from `BarberText.Initials` stay as the fallback. The account hub
  keeps initials because the account slice allows no avatar management at all; a read-only avatar
  there would promise an editing flow that does not exist.
- **Search filters are the web's six, with one touch adaptation.** City, date, language,
  specialization, hair service and the additional-service substring travel in one request, only on
  «Найти», and the catalogs for the two selects come from `GET api/specializations` and
  `GET api/hair-services`. The date is a native `DatePicker` whose tap opens the platform calendar,
  like the web's `<input type="date">`; `Date = null` is «Любая дата», because MAUI 10's
  `DatePicker` accepts a nullable date and renders an empty field — so the old 32-item `Picker`
  workaround for the optional filter is gone. The calendar is bounded below by the device's today
  and above by the 30-day booking horizon (today + 30 days): the web does not bound the date input,
  but a farther day has no bookable slots. A «×» button in the field clears the filter back to
  «Любая дата», and the selected date's text is drawn by the native picker, so it follows the
  device language like the calendar itself. The additional-service field still has no `<datalist>`
  suggestions, because there is no native equivalent and the filter itself is free text; that is
  now the only touch adaptation and is recorded here instead of being left as a silent omission.
- **The search reads the device position, and the server puts the nearest first.** When the
  «Поиск» tab first appears, the screen asks for the location permission and reads the position
  with `GeolocationAccuracy.Medium` (a city block is enough for an order, and high accuracy costs
  battery and time), and sends the pair as optional `latitude`/`longitude` query parameters of the
  same `GET api/barbers` request — no second endpoint and no client-side re-sorting: the keyset page
  must stay ordered as it is returned. The server ranks by the distance to the barber's nearest
  active salon and returns `distanceKm`, so each row shows «850 м» / «1,2 км» next to the rating
  and the list says «Сначала ближайшие мастера»; without coordinates the order stays the web's
  rating one. The distance is a local flat approximation on the server, not a geodesic: at city
  distances the order is the same and the cost is one `sqrt` per row, which is recorded in
  `backend/AGENT.md`. The city filter and the distance order share one read (see the next bullet),
  so the permission is requested on the tab's first appearance, not on the first «Найти»: the city
  has to be visible before the person searches. A refusal or a missing GPS fix never blocks the
  search: the request goes out without coordinates, and the results section shows the reason with
  «Повторить», which resets the remembered attempt — a person may have allowed access in the phone
  settings. And the position is read once per screen (a successful fix lives as long as the page,
  a failed one is not retried by every «Найти»), because nobody moves across town between two
  searches and an Android permission dialog must not reappear on each tap. Android asks for
  `ACCESS_COARSE_LOCATION` and `ACCESS_FINE_LOCATION` (from 12 the person chooses the precision);
  iOS carries `NSLocationWhenInUseUsageDescription` with the Russian text, because the system
  dialog follows the device language while the interface defaults to Russian. The web has no
  geolocation at all, so this is a mobile-only feature with its own wording rather than a port.
- **The city filter fills itself from the position, and «Москва» is the fallback.** The same first
  read that orders the results by distance also reverse-geocodes the position
  (`Geocoding.Default.GetPlacemarksAsync`, capped at five seconds) and puts `Placemark.Locality`
  into the city field, normalized by `CityName.Normalize`: «г. Москва» becomes «Москва», because
  the server compares `Salon.City` exactly. Any failure — a denied permission, no GPS fix, a
  missing placemark, a geocoder timeout or error — writes «Москва», the product's default city;
  the causes are deliberately not distinguished here, unlike the sorting hint. The automatic value
  is remembered, so «Повторить» on the location hint replaces «Москва» with the real city once
  access is granted, while a city typed or picked from the suggestions is never overwritten. A
  programmatic fill suppresses the suggestion list, which would otherwise open by itself on
  launch. This is a deliberate deviation from the earlier decision to ask for the permission on
  the first «Найти»: the Search tab is the app's first tab, so the system dialog now appears at
  launch, and the field shows «Определяем геопозицию…» while the city is resolved. The city name
  comes back in the device language: the platform geocoder has no locale of its own while the
  product's salon cities are Russian, so a phone set to English can produce a name the exact-match
  filter does not know — recorded as a known limit rather than hidden. Tests add 26 cases (874
  total): the auto-fill on success and with a prefixed locality, the default city for a denied,
  unavailable, empty or failed geocoder, the user's own city kept, no suggestion request, the
  shared position read, «Повторить» replacing the default, and `CityName.Normalize` itself.
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
  The channel carries seven events: the hour-before reminder (`booking.reminder`); the
  booking-creation event (`booking.created`), which the server sends both when the customer books a
  barber outside the app and when a barber books the customer; and five booking-change events —
  confirmation (`booking.confirmed`), cancellation (`booking.cancelled`), reschedule
  (`booking.rescheduled`), and a customer's own cancellation or reschedule made outside the app
  (`booking.cancelledByCustomer`, `booking.rescheduledByCustomer`). Silence follows the request
  surface, not the author: a customer's own create, cancel or reschedule from the app produces no
  push — the screen already confirmed the result, and a cancel or reschedule queues an email to the
  verified address instead — while the same action from the web does. Barber and admin actions
  always push, even
  with the `app` header. The app marks itself with `X-Client-Surface: app` on the create, cancel
  and reschedule requests; the backend treats a missing or unknown header as another surface and
  pushes, and never rejects the request over it. All seven texts are composed by the server, all
  seven open «Записи», and all seven reuse the same `reminders` channel and the same «Присылать
  напоминания» switch. Switching reminders off unregisters the device, so while the switch is off
  the server cannot send any of them, the five booking-change events included; a separate toggle
  would need a second registration concept and is out of scope. The creation event arrives on the
  worker's next tick when it pushes, and a guest booking (no account) produces none. The app never
  formats push text. FCM delivers a
  `notification` payload that Android shows
  even when the process is dead, so the title and body must exist before any app code runs; a data-only
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
  ensure retries while the toggle is off. The switch and the `reminders` channel are shared with
  every booking event, none of which has a toggle of its own: switching reminders off stops every
  push the channel carries, the reminder and all six booking events. The channel is `reminders`,
  importance `Default`; its
  visible name and description are localized keys «Напоминания» / «Напоминания о записях» and are
  created/updated by `App` **after** localization is initialized (not in `MauiProgram`), only when
  push is configured. Notifications use the monochrome lucide bell vector
  `Platforms/Android/Resources/drawable/notification_bell.xml` as the small icon, and the manifest
  sets `com.google.firebase.messaging.default_notification_icon` /
  `...default_notification_channel_id=reminders`, so a background notification shown by the system
  gets the same icon and channel. `POST_NOTIFICATIONS` is declared in the manifest and requested
  only after push is configured. A foreground message is posted locally (`AndroidNotifications`), a
  background one is shown by the system. A tap on any of the seven events opens the «Записи» tab via
  `//bookings`: cold start reads the extras in `MainActivity.OnCreate` **only when
  `savedInstanceState is null`** (a restored process must not jump tabs), warm start in
  `OnNewIntent` (`LaunchMode.SingleTop`), and the router waits for `Shell.Current`. There is no
  per-booking deep link because `GET api/bookings/{id}` does not exist and the list already shows
  the visit. Tests add 47 cases (624 total): options parsing and
  the `Disabled` fallback, the registrar lifecycle (session events and generations, token refresh,
  sign-out-before-logout ordering, failed-unregister retry, transient status, one `POST` per token),
  the API client (`204`, no body), payload parsing/routing and the «Профиль» toggle/status texts
  (including the rule that a transient failure shows no device hint). The booking-creation event
  adds two payload-routing cases, and the barber confirmation, cancellation and reschedule events
  add three more, which brings the suite to 842 tests. iOS compiles after an iOS
  `PlatformTheme` no-op partial and the CA1711 suppressions: one explicit line in the existing
  `[src/BarberSearch.Mobile/Platforms/**/*.cs]` section (the MAUI-mandated `AppDelegate` and
  `AndroidPushPermission` names) plus the pointwise `IPushPermission.cs` entry. No real FCM delivery
  was exercised because there is no Firebase project. The production backend stays `Push:Enabled=false`
  until `/privacy` is amended and `PrivacyPolicy.Version` is bumped (`backend/AGENT.md` §20.9); a
  build without `push.json` sends nothing anywhere.

- **The «Профиль» tab runs one typographic step above the rest of the app.** Screen title 22, card
  title / person name 18, body 16, meta 14 — exactly one step up the same 12/14/16/18/20 scale, no
  new size or font. The smallest text in the product (email verification status, role, reminder
  caption) lives on that tab, and the tab exists to read one's own account data; other pages keep
  the default body of 14. This is a deliberate deviation from the web's text-xs/text-sm pairing on
  that page, recorded here rather than left silent. The load-failure message is the one exception:
  its headline stays 16 and its reason 14, the app-wide pair, so the same «Не удалось загрузить …»
  reads the same size on «Записи» and «Профиль».

- **A rejected field is explained under that field, not by the form's generic failure.** The
  personal-details form already mapped `user.updateProfile.firstName` and `.lastName` to their
  fields, but an invalid phone fell through to «Не удалось сохранить профиль.» — a sentence that
  names neither the field nor the rule. The phone stays optional and the server remains the only
  authority on its format (`PhoneNumber` in the domain owns the Russian-mobile rule and its tests),
  yet `400 user.updateProfile.phoneNumber` now puts «Похоже, в номере опечатка. Нужен российский
  мобильный номер, например +7 900 000-00-00.» under the phone input, with no form-level text;
  editing the field clears it, as it does for the two names. One `400` can carry several field
  codes, so every matching field is filled from the same answer instead of the first one only
  (the form used to return after the first name code). The web counterpart still shows the generic
  line, because its form maps only the two name codes; this is a deliberate deviation from §8,
  recorded here rather than left silent.

- **A failed read retries itself while the screen is visible, and «Повторить» stays as «retry now».**
  On a phone a manual retry asks the person to notice the failure, guess when the network came back
  and tap again for every attempt — nobody does that. The «Записи» tab had already chosen the
  automatic side (a silent 30-second refresh, no «Обновить» button, see above), and the retry button
  was the last place where the product asked to be poked. The mechanism started as a pilot on three
  screens and is now on every read in the product: the search results and the search catalogs, the
  first page and the next page of «Записи», the first page and the next page of «Мои отзывы», the
  barber card's profile-with-slots and its reviews, the «Профиль» hub, «Личные данные»,
  «Безопасность», and the slots grid of the reschedule screen. Only reads. Writes — booking, cancel,
  reschedule submit, review submit, edit and delete, profile and email save, sign-in and registration,
  password change, the reminder switch — keep their «Повторить» untouched: after a failure their
  outcome is unknown, and a silent repeat could book twice or hide a refusal the person must see. City
  suggestions are the one read outside the rule on purpose: they are a limiter and an input aid, not
  an answer, and they already stay silent on every failure.
  The decision lives in Core. `ApiFailureClassifier` turns an exception into `ApiFailureKind`:
  `Transport` — no answer at all (DNS, TCP, TLS, a dropped connection, and the 30-second
  `HttpClient.Timeout`, which arrives as a `TaskCanceledException` and used to be swallowed by
  `catch (OperationCanceledException)` as a cancellation, so the person saw nothing);
  `RetryableServer` — the server answered 5xx, 408 or 429; `Terminal` — 400/401/403/404/409 and
  anything unknown, where a repeat only repeats the refusal; `None` — a real cancellation, which is
  not an error and shows no message. `AutoRetryLoop` waits 1, 2, 4, 8, 15 and 30 seconds, repeats the
  last interval, and stops on success, on a terminal answer, or when the screen leaves. The loop is
  armed by `StartAutoRetry` and disarmed by `StopAutoRetry` from the page — `PageVisibility` for every
  new page (appearing plus `Window.Stopped`/`Resumed`), the tab's own `_isVisible` plumbing for
  «Записи», which already stopped its timers in the background — so a request is never made from a
  hidden window, and an attempt that was already waiting is cancelled on the way out. The loop
  deliberately keeps the caller's context instead of `ConfigureAwait(false)`: an attempt is
  ViewModel code that updates bindings, and a continuation on a pool thread would repaint from a
  foreign thread.
  A screen with two independent reads arms two loops, one per read: «Записи» (first page and next
  page), the search (results and catalogs) and the barber card (profile-with-slots and reviews). The
  loops never arm each other — a broken catalog list is no reason to re-run the search, and a failed
  review page is no reason to re-read the profile. The reviews loop is owned by the card but lives on
  the section, which has no lifecycle and no binding context of its own: the card arms it, and the
  failure it reports back is the heaviest of the attempt, so a profile that recovered in the same
  attempt cannot stop the retry of reviews that did not.
  The button keeps its wording and its place: it stops the loop, loads at once and restarts the loop
  only if the new failure is retryable again — it cannot arm a page that has already left. While the
  loop runs, the error card keeps its title, its reason and its button and adds one muted line,
  «Пробуем снова…» (the only new catalog key), so the screen says the app is working instead of
  looking frozen. A silent attempt also has to keep what is already shown: «Записи» gets
  `FirstPageMode.Retry`, which does not clear the message until the answer — the old silent mode
  blanked the card into an empty screen for the length of every request — and neither the rows nor
  the reason are erased by a failed attempt. This is a deliberate deviation from §8, where the web
  only retries on a click, recorded here rather than left as an omission; the remaining «Повторить»
  buttons keep their behaviour, because they are the write paths.
  Two more rules keep a silent attempt honest. A retry repeats the request that failed rather than a
  fresh one built from the form or from the screen: the search remembers the filters of the failed
  attempt, coordinates included, because they are attached after the form is read — so a field edited
  while the error card is up still waits for «Найти»; a load-more retry keeps the cursor and the tab
  that failed, and an id already in the list is not appended a second time, so a repeat cannot
  duplicate or reorder rows; the reviews paginator keeps the page number and its cursor, and a
  successful retry stays on that page instead of skipping ahead; the reschedule slots keep the date
  that failed, not the one picked since; the catalogs keep which of the two requests failed, and a
  retry asks only for that one, because a partial success is not thrown away; the profile reads carry
  no parameters at all, so there is nothing to rebuild. And a silent attempt never repaints the
  failure as a loading screen: the reason, the rows and the button stay until the answer. That is why
  the barber card needed a silent mode of its own and «Записи» a `FirstPageMode.Retry`: a load that
  clears its error text and flips its spinner at the start of an attempt makes the whole block with
  «Повторить» vanish between attempts, and a cancelled attempt that never restores the text leaves
  the card blank with nothing to press.
  Two screens needed a rule of their own. The reviews paginator reuses the «Дальше» button and only
  its «Повторить» text on failure: `CanGoNext` is `HasPageError || NextCursor is not null`, so the
  same button means "next", "retry this page" and "next again" without gaining a second control. The
  «Профиль» hub stops its loop before every push into a child screen (`EditProfile`, `Reviews`,
  `Security`, `Register`, `ForgotPassword`), because Shell does not raise `OnDisappearing` for a push:
  without the stop the loop would keep polling the network under the pushed page. Catalog `429` is
  the one place where `RetryableServer` is not retried: the rate-limit text stays and the loop does
  not start, while every other read keeps `429` retryable with its own text.
  «Записи» needed one more rule, found on the phone rather than in a test: its list waits for
  `GET api/auth/me`, and a cold start without a network failed that read before the list was ever
  requested — the loop then had no user id to retry with, so the screen froze on «Повторить» while
  every other screen healed itself. A failed `/me` is therefore a read like any other: it starts the
  loop on its own (the `/me` kind is remembered separately from the list kind, so a stale list
  failure cannot arm it), and a retry asks for `/me` first and only then for the first page of the
  tab that is active when the answer arrives. A `401` from `/me` still ends the session, and a
  terminal one never arms. The lesson worth keeping: an automatic retry must own every read its
  screen needs to show data, not only the read that produces the rows.
  A `401` still ends the session, a terminal answer stops the loop instead of spinning on a refusal,
  and a real cancellation shows nothing. Tests add 126 cases (837 total): the classifier and the loop
  themselves, and per screen the retry-until-success, the terminal refusal that never starts the loop,
  the stop on leaving, the button that loads at once and restarts only on a new retryable failure, the
  failed silent attempt that keeps what is shown, the timeout that finally shows «Нет связи с
  сервером…», the retry that outlives a form edit or a tab switch, the two loops that never arm each
  other, the cold start whose `/me` read fails before the list is ever asked for, and the «Пробуем
  снова…» line that follows the loop state.

- **«Стать мастером» is a quiet row in the account hub, and only a customer sees it.** The row sits
  under the person card, before the block that reports a failed silent profile refresh — and it is a
  `Border` card-row (1 pt `Border`, radius 10, `MinimumHeightRequest` 56, the scissors in a `Muted`
  circle 36, the title and a chevron, the whole row tappable) rather than a filled `Primary` button:
  a filled button in that place would compete with «Выйти» and «Личные данные», which are the hub's
  real actions, while becoming a master is an application that grants nothing yet. The row is drawn
  only when the profile is loaded and the role is `UserRole.Customer` (`CanRequestBarberRole`). A
  barber and an administrator already hold the role, `POST api/barber-requests` would answer them
  `403`, and an interface that offers an action the server forbids teaches the person to expect a
  refusal.
  `BarberRequestTitle` is the request's state rather than a permanent action label: while there is
  no request it says «Стать мастером», then «Заявка на рассмотрении», «Заявка отклонена» or
  «Заявка одобрена». A row that kept saying «Стать мастером» would leave a person who already
  applied with no sign that the application exists, and this row is the only entry into the request
  page — the page carries the details, so the row has to carry the status. A rejected request keeps
  the row as the way back into the form, and an approved one ends in «Выйти и войти снова» instead
  of a silent reload: approval changes the role, the server rotates `SecurityStamp` and revokes every
  refresh token, so the pair the app holds is dead the moment the administrator decides, and only a
  new sign-in shows the new role.
  The state itself comes from `GET api/barber-requests/mine`, read quietly after the profile and only
  for a customer, on a second `AutoRetryLoop` of the hub's own — the same rule as «Записи»: the two
  loops never arm each other, and a failed `mine` is no reason to re-read the profile.
  `request: null` is the ordinary "no request yet", not an error and not a state to hide. A failure
  of this read neither hides the row nor blocks the hub: the title falls back to «Стать мастером»,
  the loop repeats while the tab is visible, and a tap still opens the page. The row is a hint, not
  the record — the page reads the same `mine` with its own loop and shows the actual request — and
  hiding it on a failed read would remove the only way into that page and into an application that
  already exists. Signing out clears the request state together with the profile, so the next
  account never inherits the previous title, and the push into «Стать мастером» stops the hub's
  loops first, like every other push into a child screen.
  The page is native — `BarberRequestPage` on the `barber-request` route, pushed over the «Профиль»
  stack with the tab bar hidden — not the web's `/become-a-barber` opened in a browser. The app
  carries a Bearer pair while the site's page lives on a cookie session: in a browser the person
  would arrive signed out and would apply against an account the app is not showing, whereas the
  native page posts `POST api/barber-requests` with the token `AuthenticatedHttpHandler` has already
  attached, and needs no `X-CSRF-TOKEN`, because the antiforgery filter lets a bearer request
  through. The word on the screen is «мастер», English “professional” (the `GLOSSARY.md` entry
  «Мастер / Professional» at the git root), while the code and the domain keep `Barber`:
  `BarberRequest`, `BarberRequestResponse` and `api/barber-requests` are the role's name, not an
  interface string, while «барбер», «парикмахер» and «колорист» are specialisations: the role covers
  more than one trade, and the screens of this slice say «мастер» and never «барбер». Three
  neighbours are deliberately outside this slice: renaming
  the web page `/become-a-barber`, the barber cabinet, and translating the existing catalog from
  «Мастер» to “professional”. That last one is the recorded deviation rather than an oversight: 33
  lines of `Core/Localization/messages/en.json` still say “barber” (the first is «Мастер» →
  “Barber”), because that wording is spread over search, the card, bookings and reviews, and a
  handful of new strings must not rewrite the product's vocabulary in passing. Every string this
  slice adds is already “professional”.

- **The admin section is a role-gated drill-down, not a fifth tab.** The entry is a row in the
  «Профиль» hub, visible only when the profile's `Role` is `Admin`, with a red badge that is the sum
  of the pending queues from `GET api/admin/summary`. Admin is a rare role, and a fifth tab would
  spend a fifth of every other person's tab bar on it; the account hub is also where the role is
  already named, so it is where the person looks for it. The badge read is a second, silent request:
  its failure is not a profile error and is never retried, because a hint is not data — the row
  simply loses its number. The hub behind it (`Pages/AdminHubPage`, `AdminHubViewModel`) merges the
  web's `admin-rail.tsx` and `summary-screen.tsx` into one list, because on the web those two render
  the same numbers twice and a phone has no room for a rail plus a page about the rail. A row is a
  section: title, the queue stated in words, and the count as a badge. Wording the web puts in a
  hover tooltip (what the number means) is permanent text here — there is no hover, and the count
  alone would say nothing. An empty queue keeps its row: the section is still worth opening to see
  what was already decided. The badge caps at «99+», the web's `formatAdminBadgeCount` threshold.
- **The hub refreshes on appearance and has no timer.** The lists on «Записи» tick every 30 seconds
  because visits change under the person; a moderation queue only changes when somebody decides, and
  the return from a section is exactly that moment. So `OnAppearing` re-reads and the pull-to-refresh
  gesture repeats it, with no poller in between. One snapshot feeds every row, which is also why the
  web's two-pollers-disagree class of bug cannot appear here: the numbers come from one response, not
  from one response per section.
- **A decision is taken at the bottom of the screen, and a refusal costs two taps.** The web settles
  everything in modal dialogs (`*-dialog.tsx`) opened from a toolbar; a phone has no toolbar reach
  and a modal hides the record it is asking about. So the record fills the screen, and the decision
  lives in a fixed bottom bar: the primary action on the right, the destructive one on the left in
  secondary styling — the destructive action is never the easy thumb target. «Отклонить» / «Скрыть» /
  «Снять подтверждение» opens an inline panel above the bar with its own confirm, and that confirm
  stays disabled until a reason satisfies the server's own rule *where the server stores one* (photo
  and request rejections). Hiding a review and removing a verification take no reason at all, so
  their panel shows the consequence in words instead of a field: a field whose text goes nowhere
  promises a reader who will never read it. Two taps for a destructive action is the point, not
  friction to remove. After a successful decision the page pops back to the queue, whose
  `OnAppearing` refresh drops the resolved row — the section never keeps its own "changed" event.
- **The phone ports the moderation queues, not the whole section.** Reviews, photo pre-moderation
  (avatars and portfolio), profiles awaiting verification and requests to become a professional are
  the four queues where an administrator's job is a decision on a record, and a decision fits a
  phone. The rest stays web-only and is a recorded omission, not an oversight: statistics (the
  section is charts and periods over history the API does not keep, read at a desk), the catalogue
  (hair services and specializations are data entry), salons (a form with address suggestions and
  working hours), and users, bookings and the audit log (reference data an administrator looks up
  while doing something else, with no decision of its own). The hub therefore lists four sections and
  no "coming soon" rows.
- **The admin screens share one set of failure texts.** `Core/Api/Admin/AdminErrorText` holds the
  wording for network, rate limit, `403`, `401` and the per-action fallback, while the client screens
  each keep their own copy: an administrator moves between four queues in one session, and four
  slightly different sentences for "no connection" would read as four different products.
- **One counter needs a colour of its own.** `Colors.xaml` gains `DestructiveSolidForeground`, white
  in both themes, for the digits on the solid badge. The web paints them with `text-white` on
  `bg-destructive-solid` (`admin-rail.tsx`), which is a utility rather than a token there; on the
  phone `PrimaryForeground` cannot stand in, because in the dark theme it is `#171717` and the badge
  keeps the same `#CE0000` in both themes. No other colour, font or radius enters the app with this
  slice.

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
reminder and the booking events (creation, confirmation, cancellation and reschedule), and routes a
tap on any of them to «Записи». A per-booking deep link is still impossible — there is no
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
  must have a branch of its own; if there is none, create one from the **freshly fetched
  `origin/main`** — `git fetch origin && git switch -c docs/… origin/main`, with `docs/…`,
  `features/…` or `fix/…` named after the change. A local `main` that was not fetched first is not a
  base: it can be behind `origin/main`, and the branch would then start from a stale commit. A
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
  placeholder. On Android 12+ the splash is the same lockup as an `AnimatedVectorDrawable`:
  `Platforms/Android/Resources/drawable/hairo_splash_lockup.xml` repeats both outlines from
  `splash.svg` at two-thirds of its 432 dp canvas, which the system draws at 288 dp, so the mark
  keeps the raster's 192 dp instead of jumping in size, and `hairo_splash_animated.xml` snips: the
  two rigid blades (`blade_upper` / `blade_lower`, split at the crossing) turn in opposite
  directions, opening 14° and closing again in a 700 ms loop for as long as the system splash is
  up, so the launch reads as a haircut rather than a rocking logo. The animated copy closes the
  4-unit lucide gap in the lower blade, because a half with a floating dash cannot be a rigid
  blade; the static splash and the in-app `ScissorsMark` keep the gap. The system splash plays an
  AVD but never a raster. `MainActivity` takes `Theme.Hairo.Splash` over `Maui.SplashTheme`; the
  `values-v31/styles.xml` override points the animated icon at that AVD and gives Android 12 the
  duration, while iOS and older Android keep the static `splash.svg`. The icon keeps the name alone
  — the adaptive mask has no room under the text. The name is the product's mark, not just a
  picture: `ApplicationTitle` and the Shell title are `Hairo`, and the sign-in heading reads
  «Вход в Hairo». Identifiers are not text and keep their names: the namespaces, the project files,
  `ru.barbersearch.mobile` and the URLs still say `BarberSearch`.
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

