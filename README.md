# BarberSearch

Barber discovery and online booking: an ASP.NET Core API and a React single-page app in one
repository.

## Layout

```
backend/    .NET 10 solution — DDD domain, EF Core + PostgreSQL, cookie + JWT auth
frontend/   React 19 + TypeScript + Vite + Tailwind CSS v4 + shadcn/ui
mobile/     .NET MAUI client for iOS and Android
.agents/    agent skills shared by both projects
.github/    CI workflows
```

## Prerequisites

- .NET SDK 10.0.303 (pinned by `backend/global.json` and `mobile/global.json`)
- Node.js 24 and npm
- Docker Desktop — PostgreSQL for local runs, Testcontainers for integration tests
- .NET MAUI workloads (`android`, `ios`) for the mobile app — installed with Visual Studio 18.8
  or by `dotnet workload install maui`

## Backend

```powershell
cd backend
Copy-Item .env.example .env    # fill POSTGRES_PASSWORD
docker compose up -d
dotnet run --project BarberSearch/src/BarberSearch.Web   # http://localhost:5243, Swagger UI at /swagger
```

Migrations, tests and the full command list: `backend/AGENT.md` §5.

## Frontend

```powershell
cd frontend
npm install
npm run dev      # http://localhost:5173
npm run build    # tsc -b && vite build
npm run lint     # oxlint
```

In development Vite proxies `/api` to `http://localhost:5243`. Point it elsewhere with
`VITE_API_PROXY_TARGET`. Details: `frontend/README.md`.

## Mobile

.NET MAUI client for iOS and Android — the customer side: search, the barber card, slots,
booking, favourites. The barber cabinet and the admin section stay on the web.

```powershell
cd mobile
dotnet test tests/BarberSearch.Mobile.Core.Tests
dotnet build src/BarberSearch.Mobile/BarberSearch.Mobile.csproj -f net10.0-android
```

API DTOs are shared with the backend by project reference (`BarberSearch.Contracts`), so a broken
contract fails the mobile build instead of the app at runtime. The Android emulator reaches the
local API at `10.0.2.2:5243`, the iOS simulator at `localhost:5243`. iOS builds need macOS with
Xcode 26. Details: `mobile/README.md` and `mobile/AGENT.md`.

## Places

A Barber receives Customers at a place (`Salon`) of one of five kinds: a barbershop, a
chair-rental studio, a solo cabinet, a home or a mobile visit. A barbershop is always listed in
the catalogue and goes through moderation; a cabinet can be listed by the Barber's choice, while
a home or a mobile place is never listed. An unlisted place skips moderation and is reachable
only from the Barber's profile, but it still has an address and a time zone, so the weekly
schedule, slots and online booking work exactly as in a barbershop. The barber cabinet calls this
«место приёма»: the barber picks the kind and whether the place is shown in the catalogue.

## CI

- `.github/workflows/backend-ci.yml` — solution build plus unit and integration tests; runs on
  `backend/**` changes.
- `.github/workflows/frontend-ci.yml` — `npm ci`, lint and production build; runs on
  `frontend/**` changes.
- `.github/workflows/mobile-ci.yml` — the mobile tests plus an Android build on `mobile/**`
  changes; the iOS build (compile only, simulator) runs on `macos-26` weekly and on demand.

All three workflows also support manual runs via `workflow_dispatch`.

## Conventions

- Branches: `features/issue-N`, merged into `main` through PRs.
- Domain terminology: `GLOSSARY.md`.
- Agent skills live in `.agents/skills` at the repository root — DSH and other agents discover
  them from the `.git` root, so do not move that directory into a subproject
  (see `backend/AGENT.md` §14).
