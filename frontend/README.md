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
```

## Routes

| Path | Screen |
|---|---|
| `/` | landing page: real barber search by city and date, how it works, account CTA |
| `/barbers/{id}` | public barber profile: services, salons, weekly schedule, rating |
| `/account` | customer cabinet: bookings, cancellation, review, password |
| `/admin` | admin shell; redirects to `/admin/reviews` |
| `/admin/reviews` | review moderation queue (pending / published tabs) |
| `/admin/reviews/{id}` | one review; the tab is in `?tab=published` |
| `/admin/barbers` | barber queue: pending / all / hidden, search by name or email |
| `/admin/barbers/{id}` | one barber: contacts, readiness checklist, verify/grade/hide |

The customer cabinet and the admin section are client-side gated; the API enforces roles
separately (`AdminOnly`).

## Barber search

The landing calls `GET /api/barbers?city=&date=&cursor=&pageSize=` (anonymous, 120 requests
per minute per IP). The v1 filters are a **city** (trimmed and compared exactly, case-sensitive)
and an optional **date** — the barber has a working day on that weekday. Results are ordered
`rating DESC NULLS LAST, id`; `pageSize` is 12 and the next page is fetched with the opaque
`nextCursor` from the previous response. There is no text search, and the card shows no price
or service tags because the search DTO has neither.

Specialization and service filters are deliberately not in the UI yet: `specializationId` and
`hairServiceId` are accepted by the API, but `GET /api/specializations` and
`GET /api/hair-services` do not exist, so there is no list to populate a filter from. They
will follow once those read endpoints appear.

- `src/lib/barber-search.ts` — filters, `BarberSearchItem`, `searchBarbers()`.
- `src/hooks/use-barber-search.ts` — pages, abort of stale requests, retry, load-more state.
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
more than one salon. Slots are non-clickable `<time>` chips shown in the salon time zone
(`timeZoneId`), never recomputed into the browser zone. A day off, a working day without slots,
loading, a retryable network/`429`/`5xx` error, `404` (profile/salon/service) and `409` (broken
salon time zone) each have their own Russian state; the previous request is aborted when the
parameters change. There is **no** booking form yet, and no slots cache: slots change with
bookings and time off.

- `src/pages/barber-page.tsx` — document title, back link, states and section composition.
- `src/components/barber/` — hero, slots, about, services, specializations, salons, state views.
- `src/hooks/use-barber-profile.ts` — cache-first load, abort on id change/unmount, retry.
- `src/hooks/use-barber-slots.ts` — one slots request per parameter change, abort + generation, retry.
- `src/lib/barber-path.ts`, `src/lib/catalog.ts` — route parsing and the shared profile cache.
- `src/lib/barber-slots.ts` — slots DTO, `fetchBarberSlots`, salon-local date helpers.
- `src/lib/barber-profile-labels.ts` — Russian labels for categories, languages, payment and days.

Search cards on the landing are real links, so middle click and «открыть в новой вкладке» work.
There is **no** booking button and no review list yet: booking is the next slice. Services carry
`barberServiceId` (for the future `POST /api/bookings`) and `hairServiceId` (sent to `/slots`).

## API proxy

In development Vite proxies `/api` to `http://localhost:5243` (the backend `http` launch
profile). Point it elsewhere without touching the config:

```bash
VITE_API_PROXY_TARGET=https://localhost:7175 npm run dev
```

## Authentication

The header button signs in through the browser (cookie) branch of the API:
`GET /api/auth/csrf` → `POST /api/auth/login/cookie` → `GET /api/auth/me`.
Tokens never reach JavaScript: the session cookie and the refresh cookie are both `HttpOnly`.
Every state-changing request carries `X-CSRF-TOKEN` from the CSRF endpoint: auth, booking,
review and admin publication mutations use the helper in `src/lib/csrf.ts`.
`RememberMe` only tells the API whether the session cookie survives a browser restart — the
session itself lives for 8 hours of inactivity.

## Admin reviews

- `src/pages/admin-page.tsx` — route parsing, role gates, shell.
- `src/components/admin/` — rail, tabs, queue, detail, rules dialog, states.
- `src/lib/admin-reviews.ts` — `GET /api/admin/reviews`, `GET /api/admin/reviews/{id}`,
  `POST/DELETE /api/reviews/{id}/publication`.
- `src/lib/admin-path.ts` — `/admin` paths and the selected review in the URL.

The screen never shows a total count (the API does not provide one), never edits the client's
text, and never offers bulk publication.

## Admin barbers

- `src/components/admin/barbers-screen.tsx` — filter, debounced search, split view, states.
- `src/components/admin/barber-list.tsx`, `barber-detail.tsx` — table/cards and the card with
  contacts, the readiness checklist and the actions.
- `src/lib/admin-barbers.ts` — `GET /api/admin/barbers[?status&search]`,
  `GET /api/admin/barbers/{id}`, verification/grade/activation calls.
- `src/hooks/use-admin-barber-list.ts`, `use-admin-barber.ts` — list and card state.

«Скрыть из поиска» reuses the existing activation flag — there is no separate hidden state in
the domain. Confirming a hidden profile is rejected by the domain, so the button explains that
the profile must be returned to search first.

## Layout

- `AGENT.md` — durable frontend rules and reference for AI agents; read it before changing the
  project
- `src/App.tsx` — shell: header, page switch, footer
- `src/pages/` — `home-page`, `account-page`, `admin-page`, `barber-page`
- `src/components/ui` — shadcn/ui components, add more with `npx shadcn@latest add <name>`
- `src/lib/utils.ts` — `cn` helper
- `components.json` — shadcn/ui config and aliases (`@/`)
- `ADMIN_PAGE_HANDOFF.md` — admin design concept and stage plan

## Agent skills

Project skills live in `../.agents/skills` (React, TypeScript, Tailwind, shadcn, Playwright).
Update them with `npx skills check` / `npx skills update` from the repository root.
