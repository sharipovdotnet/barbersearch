# BarberSearch

Barber discovery and online booking: an ASP.NET Core API and a React single-page app in one
repository.

## Layout

```
backend/    .NET 10 solution — DDD domain, EF Core + PostgreSQL, cookie + JWT auth
frontend/   React 19 + TypeScript + Vite + Tailwind CSS v4 + shadcn/ui
.agents/    agent skills shared by both projects
.github/    CI workflows
```

## Prerequisites

- .NET SDK 10.0.303 (pinned by `backend/global.json`)
- Node.js 24 and npm
- Docker Desktop — PostgreSQL for local runs, Testcontainers for integration tests

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

## CI

- `.github/workflows/backend-ci.yml` — solution build plus unit and integration tests; runs on
  `backend/**` changes.
- `.github/workflows/frontend-ci.yml` — `npm ci`, lint and production build; runs on
  `frontend/**` changes.

Both workflows also support manual runs via `workflow_dispatch`.

## Conventions

- Branches: `features/issue-N`, merged into `main` through PRs.
- Domain terminology: `GLOSSARY.md`.
- Agent skills live in `.agents/skills` at the repository root — DSH and other agents discover
  them from the `.git` root, so do not move that directory into a subproject
  (see `backend/AGENT.md` §14).
