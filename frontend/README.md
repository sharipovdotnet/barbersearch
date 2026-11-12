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
npm run check:i18n # catalog coverage: t() keys, JSON conflicts, unwrapped Russian UI
npm run preview  # serve dist/

npm test           # Vitest unit suite: no API, no browser (see «Testing»)
npm run test:watch # the same suite in watch mode

npm run test:e2e:install   # once: download Chromium for Playwright
npm run test:e2e           # Playwright + axe against a real API (see «E2E»)
npm run test:e2e:ui        # the same in the Playwright UI mode
```

## Localization

The interface is available in Russian (default) and English. The `RU`/`EN` switch in the header
stores the choice in `localStorage` (`barbersearch.locale`) and updates `<html lang>` and the meta
description. `src/i18n` owns the catalogs; **the key is the Russian source string**, so a missing
translation falls back to readable Russian instead of a raw key:

```tsx
const { t } = useI18n()
t('Мой кабинет')                  // → «Мой кабинет» / «My account»
t('Салон: {name}', { name })      // placeholders
tp('reviews', count)              // «1 отзыв» / «2 отзыва» / «1 review» / «2 reviews»
```

English translations live in `src/i18n/messages/en/*.json`, split by feature area. Dates, numbers,
prices and plurals are locale-aware in `src/lib/format.ts` (`ru-RU` / `en-GB`, 24-hour time, RUB).
Native date inputs get their hint from the browser locale, so `ui/input.tsx` overlays a localized
`DD.MM.YYYY` hint on an empty field; the browser's own calendar popup still follows the browser
language. `npm run check:i18n` fails when a `t()` key has no English entry, when two catalogs
translate the same source differently, or (`--strict`) when Russian UI text bypasses `t()`. See
`src/i18n/README.md` for the glossary and conventions.

## Routes

| Path | Screen |
|---|---|
| `/` | landing page: real barber search by city, date, language, specialization, service and additional-service substring, how it works, account CTA |
| `/barbers/{id}` | public barber profile: avatar in the hero, services, salons, weekly schedule, rating, the «Работы» portfolio gallery with a lightbox |
| `/account` | personal cabinet, «Мои записи»: «Предстоящие»/«История» as two server lists with the shared pager, hero, cancellation and the barber's cancellation reason, review |
| `/account/profile` | profile: avatar upload, change and delete, plus the «Email» card with the address, its verification status and the «Изменить email» dialog (new address + current password, confirmation link, notice to the old address), then first name, last name and phone |
| `/account/security` | security: password change, sign out, email verification (status, resend with a 60-second cooldown, `?verification=sent` landing) and the «Забыли пароль?» link |
| `/account/reviews` | author's own reviews: both statuses, edit and delete |
| `/barber` | barber cabinet: the «Сегодня» day feed by default (visits of the day, the free windows between them and a one-click booking into a window), then «Ближайшие» / «История» lists with confirm / complete / no-show / cancel with a required reason / note, and «Записать клиента» for a registered user of any role or a walk-in without an account |
| `/barber/onboarding` | creates the barber profile of a `Barber` who has none: every field of the create request, Russian validation, session refresh after `201` |
| `/barber/schedule` | barber schedule: weekly template, date exceptions (day off or custom hours) and time offs |
| `/barber/salons` | «Мои салоны»: linked places with their kind and moderation status (approved / pending / rejected + reason, «Не в каталоге» for an unlisted place), «Создать салон» for a barber with a profile (a listed place goes to admin review, an unlisted one is active at once), owner address/contacts edits, picker for an existing salon |
| `/barber/profile` | own profile: the card form (grade read-only), the price list with active and hidden services (add, change the price, hide, restore), «Фотографии» (upload, caption, order, hide/restore, delete, up to ten) and specializations; «hiding» a service keeps the row for the booking history |
| `/barber/import` | «Перенести записи»: two sources. A `.ics` calendar file — one file up to 2 MB, parsed in the browser, single and recurring events with `RRULE`/`RDATE`/`EXDATE`, `VTIMEZONE`, a 7/30/90-day window, a per-event preview with edits and selection up to 50, a deterministic idempotency key from the event `UID`, guests-only bookings (`customerId: null`), `409` → nearest free slots and `429` → a one-minute pause. A pasted list from Google Docs or a spreadsheet — up to 50 lines, tab/semicolon/pipe separated, line-by-line parse of date, time, name, phone, service and salon with per-row errors and inline fixes. Both: sequential guest bookings with progress, partial success and retry of failed rows |
| `/barber/notifications` | reminder inbox: the bell in the cabinet heading opens it; unread badge («1» in the fixtures), visit and morning-digest rows, reminder settings and the optional Telegram link |
| `/become-a-barber` | request for the barber role: public copy, the customer's form or status, links for a barber and an admin |
| `/register` | customer self-registration; a signed-in visitor sees the «Вы уже вошли» gate |
| `/verify-email` | confirmation from the email link: one anonymous `POST` with the token, then «Адрес подтверждён», «Ссылка недействительна», a network error or «Ссылка неполная»; the token is removed from the address after it settles |
| `/confirm-email-change` | email change confirmation from the letter: one anonymous `POST` with the token, then «Адрес изменён» (all sessions ended, no auto sign-in), «Ссылка недействительна», a network error or «Ссылка неполная»; the token is removed from the address after it settles |
| `/forgot-password` | request a password-reset link: one anonymous `POST`, the generic «Проверьте почту» card for any address, a local 60-second resend cooldown, «Указать другой адрес» and the global «почта не настроена» error |
| `/reset-password` | set a new password from the email link: the token is read once and removed from the address, then the form, «Пароль изменён» (all sessions ended, no auto sign-in) or «Ссылка недействительна» |
| `/privacy` | Политика обработки персональных данных: полный документ (152-ФЗ), содержание, перечень cookie, реквизиты Оператора |
| `/admin` | admin shell; redirects to `/admin/reviews` |
| `/admin/summary` | «Что требует внимания»: four queues — reviews on moderation with the oldest one, barbers without confirmation, disabled salons and photos (avatars/portfolio) waiting for approval — counters and links, no actions; a salon waiting for moderation is not counted as disabled |
| `/admin/statistics` | «Статистика сервиса» за период: presets 7/30/90/365 days, two dates, comparison with the previous period; six tiles, an SVG chart with a table duplicate, statuses, clients, supply, three leader tables and quality/moderation; read-only |
| `/admin/reviews` | review moderation queue (pending / published tabs) |
| `/admin/reviews/{id}` | one review; the tab is in `?tab=published` |
| `/admin/photos` | photo pre-moderation queue: tabs «Аватары» (`?tab=avatars`, default) and «Портфолио» (`?tab=portfolio`), only frames «На проверке» in arrival order, search by name/email, paged rows, full-size preview, «Одобрить» and «Отклонить» with a mandatory reason |
| `/admin/barbers` | barber queue: pending / all / hidden (filter in `?status=all\|hidden`, `pending` by default), search by name or email |
| `/admin/barbers/{id}` | one barber: contacts, readiness checklist, «Фотографии» with approve/reject and block/unblock, verify/grade/hide; the current filter stays in `?status=` |
| `/admin/users` | user list: status/role filters, search by name or email |
| `/admin/users/{id}` | one user: role, block/unblock, email/phone confirmation, avatar approve/reject and block/unblock |
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
`/admin/barbers`, `/admin/users`, `/admin/photos`, `/admin/bookings` and `/admin/audit` — pages
with the same footer.
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

The landing calls `GET /api/barbers?city=&specializationId=&hairServiceId=&date=&language=&additionalService=&cursor=&pageSize=`
(anonymous, 120 requests per minute per IP). The filters are a **city** (trimmed and compared
exactly, case-sensitive), an optional **date** — the barber has a working day on that weekday —
one **language** (an enum name such as `Ru`), one **specialization**, one **hair service** and one
**additional service** — a free-text substring matched case-insensitively against any element of
the barber's `additionalServices` list (`%` and `_` are literals); empty values are not sent at
all. Results are ordered `rating DESC NULLS LAST, id`; `pageSize` is 12 and the next page is
fetched with the opaque `nextCursor` from the previous response. There is no name/email text
search, and the card shows no price tags because the search DTO has none; it does show the
additional-service badges.

The language select always lists the six known languages. The specialization and service selects
come from `GET /api/specializations` and `GET /api/hair-services` (anonymous, active rows only,
bare arrays, no pagination): an empty catalogue hides its select instead of showing a dead list.
While they load the selects are disabled with «Загрузка…»; if the load fails they stay disabled
and the form shows a Russian `role="alert"` with «Повторить» — city, date, language and «Найти»
keep working. The additional-service field is an input with a `<datalist>` fed by
`GET /api/barbers/additional-services` (distinct values of active and verified barbers, up to 100,
ordered by value); that request is best-effort — if it fails the field simply has no suggestions
and still filters by whatever the visitor types. Only «Найти» sends a request; filters live in
React state, not in the URL.

- `src/lib/barber-search.ts` — filters, `emptyBarberSearchFilters`/`normalizeBarberSearchFilters`, `BarberSearchItem`, `searchBarbers()`.
- `src/hooks/use-barber-search.ts` — pages, abort of stale requests, retry, load-more state.
- `src/hooks/use-search-catalogs.ts` — parallel catalogue read, abort on unmount, retry.
- `src/lib/specializations.ts`, `src/lib/hair-services.ts` — catalogue types and requests.
- `src/lib/additional-services.ts` — suggestions for the additional-service input.
- `src/components/home/` — hero/search form, results grid, cards and the account CTA.

## Barber profile

`/barbers/{id}` calls `GET /api/barbers/{id}` (anonymous, 120 requests per minute per IP; `404`
for a missing, hidden or unconfirmed profile, `400` for an empty GUID). The page shows the name,
Russian grade, «Проверен» badge, rating or «Пока нет отзывов», experience, bio, languages,
certificates, achievements, comfort and communication lists, additional services, services with
price and duration, specializations, salons with addresses and the weekly schedule, payment
methods and the late policy. Empty blocks are hidden instead of rendering an empty heading.

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

## Portfolio photos

A photo can now be a real uploaded image, not only a link. The public card shows it in the «Работы»
section, the cabinet manages it in «Фотографии», and an administrator can take a frame off the
public card without deleting it. Since #89 a new photo is **pre-moderated**: the upload creates it
`Pending`, it is not public and appears in the `/admin/photos?tab=portfolio` queue until an
administrator approves it.

**Public gallery.** `/barbers/{id}` renders `photos` between the hero and the slots grid. An empty
array means no section at all. The grid is two columns at 320 px, three from `sm` and four from
`lg`; every cell is `aspect-square` + `object-cover`, so the page does not shift while images
load. The first photo is `loading="eager" fetchPriority="high"`, the rest are `lazy` with
`decoding="async"`. A click opens a `Dialog` lightbox with the full image (`object-contain`,
`max-h-[75vh]`), the caption, a «2 из 5» counter, «Назад»/«Дальше» (disabled at the edges),
ArrowLeft/ArrowRight and Escape; Base UI returns focus to the photo button on close. A failed
image becomes a muted placeholder with the same `alt` instead of disappearing. The section is
covered by axe at 1280 and 320.

**Cabinet.** `/barber/profile` has «Фотографии» between «Карточка» and «Услуги». «Добавить фото»
opens a dialog with a file input, a local preview and a caption (up to 2000 characters with a
counter); at ten photos the button is disabled with an honest limit text. Each card has «Выше» /
«Ниже» (one bulk `PUT .../photos/order` with the full id array, never two swaps), «Изменить
подпись», «Скрыть» / «Вернуть» and «Удалить» behind an `alertdialog`. A new frame shows the
«На проверке» badge and the explanation «Кадр виден только вам, пока администратор его не
одобрит.»; a rejected one shows «Отклонено» and `Причина: …` from the administrator. A photo
blocked by an administrator shows the `destructive` badge «Скрыто администратором» and only
«Удалить» — the owner cannot unblock it. A hidden-by-owner photo shows «Скрыто» and can be
returned. Every
success is announced, calls `invalidateBarberProfile(id)` so «Открыть, как видят клиенты» does
not read the stale public cache, and quietly re-reads the card; an error stays in a `role="alert"`
line above the grid and never hides the loaded photos.

**Upload and client preparation.** The browser accepts JPEG, PNG and WebP up to 15 MiB and checks
that before decoding. `src/lib/image-prep.ts` draws the image on a white canvas (transparency does
not turn black), scales the long side down to 1600 px (never up), encodes JPEG at quality 0.85 and
retries at 0.7 if the result is still over 5 MiB; a larger result is the `tooLarge` error. This
also removes EXIF, including GPS, for browser clients. The upload is one `multipart/form-data`
request with `file` and `description` (`POST api/barber-profiles/{id}/photos/upload`); `api.ts`
does not add a JSON `Content-Type` for `FormData`, and a stale CSRF token is retried with the same
`FormData`. The server re-checks the signature (JPEG/PNG/WebP, never SVG/GIF), the size (1 KiB..5
MiB), the sides (300..6000 px) and the area (at most 24 MP); the request itself is capped at 6 MiB,
so Kestrel may answer `413` before the action. The result is `201 { id }`; the photo is created
`Pending` and is not public until approved, and the 11th photo is `409 barberProfile.addPhoto.limit`.

**Storage and URL.** Files live on the API disk behind `IPhotoStorage`, under `Storage:RootPath`
(default `App_Data/photos`, relative to the API content root; an absolute path is used as is). The
layout is `{root}/portfolio/{barberProfileId:N}/{mediaId:N}.{jpg|png|webp}`; `mediaId` is a new
UUIDv7 and the extension comes from the signature, not from the client file name. The API returns
a **relative** URL `/api/media/portfolio/{barberProfileId:D}/{mediaId:N}.{ext}`, which works in
`<img src>` because the SPA and `/api` share an origin. Old photos added through the JSON endpoint
keep their absolute external URLs.

**Visibility and blocking.**

| State | Public card | `GET /api/media/portfolio/...` |
|---|---|---|
| `moderationStatus: 'Approved'`, `isPublished: true`, `isBlocked: false` | shown | `200`, `Cache-Control: public, max-age=0, must-revalidate`, strong `ETag` |
| `moderationStatus: 'Pending'` or `'Rejected'` | absent | `404` for strangers, `200` for the owner/admin with `private, no-store` |
| `isPublished: false` («Скрыть») | absent | `404` for strangers, `200` for the owner/admin with `private, no-store` |
| `isBlocked: true` («Скрыто администратором») | absent | `404` for strangers, `200` for the owner/admin with `private, no-store` |

The media route reads visibility from the database on every request; a hidden, blocked or
not-approved URL is `404` even if it is already known, and a served file always carries
`X-Content-Type-Options: nosniff`.
Only an admin can approve/reject a pending frame (`POST .../photos/{photoId}/approval|rejection`,
rejection with a mandatory 1..500 reason) and block or unblock a public one
(`POST`/`DELETE api/barber-profiles/{id}/photos/{photoId}/block`); the owner can hide and return a
photo with `POST`/`DELETE .../publication`, and those calls never change `isBlocked` or the
moderation status. `GET api/barber-profiles/{id}` (the own card) returns all photos with both
flags, the real `moderationStatus` and `rejectionReason`; the public card only
`Approved && isPublished && !isBlocked` (its response always says `Approved`/`null`), and
`AdminBarberDetailResponse.photos` all of them while `readiness.photosCount` counts visible and
approved rows only.

**Variants and delivery.** A photo is delivered in the size it is shown. `thumbnailUrl` is now
computed: `{url}?size=md` for own media URLs, the stored legacy value if present, and `null` for
external legacy URLs. `GET /api/media/portfolio/...` accepts `?size=sm|md|lg` and answers with a
WebP variant (256/512/1024 px wide, quality 80); when a variant is impossible (the source is
already narrower than the target, the inspector refused, the decoder failed) the original is
served with `200`, its own `Content-Type` and its own `ETag`. Variants are written next to the
original as `{mediaId:N}-{sm|md|lg}.webp` at upload (best effort — a generation failure only logs
a warning and the upload still succeeds) and lazily on the first `?size=` read, so frames uploaded
before this slice start serving variants after the first request; deleting the photo removes the
original and every `{mediaId:N}-*.webp`. The gallery, the cabinet previews, the admin photo queue
rows and the admin barber card preview request variants through `srcset`/`sizes` from
`src/lib/media.ts`; the lightbox and the admin
big preview keep the original. A visible file is `public, max-age=0, must-revalidate` with a strong
`ETag`; `If-None-Match` answers `304` without a body (never for a request with `Range`); a
non-public preview is `private, no-store`, and every refusal is `404` with `no-store`. There is no
`Vary` header. There is still no AVIF and no crop (`AGENT.md` §14 #31).

- `src/lib/media.ts` — `portfolioPhotoSource`/`avatarImageSource`, `MediaSource` and
  `PORTFOLIO_WIDTHS`/`AVATAR_WIDTHS`; the widths are part of the contract with backend
  `PhotoVariantWidths`.
- `src/lib/portfolio-photos.ts` — DTO, the eight mutations, the `FormData` upload and
  `describePortfolioPhotoError`.
- `src/lib/image-prep.ts` — `preparePortfolioImage` and its `ImagePrepError` codes.
- `src/lib/catalog.ts` — `invalidateBarberProfile`, called after every photo mutation.
- `src/components/barber/barber-portfolio.tsx` — the public «Работы» gallery and lightbox.
- `src/components/barber/barber-profile-photos.tsx` — the cabinet section and its three dialogs.
- `src/components/admin/barber-detail.tsx`, `barbers-screen.tsx` — the admin «Фотографии» block and
  its `block`/`unblock` calls; `src/hooks/use-admin-barber.ts` quietly re-reads the open card.

**E2E.** The portfolio scenario uses `e2e/fixtures/portfolio-photo.png`, a real **800×600** PNG of
about 101 KB (not an SVG and not a 1×1 pixel), and
`e2e/specs/barber-profile-photos-mutations.spec.ts` in the `mutations-chromium` project, last: six
serial scenes on the isolated `profileBarber`. The barber uploads it from `/barber/profile`, sees
its own preview with the «На проверке» badge and the explanation that only they can see it, while
the public card has no «Работы» section and `GET /api/barbers/{id}` returns no photos. The admin
opens `/admin/photos?tab=portfolio`, searches by the barber's name, previews the frame, approves
it, and the public gallery + contract return exactly one `Approved` photo (axe scans that queue row
with a caption at 1280). The cabinet preview requests the `?size=sm` variant: the owner's
`Pending` frame answers `image/webp` with an `ETag`, and a conditional repeat is `304`. In the
public gallery the grid image carries a `srcset`; blocking the approved frame makes the same
`?size=` URL answer `404` with `no-store` and restoring it answers `200` with
`public, max-age=0, must-revalidate`. The next scene opens the public card: the «Работы» gallery, a click opens
the lightbox with «1 из 1» and both arrows disabled at the edges (the scenario has one photo —
multi-photo paging is covered by the `barber-portfolio.test.tsx` unit test), Escape closes it and
focus returns; axe scans the gallery and the open lightbox at 1280, then a 320 reload checks
`scrollWidth <= 320`, scans the gallery and opens a lightbox no wider than the screen. A second
frame is uploaded, rejected in the queue with a reason, and the owner sees «Отклонено» and
«Причина: …» while the public gallery still has one photo. An admin then blocks the approved photo
(badge «Скрыто администратором», `role="status"`), the public card loses the section and the API
returns no photos, «Разблокировать» restores both, and the barber finally deletes both frames —
«Фотографий нет.» and no «Работы» publicly. The queue page object is `e2e/pages/admin-photos-page.ts`
(tabs, search, rows, preview, both decisions and the reason dialog); it is used by the avatar and
portfolio specs and by both axe specs. `e2e/fixtures/seed.sql` deletes the `portfolio_photos` rows
of `profileBarber` (`9e100000-0000-4000-8000-000000000015`) before its fixture upsert, so a rerun is
idempotent; a failed scene is cleaned through the API in `afterEach`, with a best-effort `afterAll`
as the last resort. File uploads are written to `Storage:RootPath`; CI passes
`Storage__RootPath: ${{ runner.temp }}/photos` to the API step, and a local run may leave orphan
files in `App_Data/photos` — they do not affect the database or the tests, but the directory
should be emptied when it grows.

## Avatar

A signed-in user can upload one profile photo. It is shown in the header account menu, in the
cabinet shell (the identity block), in the avatar card at `/account/profile`, on every barber card
in search results, in the client cabinet booking lists (the row and «Ближайшая запись»), in the
barber cabinet customer rows and in the admin booking list and detail — for both the customer and
the barber. For a barber the photo also appears in the hero of the public card at `/barbers/{id}`.
Every image is decorative (`alt=""`) and the initials remain the fallback.

**One profile cache.** `ProfileProvider` (`src/components/profile-provider.tsx`) is mounted once in
`main.tsx` inside `SessionProvider`; `useMyProfile()` reads its context, and the old module cache
keyed by user id is gone. The header and the cabinet share one state, so the cost is exactly one
`GET /api/users/me` per authenticated page load and zero for an anonymous visitor, with no second
cache. `setAvatar` from the avatar card updates every subscriber — the header
switches to the new photo (or back to the initials) immediately after an upload or delete, without
a reload. A `401` only sets `requiresSignIn`: the session stays owned by `SessionProvider`; other
profile errors leave the header on the initials.

**Cabinet card.** `/account/profile` starts with «Аватар» above «Личные данные». The card shows a
96 px preview: `AvatarImage` when `profile.avatarUrl` is set, otherwise the user's initials. The
hidden file input accepts `image/jpeg,image/png,image/webp`; the visible action is «Загрузить
фото» without an avatar and «Изменить фото» with one, and «Удалить» appears only when there is
something to delete. The hint says «JPEG, PNG или WebP до 5 МБ. Квадрат 512×512 подготовит
браузер.», the client checks the type and the 15 MiB source limit *before* decoding, the spinner
and `disabled` last only while a request is in flight, success is announced in `role="status"`
(«Аватар загружен и ждёт проверки.» / «Аватар удалён.») and a failure in `role="alert"` with
Russian copy from `describeAvatarError` (the English `detail` is never shown). A `Pending` frame
adds the «На проверке» badge and «Кадр виден только вам, пока администратор его не одобрит.»;
a `Rejected` one shows «Отклонено» and the administrator's reason, and the preview stays in the
card so the owner can see what was refused. Deletion is an `alertdialog`:
«Аватар исчезнет из карточки и кабинета. Файл удаляется безвозвратно.», and after it closes focus
returns to the upload button, because «Удалить» no longer exists. At 320 px the buttons are 44 px
and the card does not produce horizontal scroll.

**Pre-moderation.** Every upload creates a `Pending` frame: it is not public, and the cabinet
shows the owner-only preview with the badge. The decision is made in `/admin/photos?tab=avatars`
(the main path) or in the admin user/barber card:
`POST /api/admin/users/{userId}/avatar/approval` makes the frame public, and
`POST /api/admin/users/{userId}/avatar/rejection` with a mandatory `{ reason }` (1..500) keeps it
private and shows the reason to the owner. Only a `Pending` frame can be decided — a repeated
decision is `409 user.avatarModeration.invalidStatus`; `409 user.avatarModeration.noAvatar` means
the user has no photo. `GET /api/users/me` returns `avatarModerationStatus` and
`avatarRejectionReason` next to the raw URL; public `avatarUrl` is `null` unless the status is
`Approved` and the frame is not blocked. A new upload **replaces** the old frame immediately — it
is not kept public while the new one waits — so the public header, search and cards show initials
until approval. Both decisions are written to the journal as `user.updated` with
`avatarModerationStatus`; the rejection reason stays on the user row and is not in the payload
(see `AGENT.md` §14 #32).

**Blocking.** An administrator can hide or restore an approved photo from `/admin/users/{id}` or
`/admin/barbers/{id}` — `POST`/`DELETE /api/admin/users/{userId}/avatar/block`, both answering `204`
and idempotent. Hiding does not touch access: the file and `users.avatar_url` stay, so restoring
returns the same frame, and the account, role and sessions are unchanged. Public reads hide it:
`GET /api/barbers`, `GET /api/barbers/{id}` and `GET /api/barber/bookings` return `avatarUrl: null`
(a barber still appears in search), and `GET /api/media/avatars/...` answers `404` to anonymous
visitors and to the owner; an administrator gets the file with `Cache-Control: private, no-store`.
Each decision is one `user.updated` row in `/admin/audit` with «Фото скрыто администратором:
Нет → Да» and the admin as the author; a repeated call writes nothing. `409
user.blockAvatar.noAvatar` means the user has no photo to hide, `404 user.notFound` means the user
is gone.

**What the owner sees.** The header and the cabinet identity block render the photo only while it
is `Approved` or `Pending` and not blocked; for `Rejected` or a blocked frame they render the
initials. The raw URL is still in the context, but it is not put into an `<img>` when it must not
be shown, so the browser cache cannot bring an old frame back: a visible file revalidates with
`ETag` and a hidden one answers `404` with `no-store`. `AvatarCard` is the one
exception: it keeps the rejected preview with «Отклонено» and the reason, because the owner must
understand what failed. A blocked photo shows «Фото скрыто администратором» and the explanation;
the upload button becomes «Загрузить другое фото» and «Удалить» stays available (it clears the
URL and the flag). A successful upload or delete calls `setAvatar(url, status)`, which clears the
old block flag and reason locally in every subscriber.

**What the admin sees.** The user and barber cards keep the **raw** URL and add a textual
moderation badge — «На проверке» with «Одобрить»/«Отклонить» (the rejection `alertdialog` asks
for a mandatory reason), «Отклонено» with the reason and no decision buttons, or «Опубликовано» —
plus a «Фото скрыто» badge and «Скрыть фото»/«Вернуть фото» for the blocking state. Hiding asks
for confirmation in an `alertdialog` («Фото исчезнет из шапки, поиска, карточек и списков
броней. Аккаунт останется активным, вернуть фото можно здесь же.»), restoring needs no dialog.
Without a URL the card says «У пользователя нет фото — скрывать нечего.» and offers no action.
The rows of `/admin/users` and `/admin/barbers` show the 24 px photo with the same textual
badges, so a pending or hidden photo is visible to the admin who must decide or restore it.

**Why `users/me` returns the raw URL.** `PUT /api/users/{id}/profile` replaces the whole profile and
the cabinet sends the current `avatarUrl` back; if `GET /api/users/me` returned `null` for a hidden
or rejected photo, saving a name would erase the file (#85 regression). So `users/me` keeps the raw
URL and adds `isAvatarBlocked`, `avatarModerationStatus` and `avatarRejectionReason`, and every
renderer checks them. A real upload or delete changes the URL and clears the old status/reason; a
name save sends the same URL, so it changes nothing. The admin reads keep the raw URL and the same
three fields for the same reason: an admin must see what is pending or hidden to decide. Public
reads are the only masked surface.

**Client preparation.** `src/lib/image-prep.ts` gained `prepareAvatarImage` next to
`preparePortfolioImage`; both share one private encoder (JPEG quality 0.85, retry at 0.7 if the
result is over 5 MiB) and the same pre-decode type/size gates. The avatar is a square centre crop
(side = `min(width, height)`, centred offsets) scaled to exactly 512×512 — up-scaling included, so
the server's 300 px minimum always holds — on a white background, named `avatar.jpg`.

**Transport and cache.** `src/lib/avatars.ts` sends `POST /api/users/me/avatar` as
`multipart/form-data` with the single field `file` through `mutateWithCsrf` (fresh `X-CSRF-TOKEN`,
one retry with the same `FormData` on `auth.csrf.invalid`), validates the
`201 { avatarUrl, moderationStatus }` with `isAvatarUploadResponse` (a relative
`/api/media/avatars/...` URL and a known status), and deletes through
`DELETE /api/users/me/avatar` (`204`, idempotent). The upload always answers `Pending`, and the
provider's `setAvatar(url, status)` updates the shared
context without a second `GET /api/users/me`, so the header and every other consumer change at
once; a later profile save still sends
`avatarUrl` back unchanged, and after a successful upload or delete the card calls
`invalidateBarberProfile(profile.barberProfileId)` so «Открыть, как видят клиенты» never shows the
old public cache.

**Public card.** `BarberProfile.avatarUrl` is part of the public response and is `null` unless the
frame is `Approved` and not blocked; `BarberHero` renders `AvatarImage` when it is set and keeps
the initials as the fallback. `AccountIdentity` does the same for the cabinet shell. The admin
user and barber cards render the raw `avatarUrl` (pending, hidden or not) with the moderation badge
and the `AvatarModeration` block owns approve/reject and hide/restore. A hidden or pending photo is
masked on the public card and in search, but never in the admin card.

**Storage and URL.** The file lives on the same disk and under the same `Storage:RootPath` as the
portfolio, at `{root}/avatars/{userId:N}/{mediaId:N}.{ext}`. `GET /api/media/avatars/{userId}/{fileName}`
is anonymous: it serves the file only when the URL stored in the database equals the requested one.
A public frame (`Approved`, not blocked) keeps `Cache-Control: public, max-age=0, must-revalidate`,
a strong `ETag` and `X-Content-Type-Options: nosniff`; `?size=sm|md|lg` returns a WebP variant
(96/192/384 px wide, quality 80) or the original when a variant is impossible; `If-None-Match`
answers `304` without a body (never for a request with `Range`), and there is no `Vary`. Variants
live next to the original as `{mediaId:N}-{sm|md|lg}.webp`, are generated at upload and lazily on
the first `?size=` read, and are removed together with the avatar; every `AvatarImage` requests
them through `avatarImageSource` (`1x/2x/3x`). A `Pending`/`Rejected` frame is served only to its
**owner**
(cabinet preview) with `private, no-store`; a blocked frame is served only to an administrator with
the same `private, no-store`; everyone else — including the owner for a blocked frame — gets `404`
(never `403`/`410`, so existence is not revealed). There is no `avatar=blocked` filter in the admin
lists. Hiding and restoring **are** written to `audit_log` as `user.updated` with `isAvatarBlocked`;
since #91 the upload, replacement and deletion of the frame are written as `user.updated` with
`avatarUrl`/`avatarModerationStatus`, and the two pre-moderation decisions as `user.updated` with
`avatarModerationStatus` — the rejection reason lives on the user row and is not in the payload.
Those limits, with the single-disk backup, are debt `AGENT.md` §14 #32.

**Policy.** `/privacy` §3 now names the profile photo (the user uploads it; for a barber it is
published on the public card) and §4 sets its retention to «до удаления пользователем или
блокировки аккаунта». The policy edition and `PRIVACY_POLICY_VERSION` are `2026-11-11`, in sync
with the backend's `PrivacyPolicy.Version`; older consent records keep the edition they signed.

**E2E.** Two serial specs in the `mutations-chromium` project, both using the real
`e2e/fixtures/portfolio-photo.png` (a genuine 800×600 PNG) and the real API:

- `e2e/specs/account-avatar-mutations.spec.ts` (`mutations` account): upload from
  `/account/profile` — the card shows the owner-only preview, «На проверке» and «Аватар загружен
  и ждёт проверки.», `naturalWidth > 0` and `src` starting with `/api/media/avatars/`, the same
  `src` in `AccountIdentity` **and in the header account menu** (the initials `data-initials`
  before it, no image), a reload that keeps both, and `GET /api/users/me` returning the raw URL
  with `avatarModerationStatus: "Pending"`; an admin context sees the same `src` and the «На
  проверке» badge in `/admin/users/{id}`. The admin then opens `/admin/photos?tab=avatars`,
  searches by the customer email, previews the frame, approves it (the row disappears, «Фото
  одобрено.»), and the owner's `users/me` answers `Approved`; the media file is now `200` to a
  **real guest** context with `Cache-Control: public, max-age=0, must-revalidate` and `ETag` (the empty `storageState`
  fix); the header avatar exposes a `srcset`, and its `?size=sm` URL returns `image/webp`. The admin hides the photo (the `alertdialog` is scanned by axe at 1280) — the cabinet and
  the header show the initials, `users/me` keeps the raw `avatarUrl` with `isAvatarBlocked: true`,
  anonymous and owner media requests answer `404`, the admin's answers `200` with `private,
  no-store`, and the 320 px list row keeps its badge without horizontal overflow; restoring brings
  the frame back. A second scene rejects a **new** upload with a reason in the queue: the owner
  sees «Отклонено», the reason and still their own preview (the header shows initials), the
  anonymous media request is `404`, the owner's is `200`, and `users/me` returns `Rejected` +
  `avatarRejectionReason`; axe at 1280 and 320 (zero horizontal scroll, 44 px buttons), then
  deletion through the `alertdialog` — the header and the identity block return to the initials
  without a reload and the old media URL answers `404`.
- `e2e/specs/public-barber-avatar-mutations.spec.ts` (`profileBarber`, the isolated profile):
  upload in the cabinet gives the pending preview, while `GET /api/barbers/{id}` and
  `GET /api/barbers?…` return `avatarUrl: null` and the hero/search card show the initials; the
  admin approves the frame in `/admin/photos?tab=avatars`, after which the hero shows a real image,
  a search by the `E2E поиск` marker (seeded for `…0015`) gives exactly one card with the same
  `src`, `alt=""` and `naturalWidth > 0`, and the contract returns the same URL. The admin then
  hides the owner's photo from `/admin/barbers/{id}`: the search card and the hero show the
  initials, both contracts return `avatarUrl: null`, and a 320 px row with the badge does not
  overflow; restoring brings both the image and the contract back. After deletion the card and the
  hero show the initials again and the contract returns `avatarUrl: null`. The showcase barber
  `…0001` is never touched.

Successful scenes pass the uploaded URL to the next one; `afterEach` on a failure removes the
avatar through `DELETE /api/users/me/avatar`, and — since the moderation slice — also lifts
`is_avatar_blocked` through an admin context, because the `mutations` client cannot call the admin
route. `e2e/fixtures/seed.sql` resets `avatar_url`, `is_avatar_blocked`,
`avatar_moderation_status`, `avatar_rejection_reason`, `avatar_submitted_at`, `avatar_reviewed_at`
and `avatar_reviewed_by_user_id` for every fixture account, so a reseed starts from the initials
and an empty avatar queue.

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
`X-CSRF-TOKEN`; `200 { isPublished, updatedAt }` updates the row locally without a second
read. A rating-only edit of a published review applies immediately; changing its **text**
returns the review to moderation, so the row flips to «На модерации» and the review leaves
the barber card until it is approved again — the dialog says which one will happen.
Deleting asks for confirmation in an `alertdialog` and
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
(`components/account/account-shell.tsx`): a sticky rail with the identity block (avatar or
initials, name, email, role, unverified-email note), four section links and — for a `Barber` or
`Admin` — a link to `/barber` or `/admin`. Below `lg` the rail becomes a 2×2 grid of labelled
buttons, so all four sections stay visible at 320 px. The whole cabinet is open to every role; the
old `RoleGate` that turned barbers and admins away is gone.

The shell reads the profile from `ProfileProvider`, which loads `GET /api/users/me` once per
authenticated page load, and provides it to the section screens through context, so the profile
form does not issue its own read. The header shows «Мой кабинет» for everyone, «Кабинет барбера»
for a barber and «Администрирование» for an admin; the avatar link points to `/account`.

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
`PUT /api/users/{id}/profile` with `X-CSRF-TOKEN`; `avatarUrl` is still returned unchanged because
the endpoint replaces the whole profile. The avatar itself is managed by its card above the form
(see «Avatar»).

Email is managed by its own card above the form too: the current address, its verification status
and «Изменить email», which opens a dialog with the new address and the current password. The
password is required — a stolen session must not move the account to another mailbox. A client-side
check mirrors the login dialog (both fields non-empty, a sane address shape); the server stays the
only authority. `POST /api/auth/email-change` with `X-CSRF-TOKEN` answers `204` once **both** letters
are queued in the outbox: the confirmation link to the new address and a notice without any link to
the current one. The button then counts down a local 60 seconds; the public codes map to the fields
(`user.changeEmail.newEmail`, `user.changeEmail.sameEmail`, `auth.emailChange.invalidCurrentPassword`)
or to the form (`user.changeEmail.emailTaken`, `auth.emailChange.cooldown`,
`auth.emailChange.unavailable`, `429`), and success is announced in a `role="status"` region as
«Письмо отправлено на …». Nothing changes until the link is opened:
`/confirm-email-change?token=…` reads the token from the query once, sends exactly one anonymous
`POST`, strips the token from the address with `replace`, and on success ends **every** session —
the page signs the visitor out if it was signed in and offers the login dialog, so the next sign-in
uses the new address; a repeated click on the same link is still a success (`204`). The old address
stops working for sign-in, the password stays the same, and the pending address is not displayed
anywhere: `GET /api/users/me` has no `pendingEmail`, so there is no cancel or edit of a request
either (a known debt, see `AGENT.md` §14).
The form enables «Сохранить» only when a field changed, trims values, maps
`user.updateProfile.firstName|lastName` to the field and everything else to the form-level Russian
error. `GET /api/users/me` supplies the initial values; the same response feeds the rail, so the
name updates immediately after a save without a second read.

### Security

`/account/security` reuses the existing `ChangePasswordDialog` (password from 12 characters,
`POST /api/auth/change-password`, all refresh tokens revoked) and adds sign-out. The copy explains
that changing the password ends the other sessions.

Email verification is a working card, not a status line. A verified address shows a green
«Адрес подтверждён.» and the address itself. An unverified one shows a yellow
«Адрес не подтверждён.» and «Отправить письмо ещё раз»: the request goes through
`mutateWithCsrf` with an empty body, the button then counts down a local 60 seconds, and success
or failure is announced in an `aria-live` region. `409 auth.emailVerification.cooldown` and the
`429` auth limiter share the same «Письмо уже отправлено…» text;
`409 auth.emailVerification.unavailable` says «Отправка писем пока не настроена.» instead of
pretending the letter was sent. While `GET /api/users/me` loads, the button is not rendered;
after `204` the profile is re-read through `AccountProfileContext.reload()` rather than guessing
the flag. To verify the address, open the letter and follow the link: it opens
`/verify-email?token=…`, where one anonymous `POST` confirms the address, the token is removed
from the address bar, and a repeated click is still a success (the API answers `204`). The same
card is the landing after registration: the form navigates to
`/account/security?verification=sent`, the page reads the query **once** into state, shows
«Письмо отправлено на …» in an `aria-live` notice and immediately removes `?verification=sent`
with `replace`. The letter itself is sent by the server during registration, so a failed SMTP
delivery does not fail registration: the notice still says the letter was sent, while the resend
button honestly reports an unconfigured email channel. The «Email не подтверждён» line in the
cabinet rail is a link to this section. Changing the address itself is a separate flow on
`/account/profile` (see «Profile»): this page only verifies the current one.

Forgotten passwords are restored from the same page's «Забыли пароль?» link (the login dialog has
one too). `/forgot-password` sends one anonymous `POST /api/auth/password-reset` with the email
and always shows the same «Проверьте почту» card — the API answers `204` for an existing, an
unknown, an inactive and a malformed address alike, so the screen cannot and must not reveal
whether an account exists. The resend button counts down a local 60 seconds; the server's
cooldown is silent for the same reason. Honest errors are only the global ones: `409
auth.passwordReset.unavailable` when the server has no mail channel, `429` from the auth limiter
and a network/`5xx` failure. The letter links to `/reset-password?token=…`; the link lives **30
minutes** and works **once**. The page reads the token from the query once and immediately
removes it with `replace`, so it does not stay in history, `Referer` or the address bar (a reload
therefore loses it — open the letter again); there is no preflight check, the form appears for
any non-empty token and an invalid or already used link is reported on submit. Setting the new
password ends **every** session: the server revokes all refresh tokens and rotates the security
stamp, so the old access cookie, the old refresh token and the old password all stop working, and
the address is marked confirmed. There is no auto sign-in: the success card ends the local
session if it was signed in (`signOut`) and opens the normal login dialog. The password policy is
the same as in registration and the account dialog, and its `400 auth.password.*` codes are
mapped to the new-password field; the one-time token is not consumed by a policy failure, so the
same link can be retried with a valid password.

The page also shows the last login from `GET /api/users/me`; there is no active-device list
because the API does not expose one, and the screen does not invent it.

## Barber cabinet

`/barber` is a separate lazy screen for a signed-in `Barber` with a linked profile. The cabinet
has four sections — записи (`/barber`), расписание (`/barber/schedule`), salons
(`/barber/salons`) and profile (`/barber/profile`) — linked by `BarberCabinetNav` and sharing
the same gates. The bookings screen has three tabs and **opens on «Сегодня»**: the day feed,
then «Ближайшие» and «История».

The day feed answers «кто придёт сегодня и что у меня в ближайший час». It is assembled from the
**first pages** of the two existing server lists (`scope=upcoming` and `scope=past`), filtered by
the browser-local calendar date: the server already orders both scopes so that today's rows come
first, and a separate «day» endpoint would duplicate the contract for one screen. Both list
controllers are created once, above the tabs, so switching «Сегодня → Ближайшие» makes no request
and polling and actions cannot disagree with the tab. Rows and the free windows between them read
top to bottom (`<ol aria-label="Расписание на сегодня">`); windows are counted **only between
visits**, at least 20 minutes, and only from the loaded rows — working hours would need a schedule
per salon and are a separate slice. The header shows the date, «3 записи · 2 окна» and the next
client («Следующий клиент через 40 минут», «Следующий клиент в 18:30», «Идёт визит до 15:30»).
An empty window is a clickable card («свободно 45 мин» + «Занять») that opens the same
«Записать клиента» dialog with the day and the window's start, where the nearest slot is
pre-selected once the barber picks a service and a salon. More than twenty rows in a day are
truncated with «Показаны первые 20 записей дня.» — there is no per-day cursor. Actions are the
same five, through the same `CabinetBookingRow`; a terminal action makes the other list reload, so
the row moves from «Ближайшие» to «История» without a manual refresh. One failed list keeps the
feed and adds «Не удалось обновить часть записей.» with «Повторить».

The lists themselves page the server: `GET /api/barber/bookings?scope=upcoming&pageSize=20` (the
profile comes from the `barber_profile_id` claim, there is no profile query parameter) and
`scope=past` for «История». The server orders both scopes (`start_at ASC` for upcoming, `DESC` for
history), so the client never sorts; the next page is fetched with the opaque `nextCursor`. Twenty
rows are shown per page instead of growing: a page replaces the visible rows, visited pages stay
in memory for «Назад» without a request, and the range («21–40», never «из N» — the server returns
no total) lives in the shared pager footer. Both lists are mounted once, so switching tabs makes no
request at all: each keeps its page and its cache. Entering «Сегодня» only moves both back to the
first page (no request) — the day is built from the first documents, and a page left open in a
neighbouring tab must not silently hide the beginning of the day.

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
asks for the missing guest contact. On success the day feed reloads (or the screen returns from
«История» to «Сегодня») and the screen announces «Клиент записан: …».

States: skeleton while the first page loads (StrictMode may send a second, aborted request),
first-page error with «Повторить», a next-page error that keeps the rows and turns «Дальше» into
«Повторить», «На сегодня записей нет.» with «Записать клиента», «Ближайших записей нет.» and
«История пуста.». An action that empties the visible
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

- `src/pages/barber-cabinet-page.tsx` — gates, title, the `view: 'day' | 'upcoming' | 'past'`
  state, one `tablist` + panel, announcements and the «Записать клиента» trigger.
- `src/components/barber/barber-cabinet-gates.tsx`, `barber-cabinet-view-tabs.tsx` (three tabs,
  arrows/Home/End, roving `tabIndex`), `barber-day.tsx` (day header, feed and windows),
  `cabinet-booking-list.tsx`, `cabinet-booking-row.tsx`, `barber-note-dialog.tsx`,
  `barber-booking-action-dialog.tsx`, `barber-booking-dialog.tsx` (manual booking, with
  `initialDate`/`preferredStartAt` from a window) and `barber-slot-options.tsx` (shared slot
  chips/radios).
- `src/hooks/use-barber-bookings.ts` — scope, the shared pager controller, five local mutations,
  `401`, click guard; `use-barber-day.ts` — the derived day controller over the two lists (merge,
  windows, next client, routed actions, partial failure); `use-barber-clients.ts` — debounced
  client search; and `use-barber-booking-options.ts` — own services and salons.
- `src/lib/barber-bookings.ts` — DTO, list read and the five write actions; `barber-day.ts` — pure
  day helpers (`isSameLocalDay`, `mergeDayBookings`, `findDayGaps`, `findNextBooking`);
  `barber-cabinet-view.ts` — the tab union and labels; `barber-booking-create.ts`/
  `barber-booking-options.ts`/`barber-clients.ts` — the manual booking transport;
  `src/lib/barber-cabinet-path.ts` — the `/barber`, `/barber/schedule`, `/barber/salons` and
  `/barber/import` path helpers.

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

### Barber profile

`/barber/profile` is the fourth cabinet section and the only place where a barber edits the
card after onboarding. It reads `GET /api/barber-profiles/{id}` — the **own** card, not the
public one: it works before verification and returns hidden services with `isActive: false`,
so the barber sees exactly what the price list contains. The card form is the same
`BarberProfileForm` as the onboarding one, switched to `mode="edit"`: the grade is a read-only
line («Грейд назначает администратор»), «Сохранить» stays disabled until something changes,
and the Russian field errors come from the same constants as the create form. A verified and
active profile shows «Открыть, как видят клиенты» → `/barbers/{id}`; an unverified one explains
that the card appears in search after confirmation.

What changed: the barber can now fix the name, experience, «О себе» and late policy, add a
service with its own price and duration, change that price later, hide a service and bring it
back, and add or remove specializations. The service catalogue is loaded lazily — the first
open of «Добавить услугу» is the only catalogue request, and a failed catalogue does not hide
the price list that is already on screen.

«Удаление» услуги intentionally **hides** it instead of erasing the row. The row is what the
customer, barber and admin booking lists join through (`bookings → barber_services →
hair_services`): a physical delete would make historical visits disappear from all three
lists even though the slot was really taken. So `DELETE /api/barber-profiles/{id}/services/{hairServiceId}`
sets `is_active = false`, the row stays with the «Скрыта» badge and a «Вернуть» button, and the
public card stops showing it. Bringing it back is the same `POST .../services` with the same
`hairServiceId`: the hidden row is reactivated and the new price and duration are written.
Specializations are the opposite: they take part in no booking join and carry no history, so
removing one is a real delete. The UI announces every write («Услуга добавлена.», «Цена
обновлена.», «Услуга скрыта.», «Услуга снова в прайсе.», «Специализация добавлена.»,
«Специализация убрана.») and re-reads the card quietly instead of patching it.

On a phone the four cabinet links would not fit in one row, so `BarberCabinetNav` is a 2×2
grid of 44 px cells below `sm`; the notification bell stays a utility in the heading, not a
fifth section. Inputs, navigation, «Сохранить», «Добавить услугу» and the public-card link are
44 px on narrow screens, the page has no horizontal scroll at 320, and axe covers it at 1280
and 320.

- `src/pages/barber-profile-page.tsx` — gates, header, announcements, the three sections.
- `src/components/barber/barber-profile-form.tsx` — the shared create/edit form.
- `src/components/barber/barber-profile-services.tsx`, `barber-profile-specializations.tsx` —
  the price list with its three dialogs and the tags with the lazy catalogue select.
- `src/hooks/use-barber-profile.ts` — `useBarberProfile` (public, cache-first) and
  `useOwnBarberProfile` (always fresh, abort + version, quiet reload after a write).
- `src/lib/barber-profile.ts` — own-card read, six mutations, error copy;
  `src/lib/barber-cabinet-path.ts` — the `/barber/profile` path helper.

### Creating the barber profile

Registration always creates a `Customer`; only an administrator grants the `Barber` role
(`PUT /api/users/{id}/role`). The grant used to be a dead end: `/barber` and `/barber/schedule`
said «Профиль барбера не привязан к аккаунту. Обратитесь к администратору», and the existing
`POST /api/barber-profiles` was reachable only by a `Barber` — exactly the person who could not
get to it. The missing-profile gate now leads to `/barber/onboarding`, a lazy screen with one form
for every field of `BarberProfileCreateRequest`: the display name (1–120), experience (1–70),
grade, «О себе» (2000), at least one language, optional payment methods, and four optional
one-per-line lists (certificates, achievements, communication format, goodies) plus «Опоздания»
(500). Empty lines are trimmed away, an empty optional field is sent as `null`, and empty lists as
`[]`; `userId`, rating and verification are never sent — the profile always belongs to the current
user. The grade is chosen from the ten assigned grades: «Без грейда» is not offered, because only
an administrator can clear it. Client rules repeat the domain ones, and server codes
(`barberProfile.displayName.tooLong`, `barberProfile.yearsOfExperience`,
`barberProfile.grade.unknown`, `barberProfile.languages.unknown`, …) are mapped to Russian field
errors; `429`, a network failure and `5xx` show a form error with «Повторить» (the form is not
re-submitted automatically).
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

### Import from Google Docs and calendars

`/barber/import` переносит старый журнал барбера одним вечером: файл календаря или
вставленный текст, проверка и создание пачки. Экран открывается из кабинета ссылкой
«Перенести записи», живёт в своём lazy-чанке, повторяет гейты кабинета (сессия, роль
`Barber`, привязанный профиль) и показывает `BarberCabinetNav active="bookings"`.
Состояние шагов — только в памяти: ни файл, ни вставленный текст не попадают ни в URL,
ни в `localStorage`.

- **Paste.** Одно `textarea` с примером, лимит 50 строк и 10 000 символов, кнопки
  «Разобрать» и «Очистить», строка про персональные данные со ссылкой на `/privacy`.
- **Parse.** `src/lib/import-parse.ts` — чистые функции без сети: телефон ищется первым
  (десять или одиннадцать цифр → `+7…`), затем дата (`DD.MM`, `DD/MM`, `D месяц`,
  `сегодня/завтра/послезавтра`, день недели; без года — текущий, прошедшая больше чем на
  сутки — следующий), затем время (`14:00`, `9:05`, `14-00`; точка — не разделитель, поэтому
  `14.05` остаётся датой), услуга (точное, с начала, внутри; самое длинное совпадение) и
  салон (по названию или городу, иначе основной). Строка-заголовок («12 ноября») задаёт дату
  следующим, шапка таблицы и пустые строки пропускаются. Ошибки строк — `noDate`/`badDate`,
  `noTime`/`badTime`, `noPhone`/`badPhone`, `noName`, `noService`/`ambiguousService`, `past`,
  `duplicate`.
- **Preview.** Таблица на десктопе (`caption` «Разобранные записи») и карточки до `sm`;
  подсветка `bg-destructive/5`, `aria-invalid` и `aria-describedby` на поле, селект услуги и
  салона, поля даты, времени, имени и телефона правят строку на месте. Сводка «Готово 7 из
  10 · 2 без услуги · 1 в прошлом» уводит фокус в первую проблемную строку.
- **Create.** Последовательные `POST /api/barber/bookings` по одной строке: гость
  (`customerId: null`), ключ идемпотентности создаётся при разборе и переживает повтор.
  `409 booking.add.overlap` даёт «Время занято.» и три ближайших слота (`GET
  /api/barber/slots`, `own: true`), `400 booking.create.startAt.inPast` — «Время уже прошло.»,
  `429` и сеть останавливают пачку с «Продолжить» (`429` — через минуту), `401` поднимает
  `SignInGate` и продолжает с текущей строки.
- **Report.** «Перенесено 7 из 10 записей.», список неудачных строк с причиной,
  «Повторить неудачные» (те же ключи) и «Открыть записи» → `/barber`.
- **States.** Скелетон и гейты кабинета, ошибка загрузки услуг и салонов с «Повторить»,
  `role="status"` на сводке, прогрессе и отчёте, `role="alert"` на остановке пачки.

Files: `src/pages/barber-import-page.tsx`, `src/components/barber/import-form.tsx`,
`import-preview.tsx`, `import-result.tsx`, `src/hooks/use-barber-import.ts`,
`src/lib/import-parse.ts`, `src/lib/salon-time.ts`, `src/lib/barber-cabinet-path.ts`.

### Import from calendars

Второй источник того же экрана — файл календаря (`.ics`) из Google Calendar, Apple Calendar,
Outlook или Яндекс Календаря. Это такой же одноразовый перенос, а не синхронизация: файл
читается в браузере и превращается в те же строки `ImportDraft`, что и вставленный список.

- **Файл.** Один `.ics` до 2 МБ (лимит проверяется по `file.size` до чтения), drag-and-drop и
  скрытый, но фокусируемый input с клавиатурным путём; период — `7`/`30`/`90` дней, по
  умолчанию 30. `src/lib/ics-decode.ts` разбирает BOM/UTF-8/UTF-16, нормализует переводы строк
  и требует `BEGIN:VCALENDAR`.
- **Разбор.** `src/lib/ics-parse.ts` лениво (`await import('ical.js')`) раскрывает
  `RRULE`/`RDATE`/`EXDATE`/`RECURRENCE-ID` и `VTIMEZONE`, сохраняя абсолютный момент события;
  floating-время читается по поясу салона. `src/lib/ics-map.ts` маппит событие в
  `ImportDraft` общими с текстовым импортом матчерами (`src/lib/import-match.ts`), а
  `src/lib/ics-identity.ts` строит ключ идемпотентности.
- **Пропуски.** All-day, отменённые, прошедшие, вышедшие за выбранный период, слишком длинные
  (> 12 часов), события с неизвестным часовым поясом и события без `UID` не создаются: они попадают
  в «Пропущено N событий» с причиной (`allDay`, `cancelled`, `past`, `outsidePeriod`,
  `tooLong`, `unknownTimeZone`, `noUid`, `invalid`). `TRANSP:TRANSPARENT` не пропускается —
  строка есть, но галочка снята.
- **Выбор и отправка.** Предпросмотр тот же, с построчными правками; выбрать можно до 50
  событий за раз, остальные — «Перенести ещё» без повторного чтения файла. Записи гостевые
  (`customerId: null`); ключ детерминированный — `UID` + `RECURRENCE-ID` → `ics:` + 32 hex,
  поэтому повторная загрузка того же файла возвращает уже созданные брони, а не дубли.
- **Приватность.** Файл не загружается на сервер: `File.arrayBuffer()` → `TextDecoder` →
  `ical.js` в памяти вкладки. `SUMMARY`/`DESCRIPTION` и телефоны не попадают ни в URL, ни в
  `localStorage`, ни в `customerNote`. `ical.js@^2.2.1` (MPL-2.0) — единственная новая
  зависимость, и его чанк скачивается только при разборе календаря.
- **Ошибки.** `409 booking.add.overlap` — «Время занято.» и три ближайших слота, `429` —
  пауза на минуту, `401` — `SignInGate`, сеть — «Продолжить с этой строки».

Files: `src/lib/ics-decode.ts`, `ics-parse.ts`, `ics-map.ts`, `ics-identity.ts`,
`src/lib/import-match.ts`, `src/components/barber/ics-import-form.tsx`, `import-skipped.tsx`,
`import-source-switch.tsx`; the extended `import-preview.tsx`, `import-result.tsx`,
`src/hooks/use-barber-import.ts` and `src/pages/barber-import-page.tsx`.

### Notifications

`/barber/notifications` is the reminder inbox. It is a utility screen, not a fourth navigation
item: the entry is the 44×44 bell in the cabinet heading (`aria-label` «Уведомления,
непрочитанных: N», badge hidden at zero), and the unread counter polls once a minute and on
tab focus; a failed counter request just hides the badge.

The inbox is the **source of truth**: `barber_notifications` rows exist for every due visit and
for the morning digest, are kept for 180 days and stay readable when delivery is off. The page
reads `GET /api/barber/notifications` through the shared pager (twenty rows, «Уведомлений пока
нет.», «Назад» without a request); «Upcoming» shows the visit time in the salon zone, service and
salon («14:00 · стрижка · «Барбершоп на Ленина»»), a digest shows «Сегодня 4 записи» / «Первая в
10:00». A row marks itself read and opens `/barber`; «Прочитать все» is one idempotent `read-all`
and a quiet reload, disabled at zero unread.

Settings sit above the list: «Присылать напоминания» and «Утренний дайджест» switches, an
`<input type="time" step="900">` for 06:00–12:00 and the 15/30/60 lead select. Each change saves
one `PUT` after a 500 ms debounce, a `400 notificationSettings.*` lands on its field, and the
saved value survives a reload.

Telegram is an **optional delivery channel** on top of the same rows, off without
`Telegram:BotToken`/`BotUsername`: `botConfigured: false` renders «Telegram не настроен.
Уведомления будут видны только здесь.» and a disabled «Подключить», so the feature needs no
external service or consent to work. With a configured bot, «Подключить» issues a one-time code
and a `t.me` deep link; the client never receives `chatId`.

Files: `src/pages/barber-notifications-page.tsx`, `src/hooks/use-barber-notifications.ts`,
`use-unread-notifications.ts`, `use-notification-settings.ts`, `src/lib/barber-notifications.ts`,
`src/components/barber/barber-cabinet-gates.tsx` (`BarberUnreadBell`). E2E:
`e2e/specs/barber-notifications.spec.ts` (bell, both rows, settings and the disabled Telegram
block) and `barber-notifications-mutations.spec.ts` (read-all with the badge gone, idempotent
repeat, `leadMinutes = 60` surviving a reload); the axe matrix scans the route at 1280 and 320.

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
Russian text and the dialog stays usable — there is no third attempt. The anonymous endpoints are
the exceptions: `POST /api/auth/register`, `POST /api/auth/email-verification/confirm` and the two
password-reset calls. None of them has an ambient identity: the confirmation and reset secrets are
the tokens from the letters, which may be opened in another browser, so the client does not fetch
or send a token there (see `AGENT.md` §9.4 and §14). `POST /api/auth/password-reset` takes the
email from the body rather than from the session, and `POST /api/auth/password-reset/confirm` is
authorised by the one-time token, so `src/lib/password-reset.ts` neither fetches
`/api/auth/csrf` nor sends `X-CSRF-TOKEN`. Booking creation
goes through `mutateWithCsrf` like every other cookie mutation.

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
(`src/lib/legal.ts`, `2026-11-11`) — the policy edition the checkbox names. Client checks before
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
`/account/security?verification=sent`; the role is always `Customer` and `isEmailVerified` is
`false`. The server sends the confirmation letter in the registration request itself, so the
new landing is the security card with an «Письмо отправлено на …» notice (the query is read once
and removed from the address). If the auto sign-in fails, registration is **not** repeated (a
second attempt would be `409`): the form is replaced by the «Аккаунт создан» card with a
`LoginDialog` (`redirectTo="/account"`).

Public codes are mapped at the field or the form: `user.create.emailTaken` (409),
`auth.register.consentRequired` (the checkbox error), `auth.register.consentVersion` (the
form-level «Политика обновилась» alert), `auth.password.required` / `tooShort` / `tooLong` /
`common` / `equalsEmail` / `equalsName`, `user.email`, `user.firstName`, `user.lastName`; `429`,
offline, `5xx` and anything else have their own Russian text. The English `detail` is never
shown. Both registration and sign-in use the auth rate limiter (10 requests per minute per IP),
so a registration with its auto sign-in spends two of them. The new account is active
immediately; email verification is a **soft** flag that does not block sign-in or booking, so the
user can confirm the address now or later from `/account/security` (the link in the letter opens
`/verify-email?token=…`). Accounts created before the consent record
existed and the ones an administrator creates have no record — that is expected and the admin
card says «Записи нет».

- `src/pages/register-page.tsx` — title, loading skeleton, anonymous card and the signed-in gate.
- `src/components/register-form.tsx` — fields, client/server errors, the consent checkbox, submit
  guard and the «Аккаунт создан» card.
- `src/lib/auth.ts` — `RegistrationInput` and `register()`; `src/lib/register-path.ts` —
  `REGISTER_PATH` / `isRegisterPath`.
- `src/pages/verify-email-page.tsx` — the confirmation landing; `src/lib/email-verification.ts` —
  the two transport calls and the code mapping; `src/lib/verify-email-path.ts` —
  `VERIFY_EMAIL_PATH` / `isVerifyEmailPath`.

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
- **The consent record.** `PRIVACY_POLICY_VERSION` equals `PRIVACY_POLICY_DATE` (`2026-11-11`) and
  the backend's `PrivacyPolicy.Version`; the registration form sends it and the server stores it
  with the UTC time and the IP. §2.2 of the policy states what is recorded, §4.1 that the record is
  stored with the account. Editing the policy text bumps both constants and the server constant —
  otherwise registrations would start failing with `auth.register.consentVersion`. The
  2026-11-11 edition also names the profile photo in §3 and its retention in §4; older records
  keep the edition they signed.
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

**Avatar.** Each row and the card show the owner's photo (24 px in the row) with initials as the
fallback, and a textual moderation badge — «На проверке», «Отклонено» (with the reason) or
«Опубликовано» — plus a «Фото скрыто» badge when the blocking flag is set. A `Pending` frame
offers «Одобрить»/«Отклонить» (the rejection `alertdialog` requires a reason of 1..500) through
`POST .../avatar/approval|rejection`; an approved one offers «Скрыть фото» (an `alertdialog`
explaining that the photo disappears from the header, search, cards and booking lists while the
account stays active) and «Вернуть фото» without a dialog. The requests use
`POST`/`DELETE /api/admin/users/{userId}/avatar/block` with `barber.userId` — the avatar belongs to
the profile owner, not to the profile — and the row and the card update locally before a quiet
re-read. The raw URL stays visible to the admin so the frame can be restored.

## Admin users

`/admin/users` lists the users through `GET /api/admin/users` with status and role filters and a
debounced name/email search, twenty rows per page through the same shared pager. Filters and search
start from the first page; the selected user lives in the URL so paging does not close the card,
and the mobile split keeps the list on its page. Role change, block/unblock and email/phone
confirmation update the row locally (or remove it when it leaves the active filter) before the
quiet re-sync. The screen never shows a total count, because the API does not return one.

The card has a read-only «Согласие на обработку ПД» row: «Редакция 2026-11-11 · 11.11.2026, 14:05
UTC» for a self-registered account, or «Записи нет — аккаунт создан до 11.11.2026» for accounts
that predate the record and for users created by an administrator. The IP of the consent is not
returned by the API and is never displayed.

**Avatar.** The row and the card show the user's photo (initials fallback) with the same textual
moderation badge («На проверке» / «Отклонено» + reason / «Опубликовано») and a «Фото скрыто»
badge for the blocking state. The card's avatar block offers «Одобрить»/«Отклонить» for a
`Pending` frame (rejection asks for a mandatory 1..500 reason), «Скрыть фото» behind an
`alertdialog` for an approved one and «Вернуть фото» without one, or «У пользователя нет фото —
скрывать нечего.» when there is no URL. The shared
`POST`/`DELETE /api/admin/users/{userId}/avatar/block` and the two pre-moderation endpoints answer
`204`; the row is updated locally, the announcement offers the opposite action, the card is
re-read quietly, and the raw URL stays visible to the admin so the frame can be decided or
restored.

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
- `src/lib/response-guards.ts` — `isAdminSummary`: four mandatory blocks (reviews, barbers,
  salons, photos), `pendingCount`, `unverifiedCount`, `inactiveCount`,
  `photos.pendingAvatarsCount` and `photos.pendingPortfolioCount` must be non-negative safe
  integers, and `oldest` is either `null` or
  `{ id, rating 1..5, createdAt, barberDisplayName, customerDisplayName }`. Extra fields are
  ignored, so a wider contract keeps working.
- `src/hooks/use-admin-summary.ts` — one read with an `AbortController` and a request token:
  `reload` is the first load (skeleton), `refresh` is the quiet update that keeps the old numbers
  on screen and turns a failure into a `role="alert"` line; `401` sets `requiresSignIn`;
  `updatedAt` is the last success. StrictMode-safe, a stale response is dropped.
- `src/components/admin/summary-screen.tsx` — the `h1`, the subtitle, «Обновлено в HH:MM», the
  single «Обновить», and four `<section>`s separated by thin dividers.

What it shows:

- **«Отзывы на модерации»** — `pendingCount` («14 отзывов без публикации») and the oldest review:
  «Самый старый ждёт 3 дня», the rating, the client and the barber names, and «Открыть отзыв» →
  `/admin/reviews/{id}`; below, «К очереди отзывов» → `/admin/reviews`. The copy explains that both
  new reviews and reviews hidden by an administrator wait here — `is_published` is one flag.
  «Ждёт» is counted from `awaitingSince` — the moment the review entered the queue, so a text
  edit that returns an old review shows minutes, not months.
- **«Барберы без подтверждения»** — `unverifiedCount` and «Открыть барберов» → `/admin/barbers`,
  which opens on the «На проверке» queue.
- **«Выключенные салоны»** — `inactiveCount` and «Открыть каталог салонов» →
  `/admin/catalogue?tab=salons`.
- **«Фотографии на проверке»** — two numbers from `photos.pendingAvatarsCount` and
  `photos.pendingPortfolioCount` with «Открыть аватары» → `/admin/photos` and «Открыть портфолио»
  → `/admin/photos?tab=portfolio`; the copy says that new avatars and works are not public until
  an administrator approves them. When both are zero the block says «Все фотографии проверены.»
  and renders no links.
- Zeros are named honestly: «Очередь пуста — все отзывы проверены.», «Все профили подтверждены.»,
  «Выключенных салонов нет.», «Все фотографии проверены.».

The counters use the **same predicates** as the sections (`is_published = false`,
`is_verified = false` without `is_active`, `is_active = false`, `Pending` avatars with a URL and
submitted-at, and `Pending` portfolio photos), so a number is exactly the size of
its queue; the two photo numbers are exactly the sizes of the `/admin/photos` tabs, and the oldest
review is the first row of the same FIFO moderation page the queue uses.
There is deliberately no `total` in `PagedResponse` or in the existing lists: one screen needs the
number, and the shared pagination contract stays unchanged.

What it does **not** do: no mutations, no charts, periods, trends or comparisons with the past, no
KPI tiles, no «Показать ещё», no totals, no export and no audit writes. Customer email and phone
are not in the contract — only names; the full context stays in `GET /api/admin/reviews`.

## Admin statistics

`/admin/statistics` is the «Статистика» screen and the second rail item, right after «Сводка».
It answers «what happens over a period» from one typed aggregate read,
`GET /api/admin/statistics?from=&to=&compare=true|false`, and is `noindex` and `AdminOnly` like
the rest of the admin. There is no public «in numbers» page and no statistics in the barber
cabinet.

- `src/lib/admin-statistics.ts` — `AdminStatistics` and the section types,
  `fetchAdminStatistics` (the required `from`/`to` and `compare=true` only when enabled),
  `describeAdminStatisticsError` (`403` role, `429` rate limit, `400` period, otherwise the
  Russian fallback; the English `detail` never reaches the DOM), and the period helpers:
  `parseStatisticsPeriod`/`resolveStatisticsPeriod` fall back to the last 30 UTC days for a
  broken or longer-than-366-day URL, `statisticsPath` writes `?from=&to=&compare=1`,
  `formatStatisticsRange` labels the period.
- `src/lib/response-guards.ts` — `isAdminStatistics`: all nine sections, non-negative safe
  integers for counters, shares `0..1`, a five-item distribution, a day series whose length
  equals `period.days`, and nullable `previous`; extra fields are ignored, a broken body is a
  screen error.
- `src/hooks/use-admin-statistics.ts` — one read with an `AbortController` and a request key:
  `reload` is the first load (skeleton), `refresh` keeps the sections and turns a failure into a
  `role="alert"` line, changing the period starts a new load, `401` sets `requiresSignIn`, and
  `updatedAt` is the last success. StrictMode-safe, no auto-refresh.
- `src/components/admin/statistics-screen.tsx` plus `components/admin/statistics/*`
  (`stat-section`, `metric-strip`, `trend-chart`, `status-bar`, `top-table`).

What it shows: six tiles in one strip (completed visits, turnover — «по завершённым визитам»,
not revenue — average check, new accounts, no-show rate with absolute numbers below a sample of
10, and stuck bookings as a snapshot); «Записи» with a hand-made SVG chart of created vs
completed visits, a `<details>` table duplicate, the status strip and «Закрыто визитов: … из …»;
«Клиенты» and «Предложение» in two columns on `lg`; «Лидеры» with barbers/services/cities tables
(minimum 3 completed visits, an «Остальные» footer and the minimum note); «Качество и
модерация» with the published-reviews rating, the 1–5 distribution, new reviews and published
share, and request/salon moderation with the approval rate and median decision hours; «Как
считаем» with the formulas and the honest turnover disclaimer.

The period is in the URL — `?from=&to=&compare=1` — and dates are inclusive UTC days, at most
366; presets set 7/30/90/365 days, the two native date inputs allow a custom range. The only
action is «Обновить» (quiet refresh); there is no auto-refresh, and the states cover the
skeleton, «За этот период данных нет. Попробуйте расширить период.», the first-page error with
«Повторить», `401` gate, `403`, `429` and `400` texts.

Performance: measured on a 1.2M-booking load database, the request takes about 3.1 s warm
(series 672 ms, clients 702 ms after the `NOT EXISTS` rewrite, overview+statuses 459 ms, tops
281–297 ms); this is above the 300–500 ms budget, so **no migration or rollup was added** —
`backend/AGENT.md` §15.2 records the numbers and proposes a daily rollup as a separate decision.
No new dependency, no `ui/**` change, no schema change.

## Admin audit

`/admin/audit` is the read-only change journal: when, who, what and on which entity changed, with
an expandable per-row diff. The journal is the last rail item, the route is the exact
`/admin/audit` (no card route and no builder — a row already carries its whole diff), and the page
is `noindex` like the rest of the admin.

- `src/lib/admin-audit.ts` — types, `ADMIN_AUDIT_ENTITY_TYPES`/`_LABELS`/`_ACTION_LABELS`/
  `_FIELD_LABELS`, `formatAuditAction` (the label comes from the tail after the last dot of
  `user.updated`), `formatAuditValue` (`null` → «—», booleans → «Да»/«Нет», `[redacted]` → «скрыто»,
  Russian role, grade, booking-status and service-category labels with the raw string as a
  fallback, via `bookingStatusLabel` and `SERVICE_CATEGORY_LABELS`; `moderationStatus` is Russian by
  entity type — `SALON_MODERATION_LABELS` for `salon`, `PHOTO_MODERATION_LABELS` for
  `portfolio_photo` — and `avatarModerationStatus` always uses the photo labels),
  `fetchAdminAuditEntries` and `describeAdminAuditError`.
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
  «Отзыв», «Салон», «Услуга», «Специализация», «Бронь», «Фото портфолио» — the last option),
  a debounced (300 ms) author email search capped at 100 characters, and «С даты» / «По дату».
  The two dates are **inclusive UTC days** —
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
  +70000000010», «Статус: Подтверждена → Неявка», «Файл», «Фото профиля», «Статус проверки
  фото»). Booking statuses and service categories are rendered in Russian, `moderationStatus` is
  Russian for salons and portfolio photos («На проверке → Одобрено»), and `isActive` is
  «Активность» (it serves both a user and a catalogue row).
  An unreadable payload is honest: «Изменения не записаны.» Rows expand independently.
- States: skeleton with `aria-busy`, «Журнал пуст» without filters / «Ничего не найдено» with
  them, a first-page error with its reason and «Повторить» (never the English `detail` or the
  status code), a next-page error in the pager without clearing the rows, `401` → the sign-in gate
  with `returnTo="/admin/audit"`, `403` → «Раздел доступен только администратору.».
- The journal is **immutable**: there is no write endpoint, no «Изменить», «Выключить», «Удалить»
  or «Очистить журнал», and no «Показать ещё». `AuditTrail` writes the table on every
  `SaveChanges`; the screen only reads.
- The journal covers **eight** entity types — `User`, `BarberProfile`, `PortfolioPhoto`, `Review`,
  `Salon`, `HairService`, `Specialization` and `Booking`. Booking rows carry only `Status`,
  `BarberNote` and `CancellationReason` changes: creating a booking is deliberately not audited
  (`AuditsCreation: false`). `PortfolioPhoto` is the only audited `BarberProfile` child: upload,
  caption, order, publication, block/unblock and approve/reject write
  `portfolio_photo.created|updated|deleted` (the field diff carries `url`, `description`,
  `sortOrder`, `isPublished`, `isBlocked`, `moderationStatus`; the rejection reason,
  `reviewedAt`/`reviewedByUserId` and `thumbnailUrl` are not in the payload). Services, salons,
  schedules and time offs are still not covered. Avatar uploads, deletions and decisions appear
  under `User` as `user.updated`. The journal stays immutable and read-only; retention,
  partitioning and export are still separate slices, and catalogue deletes do not exist in the API.

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
  grade labels, the five Russian booking statuses, the service categories, `moderationStatus` in
  Russian for `salon` («Одобрен») and `portfolio_photo` («Одобрено»), `avatarModerationStatus`,
  `url` as a string, unknown values and an unknown entity type), `formatAuditAction` by the action
  tail, the exact order of the nine entity values (`all` plus the eight types) and their labels
  (the new values are appended; «Фото портфолио» is last), and the error mapping for `403`, `429` and
  `admin.audit.cursor.invalid`. `AuditEntry` (colocated with the component) covers `<time>`, the
  short id with the full id in `title`, «Система» and «Удалённый пользователь», the expandable
  `<dl>` diff «Роль: Клиент → Администратор», «Активность» for `isActive`, the booking diff
  «Статус: Ждёт подтверждения → Отменена», the service «Название: … → …» diff, the
  `portfolio_photo.created` row «Фото портфолио»/«Создано» with «Файл», «Порядок» and «Статус
  проверки: На проверке → Одобрено», the avatar `user.updated` row with «Фото профиля» and
  «Статус проверки фото», the empty-diff text and independent expansion; `AuditScreen` is an
  MSW-backed screen test: the loaded rows and
  pager range, the entity-type filter starting a new first page without a cursor, a switch to
  «Салон» sending `entityType=salon`, a switch to «Фото портфолио» sending
  `entityType=portfolio_photo`, «Журнал пуст» and the `403` text.
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
  covers the four blocks with their numbers, links and copy, the honest zeros, the `401` gate, the
  first-page error with «Повторить», the refresh error that keeps the blocks, exactly one request
  per «Обновить» and the «Обновлено в …» label; the `formatProfilesCount`/`formatSalonsCount`
  plural forms are covered directly.
- `admin-statistics` — the period URL (`from`/`to` always, `compare=true` only when enabled),
  the strict guard (a fractional counter, a share outside `0..1`, a four-item distribution, a
  day series of the wrong length and a negative `other` are broken; extra fields are fine), the
  `403`/`429`/`400 admin.statistics.period`/fallback error mapping, the 30-day fallback for a
  broken or too-long period and `formatStatisticsRange`. `StatisticsScreen` covers the six tiles
  with their numbers, the chart `role="img"`, the status strip, the three top tables with
  «Остальные», the table duplicate, a preset and a custom date each making one new request,
  compare sending `compare=true` and showing a delta, the small-N absolute no-show display, the
  empty period, the first-page error with «Повторить», the `403`/`429`/`400` texts, the `401`
  gate and exactly one quiet refresh that keeps the sections. `formatPercent`, `formatDelta`,
  `formatCount` and `formatNumber` are covered in `format.test.ts`.
- `media` — `src/lib/media.test.ts`: `portfolioPhotoSource` keeps the original in `src` and builds
  the `256w/512w/1024w` candidates plus the gallery `sizes` (or a custom value); `avatarImageSource`
  builds `1x/2x/3x`; external `http(s)`, `data:`/`blob:` and URLs that already carry `?`/`#` get
  `src` only.
- `portfolio-photos`, `image-prep`, `BarberProfilePhotos`, `BarberPortfolio` — the multipart
  upload with a fresh CSRF token and one retry with the same `FormData`, the `201 { id }` guard,
  caption/order/publication/delete/block calls, the `413`/409/404/403/storage error mapping, the
  type and 15 MiB gates before decoding, the 1600 px white-background JPEG with the 0.7 retry,
  the cabinet section (empty, limit, blocked row without «Вернуть», dialogs, preview `srcSet`)
  and the public gallery (no section when empty, `srcSet` with the three width descriptors and
  `sizes`, alt text, lightbox counter/edges/arrows/Escape keeping the original `src` and no
  `srcset`, broken image).
  `api.test.ts` proves `FormData` gets no JSON `Content-Type` while an ordinary body does.
- `avatars`, the avatar card and the avatar in the shell/hero — `POST /api/users/me/avatar` with
  the single `file` field and a fresh CSRF token, one retry with the same `FormData`, the
  `201 { avatarUrl }` guard (relative `/api/media/avatars/…` only), the `DELETE` call and every
  `describeAvatarError` branch (shared upload codes, the three new avatar codes, `413` without
  ProblemDetails, `401`, `429`, network, `5xx`, `canRetry`). `image-prep.test.ts` covers the square
  centre crop (1200×800 → 800 → 512 and 800×1200), up-scaling 300×300 to 512, the 0.7 retry and all
  decode/type/size errors; `avatar-card.test.tsx` covers upload and the `role="status"`
  announcement, the type rejection without a request, `413`, `401`, the delete `alertdialog` and
  focus returning to the upload button, and a delete failure that keeps the dialog;
  `account-shell.test.tsx` and `barber-hero.test.tsx` prove `AvatarImage` appears when
  `avatarUrl` is set and initials remain the fallback; `profile-provider.test.tsx` proves one
  profile read per authenticated load (zero for an anonymous visitor), `StrictMode` making no
  second request, a changed user refetching, `setAvatar` reaching every subscriber without a second
  `GET`, and `401` only setting `requiresSignIn`; `use-my-profile.test.tsx` keeps the #85
  `setAvatar`/`save` scenarios on the context wrapper.

Tests are colocated with the module (`src/lib/api.test.ts`, `src/components/login-dialog.test.tsx`)
and compare Russian copy as a whole string, saying in the test title that it is copy.

Not covered by the unit layer: most screens and hooks (only the audit and admin-bookings screens have
direct tests), and everything that needs the real API, router, cookies or focus — that is the E2E
layer below.

## E2E

Playwright drives the real SPA against the real API and a real PostgreSQL: there is no second test
stack and no mocked success responses. The suite covers the paged lists, the admin journal, the
admin bookings desk, the admin summary (including the photo queues' fourth block) and the admin
statistics, sign-in, public reviews, the portfolio gallery with its lightbox (axe at 1280 and 320)
and its `srcset`/`?size=` WebP variants (`ETag`/`304`, the honest cache), the admin
approve/reject/block of a photo, the avatar upload in the cabinet, its
pre-moderation queue, its header and search appearances, its `srcset` WebP variant and its public
hero (axe at 1280 and 320,
an admin sees the same image, deletion restores the initials), email
verification (registration → letter in Mailpit → link → confirmed cabinet), password reset
(registration → `/forgot-password` → letter in Mailpit → new password → sign-in), the keyboard
paths and an axe scan of every screen and role in two widths (1280 and 320). `npm test` (Vitest)
stays separate and fast; E2E never runs inside it. The last full local run (2026-11-12, after the
photo-audit slice) is **309 passed, 6 skipped, 0 failed** — 315 tests in 6.3 min with
`--workers=2`; the queue page object is `e2e/pages/admin-photos-page.ts`. The #90 media scenes
extend the two existing mutation specs (gallery/cabinet `srcset`, the `?size=` WebP response,
`ETag`/`304`, the hidden-frame `404` and the new cache header). The #91 scenes add the
«Фото портфолио» filter to `admin-audit.spec.ts`
(it sends `entityType=portfolio_photo` and finds nothing under the fixture date) and one
audit-API assert, made from the admin context, to each of the two media mutation specs (exact
`entityId`, `action` and `changes`, no rejection reason). The previously documented
cache flake is fixed: a guest `playwright.request.newContext` inherited the owner's `storageState`,
so "anonymous" requests carried the cookie and ASP.NET added `no-store`; every guest context now
passes an explicit empty `storageState`, and a real guest asserts
`public, max-age=0, must-revalidate` and `ETag` on the visible media file.

### One-time setup

```bash
npm run test:e2e:install          # Chromium
```

A separate database is mandatory — never `barbersearch` and never `barbersearch_load`:

```bash
createdb -h localhost -U postgres barbersearch_e2e
```

### Mailpit

Verification and password-reset letters are **real SMTP messages, not mocks**: the E2E suite
reads the inbox through the Mailpit HTTP API (`GET /api/v1/messages`, then
`GET /api/v1/message/{id}`). Start the container before the API — it is the same pinned image as
in `backend/docker-compose.yml`:

```bash
docker run -d --rm --name barbersearch-mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit:v1.21.8
```

The inbox UI is http://localhost:8025. `e2e/global-setup.ts` polls `GET /api/v1/info` for about
15 seconds and fails immediately with this command if the container is missing, so the first test
cannot flake on a not-yet-ready SMTP port.

The same inbox serves all mail flows. The photo pre-moderation scenes do not read the inbox, but
the suite still requires Mailpit because registration, email verification, email change and
password reset do; a missing container fails the run before the first spec, not in the middle.
Registration sends the verification letter to the new
address first, so `password-reset.spec.ts` waits for the reset message **by recipient and
subject** — «Сброс пароля — BarberSearch», `waitForPasswordResetMessage` in
`e2e/fixtures/mail.ts` — instead of taking the first message to that address; the verification
token extractor is unchanged. The email-change flow adds two more subjects, both constants in
`e2e/fixtures/mail.ts`: «Подтвердите новый адрес электронной почты — BarberSearch» goes to the
**new** address (`waitForEmailChangeMessage` + `extractEmailChangeToken`, link
`/confirm-email-change?token=…`), and «Запрошена смена адреса электронной почты — BarberSearch»
goes to the **old** address and deliberately contains no token
(`waitForEmailChangeNoticeMessage`). The reset spec is desktop-only: it spends several of the 10
auth requests per minute per IP, and the mobile project is skipped explicitly with that reason,
while 320 px is covered by the axe scan of both forms.

The API must be running with the email settings and the public link base (the letter's link points
at Vite, not at the API):

```bash
# from ../backend
ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=barbersearch_e2e;Username=postgres;Password=postgres" \
  Email__Enabled=true Email__Host=localhost Email__Port=1025 Email__UseTls=false \
  Email__FromAddress=no-reply@barbersearch.test App__PublicUrl=http://localhost:5173 \
  EmailOutbox__IntervalSeconds=1 \
  Storage__RootPath=/tmp/barbersearch-e2e-photos \
  dotnet run --project BarberSearch/src/BarberSearch.Web --launch-profile http
```

`Storage__RootPath` keeps uploaded files (portfolio photos and avatars) out of the checkout; CI
passes `${{ runner.temp }}/photos` instead. Without it the API uses `App_Data/photos`, which still
works but pollutes the working tree and survives between runs.

Without these variables registration still returns `201`, but no letter is sent: the resend button
honestly answers `409 auth.emailVerification.unavailable`, `POST /api/auth/password-reset` answers
`409 auth.passwordReset.unavailable`, and both `email-verification.spec.ts` and
`password-reset.spec.ts` fail at the Mailpit step. Migrations and both seed files (the backend's
minimal seed plus the E2E fixtures):

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
`email-verification.spec.ts` and `password-reset.spec.ts` additionally register fresh
`e2e+<timestamp>@barbersearch.test` customers (one per project per run). The API has no user
deletion and the seed does not remove them, so they accumulate in `barbersearch_e2e`; the rows are
additive, and the admin user list pages through them, so no other scene depends on the total.

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

### Localization check

`e2e/specs/i18n.spec.ts` switches the landing to English, checks `<html lang>`, the tab title, the
persisted choice after a reload, the Russian return path and the 320 px layout (no horizontal
scroll). It needs no authenticated account, so it also runs in isolation with
`npx playwright test e2e/specs/i18n.spec.ts --project=desktop-chromium --no-deps`.

### Accessibility gate

`e2e/specs/a11y.spec.ts` scans every screen and role at 1280, `e2e/specs/a11y-narrow.spec.ts` the
narrow branches at 320. Only `critical` and `serious` fail the run; `moderate`/`minor` findings are
written to the Playwright report as `axe-advisory` annotations. There is **no allow-list**: a known
finding is fixed or gets a new number in `AGENT.md` §14 and an entry that cites it — an exception
without a debt number is not allowed. `/verify-email` is scanned in both files at its
invalid-link state, and `email-verification.spec.ts` adds the confirmed state at 1280.
`/forgot-password` and `/reset-password` are scanned at 1280 and 320 in their form states with a
fake token (the axe scenes never submit: every submit spends an auth request); the sent, success
and invalid states are covered by `password-reset.spec.ts` and the unit tests.

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
For the reminder inbox it adds exactly **two `barber_notifications` rows for `e2e-barber`** — an
unread `Upcoming` (14:00 in `Europe/Moscow`, «стрижка», «Барбершоп на Ленина») and a read `Digest`
(four bookings, first at 10:00) — plus `barber_notification_settings` at the server defaults
(08:00, 30 minutes). The times are built from «today» in the salon zone, so the row label reads
«Сегодня» on any run day, and the badge is exactly `1`. The section deletes and re-inserts only
this barber's rows, so a reseed restores the unread state and `leadMinutes = 30` after the
mutating scenes.
For portfolio photos the seed keeps the isolated `profileBarber`
(`9e100000-0000-4000-8000-000000000015`) clean: it deletes that profile's `portfolio_photos` rows
before its fixture upsert, so `barber-profile-photos-mutations.spec.ts` starts from «Фотографий
нет.» and can upload the same `e2e/fixtures/portfolio-photo.png` (a real **800×600** PNG, about
101 KB) on every run; a failed scene is cleaned through the API (`afterEach`, plus a best-effort
`afterAll`). Uploaded files live outside PostgreSQL in `Storage:RootPath`; orphan files from a
failed run do not affect the seed or the tests and are ignored by the media route once their
database row is gone.
For avatars the seed resets `avatar_url`, `is_avatar_blocked`, `avatar_moderation_status`,
`avatar_rejection_reason`, `avatar_submitted_at`, `avatar_reviewed_at` and
`avatar_reviewed_by_user_id` on every fixture account, including the isolated `profileBarber`
(whose conflict update now clears them too) and the `mutations` customer
`e2e-mutations@barbersearch.test`, so both avatar specs start from the initials and
`/admin/photos?tab=avatars` starts empty. A failed scene deletes the file and the row through
`DELETE /api/users/me/avatar` in `afterEach`; the showcase barber `…0001` is never mutated.
The portfolio fixture deletes `profileBarber`'s `portfolio_photos` rows before the upsert (see
above), so `/admin/photos?tab=portfolio` is empty on a reseeded database; the summary's «Фотографии
на проверке» block then shows the honest zero and no links.
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
