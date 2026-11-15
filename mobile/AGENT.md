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
| Typeface | Geist 1.9.2 — static TTF `Regular`/`Medium`/`SemiBold`, same as the web |
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
  and the status badge carries the state. The lists are not polled either, although the web polls
  «Предстоящие» once a minute: the tab refreshes on appearance (silently — the rows stay until the
  answer), on pull-to-refresh and after an action, and only the countdown ticks once a minute. This
  is a deliberate deviation from §8 "the web is the reference", recorded here rather than left as an
  omission.
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
- **The template palette is gone, not extended.** `Primary #512BD4`, `Secondary #DFD8F7`,
  `Tertiary`, `Gray*`, `Magenta`, `MidnightBlue`, `OffBlack`, `Danger #DC2626` and the `*Brush`
  duplicates were deleted. `Platforms/Android/Resources/values/colors.xml` carries the same
  `--primary` / `--background` values for the platform chrome, and the icon and splash colours in
  the `.csproj` use `#171717` for the same reason.
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
   (booking, slot, no-show…). One concept — one word in both clients.
4. Port principles, not markup: no Tailwind classes, no shadcn/Base UI components, no `data-*`
   state attributes, no hover-only affordances. Touch instead: targets ≥ 44 pt, no hover, primary
   actions within thumb reach rather than pinned to the top edge.
5. The map screen has no web counterpart — the web renders no map. It keeps the same tokens, the
   same card anatomy and the wording of the web barber card; the layout itself is a mobile
   decision, recorded in §5.

