# AGENT.md — BarberSearch (mobile)

> Context for agents and developers. Read together with the `README.md` next to it and with
> `backend/AGENT.md` (§7 — domain, §16 — API, §17 — authorization) and `frontend/AGENT.md`
> (§7 — design system, §8 — UI kit): the mobile UI is designed after the web app, see §8 below.

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
The user searches for a barber, opens the card and the free slots, books an appointment, and sees
their bookings.

The barber cabinet and the admin section live on the web (`frontend/`). They will not exist in the
mobile app: a schedule and working hours are not filled in from a phone.

## 2. Where things live

| What | Path |
|---|---|
| Solution | `mobile/BarberSearch.Mobile.sln` |
| App (MAUI) | `mobile/src/BarberSearch.Mobile` |
| Core without MAUI (HTTP client, ViewModels) | `mobile/src/BarberSearch.Mobile.Core` |
| Core tests | `mobile/tests/BarberSearch.Mobile.Core.Tests` |
| API contracts (project reference) | `backend/BarberSearch/src/BarberSearch.Contracts` |
| Git root | `D:\Projects\barbersearch` — the mobile app sits inside the repository, not next to it |

## 3. Stack

| Component | Version / value |
|---|---|
| TFM | `net10.0-android`, `net10.0-ios` (core and tests — `net10.0`) |
| SDK | `10.0.303` (`global.json`) |
| MAUI | workload from Visual Studio 18.8 (`Microsoft.Maui.Controls` 10.0.20) |
| MVVM | CommunityToolkit.Mvvm 8.4.0, `[ObservableProperty]` on fields |
| Android | `SupportedOSPlatformVersion` 24, `ApplicationId` `ru.barbersearch.mobile` |
| iOS | `SupportedOSPlatformVersion` 15.0 |

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

A local backend is required before running (`backend/docker compose up -d` +
`dotnet run --project BarberSearch/src/BarberSearch.Web`). The Android emulator reaches the host as
`10.0.2.2:5243`, the iOS simulator as `localhost:5243`.

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

## 6. What the backend still lacks

The mobile MVP is blocked by three backend changes (`backend/`) that do not exist yet:

1. **Geo search.** `Salon` has coordinates, but the public catalog does not expose them and search
   filters by city only. Needed: an endpoint of points within a radius/bbox for the map screen.
2. **Reading favourites.** `POST`/`DELETE /api/users/{id}/favorites/{barberProfileId}` exist, but
   there is no `GET` for the list.
3. **Push.** There is no infrastructure at all: no device/token table, no FCM/APNs, no client
   notifications. Reminders today are a Telegram bot for barbers and an email outbox.

Until then the "Map", "Favourites" and push screens have nothing to build on.

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

- **Tokens become resources, not literals.** `Resources/Styles/Colors.xaml` keeps the web names as
  semantic keys (`Background`, `Foreground`, `Card`, `Muted`, `MutedForeground`, `Primary`,
  `Border`, `Destructive`, `Success`, `Warning`) with both light and dark values through
  `AppThemeBinding`; `Styles.xaml` styles controls only through those keys.
- **Replace the template palette, do not extend it.** `Primary #512BD4`, `Secondary #DFD8F7`,
  `Tertiary` and `Gray*` are MAUI template leftovers; the web palette is neutral. `Danger #DC2626`
  is a placeholder for the token too.
- **Typeface is Geist Variable** with Cyrillic. MAUI cannot load WOFF2, so register a TTF/OTF
  (`ConfigureFonts`) — do not copy the font files out of `frontend/node_modules` as they are.
  Sizes follow the web scale: screen title 20–24, card title 16, body 14–16, meta 12.
- **Radius from the scale.** Base `--radius: 0.625rem` → 10: sm 6, md 8, lg 10, xl 14. No
  hand-picked `12` (`SearchPage` has one today).
- **Accessibility carries over.** AA contrast in both themes; the `muted-foreground` on `muted`
  case is a known threshold in the web (frontend §7.2, issues #33/#34) and the same values are
  reused here.

### 8.3 Rules

1. Nothing visual is invented locally. A colour, font, radius, spacing, icon or state that the web
   does not have is a bug, not a decision. If mobile genuinely needs something new, add it to the
   web design system first (three edits in `frontend/src/index.css`, §7.2) or record the deviation
   in §5.
2. A screen repeats the web structure and its three states — loading, content, error; the empty
   state keeps the web wording instead of a new sentence.
3. Wording is taken from the web, in Russian, and follows the frontend glossary term by term
   (booking, slot, no-show…). One concept — one word in both clients.
4. Port principles, not markup: no Tailwind classes, no shadcn/Base UI components, no `data-*`
   state attributes, no hover-only affordances. Touch instead: targets ≥ 44 pt, no hover, primary
   actions within thumb reach rather than pinned to the top edge.
5. The map screen has no web counterpart — the web renders no map. It keeps the same tokens, the
   same card anatomy and the wording of the web barber card; the layout itself is a mobile
   decision, recorded in §5.

