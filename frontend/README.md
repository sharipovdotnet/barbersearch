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
npm run dev      # http://localhost:5173
npm run build    # tsc -b && vite build
npm run lint     # oxlint
npm run preview  # serve dist/
```

## API proxy

In development Vite proxies `/api` to `http://localhost:5243` (the backend `http` launch
profile). Point it elsewhere without touching the config:

```bash
VITE_API_PROXY_TARGET=https://localhost:7175 npm run dev
```

## Authentication

The header button signs in through the browser (cookie) branch of the API:
`GET /api/auth/csrf` → `POST /api/auth/login/cookie` → `GET /api/auth/me`.
Tokens never reach JavaScript: the session cookie and the refresh cookie are both `HttpOnly`,
and every state-changing auth request carries the `X-CSRF-TOKEN` header from the CSRF endpoint.
`RememberMe` only tells the API whether the session cookie survives a browser restart — the
session itself lives for 8 hours of inactivity.

- `src/lib/api.ts` — fetch wrapper and `ApiError` over ProblemDetails (`errorCodes`)
- `src/lib/auth.ts` — CSRF token, sign in/out, current user
- `src/hooks/use-session.ts`, `src/components/session-provider.tsx` — session state for the tree
- `src/components/header-auth.tsx` — «Войти» or the signed-in profile in the header
- `src/components/login-dialog.tsx` — login dialog, validation and server error mapping

## Layout

- `AGENT.md` — durable frontend rules and reference for AI agents; read it before changing the
  project
- `src/App.tsx` — landing page (barber cards are still demo data; only auth talks to the API)
- `src/components/ui` — shadcn/ui components, add more with `npx shadcn@latest add <name>`
- `src/lib/utils.ts` — `cn` helper
- `components.json` — shadcn/ui config and aliases (`@/`)

## Agent skills

Project skills live in `../.agents/skills` (React, TypeScript, Tailwind, shadcn, Playwright).
Update them with `npx skills check` / `npx skills update` from the repository root.
