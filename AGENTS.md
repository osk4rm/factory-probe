# AGENTS.md — how agents work in this repo

This repository is built by the **Swarm HQ** agent swarm. Read this before any
non-trivial change.

## The pipeline

`ai:implement → ai:review → ai:verify → [ai:e2e] → ai:ready`, with failures going
to `ai:changes` and escalations to `ai:blocked`. Agents **never** move `ai:*`
labels — the orchestrator owns every transition.

## Commands (single source of truth)

| Task | Command |
|---|---|
| Build | `pwsh -File scripts/ci/build.ps1` |
| Lint | `pwsh -File scripts/ci/lint.ps1` |
| Test | `pwsh -File scripts/ci/test.ps1` |
| API dev | `dotnet run --project apps/api` |
| Web dev | `npm --prefix apps/web run dev` |
| Stack | `docker compose up` |

CI and the swarm both call these scripts, so keep them honest.

## Conventions

- **Backend**: TypeScript-like strictness — nullable enabled, warnings are errors.
  Controllers stay thin; domain logic lives in services. Cancellation tokens flow
  through async calls. No secrets in code or logs.
- **Auth**: HttpOnly cookie sessions. Never store tokens in `localStorage`. Any
  state-changing endpoint must validate the antiforgery token (`X-CSRF-TOKEN`).
- **Frontend**: function components + hooks, TanStack Query for server state,
  Tailwind utilities, `@/` path alias. Access the API only through `src/lib/api.ts`.
- **Data**: every schema change ships an EF Core migration.
- **Tests**: new behaviour gets a test. Integration tests use Testcontainers; the
  web uses Vitest + Testing Library. Never weaken or delete an existing test to
  make a change pass.
- Conventional commits (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`).

## Definition of done

All five must pass. A change that only satisfies the first two is not done.

1. `scripts/ci/build.ps1`, `lint.ps1`, `test.ps1` all pass.
2. `npm --prefix apps/web test` — component tests.
3. `npm --prefix apps/web run test:e2e` — when the change touches auth, routing, or anything
   a user clicks. If it does not, say so in the PR rather than leaving it implied.
4. The four static gates pass: `migrations.ps1`, `check-compose.ps1`, `check-dockerfile.ps1`,
   and the build itself treats warnings as errors.
5. Acceptance criteria of the linked issue are demonstrably met, and no unrelated changes are
   included.

### Things that will get a change rejected

- **Disabling antiforgery**, widening CORS, or setting `Cookie.SecurePolicy` to `None`. If a
  test needs it, the test is wrong.
- **A migration that breaks the previous version.** Expand/contract: add, backfill, then
  remove in a later release. The gate will catch it; do not bypass it with
  `// swarm:destructive-ok` without saying why in the commit message.
- **Storing a secret in configuration with a default.** If it has a fallback value, it is
  public.
- **Adding a vendor SDK to domain code.** Analytics, email and billing all have seams;
  implement the interface instead.
- **A test that cannot fail.** If removing the line you just wrote does not turn it red, it is
  documentation, not a test.
