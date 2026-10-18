# Swarm App Template

A production-minded starter for building products with the **Swarm HQ** agent swarm:
**.NET 10 API + React (Vite) + PostgreSQL**, containerized, with authentication and
observability wired in from day one.

Use this as a GitHub template repository for every new app.

## Stack

| Layer | Choice |
|---|---|
| API | ASP.NET Core 10, Controllers, OpenAPI (Scalar), ProblemDetails |
| Auth | ASP.NET Identity + HttpOnly cookie sessions + CSRF |
| Data | EF Core 10 + Npgsql, migrations, PostgreSQL 17 |
| Web | React 19, Vite 6, TypeScript, Tailwind 4, TanStack Query, React Router |
| Observability | Serilog + OpenTelemetry (OTLP) → Collector, Prometheus, Grafana, Loki, Tempo |
| CI | GitHub Actions (build, lint, test) |
| Agents | Swarm HQ local dispatcher (`swarm.mode: hybrid`) |

## Layout

```
apps/api/     .NET 10 Web API
apps/web/     React SPA
tests/api/    xUnit + Testcontainers integration tests
tests/e2e/    Playwright (added by the swarm)
infra/        Observability configuration
scripts/ci/   build / lint / test used by CI and the swarm
docs/         feature tracker, glossary, ADRs
.agentswarm/  Swarm HQ profile
```

## Run locally

Prerequisites: .NET 10 SDK, Node 20.19+ (22 LTS recommended), Docker Desktop.

```bash
# 1. Postgres (and the full stack)
docker compose up --build            # api :5080, web :5173, postgres :5432

# 2. API only, for a fast inner loop
dotnet run --project apps/api        # http://localhost:5080  (Scalar at /scalar)
# API auto-migrates in Development

# 3. Web dev server (proxies /api to :5080)
cd apps/web && npm install && npm run dev   # http://localhost:5173

# 4. Observability stack
docker compose --profile obs up      # Grafana :3000 (admin/admin), Prometheus :9090
```

## What is already built

Everything below is tested and in `v1.0.0`. Anything not listed is not built — see the
changelog's *Known gaps*.

| Area | What exists |
|---|---|
| **Auth** | cookie Identity + antiforgery, register, login, logout, **email confirmation**, **password reset**, enumeration-resistant responses |
| **Billing** | hosted **checkout** and **portal**, **webhook** trusted only by signature, subscription cache, **server-side plan gate** |
| **Ops** | separated `/health/live` and `/health/ready`, `/health/config`, structured logs, OTLP traces, hardened images, **production compose** |
| **Safety** | startup config validation, expand/contract migration gate, container and compose gates |
| **Foundations** | email provider seam, vendor-neutral analytics with a PII guard, feature flags that fail closed |

## Verify

Four levels, each catching what the one before it cannot:

```pwsh
pwsh -File scripts/ci/build.ps1      # compile, warnings are errors
pwsh -File scripts/ci/lint.ps1
pwsh -File scripts/ci/test.ps1       # 87 API integration tests
npm --prefix apps/web test           # 13 component tests
```

End-to-end, against a real browser, real API and real PostgreSQL:

```pwsh
npm --prefix apps/web run test:e2e
```

The integration and end-to-end suites need a PostgreSQL. They take it from the environment
when `ConnectionStrings__Default` is set (the .NET fixture also accepts `FACTORY_TEST_DB`),
and fall back to Testcontainers — so **they run on a machine with no Docker daemon**:

```pwsh
node tools/with-postgres.mjs npm --prefix apps/web run test:e2e
```

## Deploying

`docker-compose.prod.yml` is the production shape: no build context (images come from CI), no
published database port, required credential placeholders, read-only containers. Its rules are
enforced by `scripts/ci/check-compose.ps1`, which fails the development file on five separate
security properties.

```pwsh
docker compose -f docker-compose.prod.yml --env-file .env up -d
```

## When it breaks

[`docs/runbook.md`](docs/runbook.md). It starts by asking which question you are answering,
because "the site is down" and "the database is down" need different people.

## Connect the swarm (Swarm HQ)

1. In Swarm HQ: **Add repository** → `osk4rm/swarm-app-template`.
2. **Detect** the profile, set **Swarm mode = hybrid**, then **Ensure labels**.
3. **Install** (merges the `ai/setup` PR). The dispatcher then runs agents locally.
4. Approve a proposal with `ai:implement` and watch the ping-pong.

The generated CI must be green before a review gate promotes, so keep
`scripts/ci/*` as the single source of build/lint/test truth.
