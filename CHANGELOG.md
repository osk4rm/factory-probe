## 1.1.0

The swarm can now verify a product end to end instead of verifying its parts.

**End-to-end gate enabled.** `.agentswarm/profile.yml` moves from `e2e.mode: disabled` to
`mode: command`, running the committed Playwright suite. `disabled` meant the gate passed
without looking at the product; `agent` would cost a language model on every gate for a suite
that already exists and is deterministic.

**The e2e command is self-sufficient.** `npm --prefix apps/web run test:e2e` starts its own
PostgreSQL as an ordinary process, so the gate works on a host with no Docker daemon, and
uses `ConnectionStrings__Default` instead when CI provides one.

**Documentation a cold agent can build from.** `README.md` now states what is built and what
is not; `AGENTS.md` gains a definition of done with five required checks and a list of changes
that will be rejected; `docs/runbook.md` is new and starts by asking which question you are
answering, because "the site is down" and "the database is down" need different people.

### Fixed

- `labels.prefix` was unquoted (`ai:`), which is not valid YAML — the profile could not be
  parsed by `yaml` at all. Any consumer reading it as YAML failed.
# Changelog

The version in `TEMPLATE_VERSION` is what a product pins itself to. It changes when the
generated material changes in a way a downstream product must react to; it does not change
for internal refactors.

## 1.0.0

First version considered complete enough to build a product on without asking questions.

**Auth.** Cookie-based Identity with antiforgery on every unsafe method, email confirmation
and password reset, and enumeration-resistant responses: confirm and reset answer identically
for unknown accounts, and `forgot` always returns 204. A reset signs the user out.

**Billing.** Hosted checkout and portal behind a provider seam, webhook ingestion trusted only
by signature (constant-time, replay-windowed, rotation-aware), subscription state cached in a
table that is *not* the source of truth, and a server-side plan gate that fails closed.

**Operability.** `/health/live` and `/health/ready` separated so a database outage does not
look like a dead process; `/health/config` with an allow-list so it cannot leak; structured
logs and OTLP traces with a validated endpoint; hardened runtime images (non-root,
`HEALTHCHECK`, no SDK in the final stage).

**Safety rails.** CORS and telemetry validated at startup rather than at first request;
migrations gated as expand/contract so a deploy can be rolled back without a database
rollback; a production compose file that publishes no database port and requires real
credentials instead of shipping defaults.

**Foundations.** Vendor-neutral analytics with a deny-list that drops personal data in a
decorator, feature flags whose failure mode is off, and email behind a provider seam with a
capture implementation for tests.

**Tests.** 87 API tests, 13 frontend tests, 4 end-to-end tests against a real browser, a real
API and a real PostgreSQL. The integration and end-to-end suites run without Docker: the
database connection comes from the environment.

### Known gaps, stated rather than implied

- Real Stripe acceptance is unverified: the webhook, state machine and gate are tested, but
  no session has been created against Stripe test mode.
- No secret scanning or dependency scanning in CI yet.
- Branch protection is a repository setting, not something this template can ship.
