# Runbook

What to do when a product built from this template misbehaves. Every entry is written to be
followed at 3 a.m. by someone who did not write the code.

## First: which question are you answering?

| Symptom | Section |
|---|---|
| "The site is down" | [The site is down](#the-site-is-down) |
| "Sign-in stopped working" | [Sign-in is broken](#sign-in-is-broken) |
| "Customers say they were charged but have no access" | [Billing](#billing) |
| "The database is slow / the app is timing out" | [Database pressure](#database-pressure) |
| "A deploy went wrong" | [Rolling back](#rolling-back) |
| "Nobody knows what changed" | [Where the truth is](#where-the-truth-is) |

---

## The site is down

**1. Is it the process or the database?** Ask the two health endpoints:

```bash
curl -fsS https://<host>/health/live    # process is up
curl -fsS https://<host>/health/ready   # process is up AND the database answers
```

| `/live` | `/ready` | Meaning |
|---|---|---|
| fails | — | the process is gone; look at container logs |
| ok | fails | **the database is unreachable** — this is not an application bug |
| ok | ok | the process is healthy; the problem is between the user and the app (proxy, DNS, TLS) |

This distinction is the whole reason the endpoints are separate: a database outage must not
cause an orchestrator to restart a healthy process.

**2. If the process is gone**, read the last 100 lines before the exit:

```bash
docker compose -f docker-compose.prod.yml logs --tail=100 api
```

A startup failure is almost always configuration, and the application now refuses to start
rather than starting wrong: CORS origins, the OTLP endpoint, and a missing
`Email:PublicBaseUrl` all fail at boot. The message names the setting.

## Sign-in is broken

Three causes, in the order they actually occur:

1. **The CSRF token is stale.** Every unsafe request needs `X-CSRF-TOKEN`, and the token is
   rotated by sign-in. A client that fetches it once per session fails on the *first* POST
   after signing in and then works. The shipped client retries once automatically; a hand-
   written integration will not.
2. **Secure cookies on HTTP.** `Cookie.SecurePolicy` is `Always` in Production and
   `SameAsRequest` everywhere else. Serving Production over plain HTTP means the browser
   never sends the session back and every request looks unauthenticated. Terminate TLS in
   front of the app.
3. **Antiforgery cookie rejected.** The cookie is `SameSite=Strict`; a login form on a
   different origin will not work, by design.

**Do not "fix" sign-in by disabling antiforgery.** The realistic risk is not an attacker,
it's a support queue full of users whose accounts were changed by a page they visited.

## Billing

**A customer says they paid and has no access.**

1. Read our row: `select * from "Subscriptions" where "UserId" = '<id>';`
2. Compare with the provider's dashboard for that customer.
3. If the provider is right and we are wrong, the webhook never arrived:

```bash
docker compose -f docker-compose.prod.yml logs --tail=200 api | grep -i webhook
```

- `invalid_signature` → the `Billing:WebhookSecret` in the environment does not match the
  endpoint's secret. Fix the environment; **do not** disable verification.
- `no local user` → the event carried no `client_reference_id`. Either the checkout was
  created by something other than this API, or the customer's account was deleted.
- nothing at all → the provider is not delivering to `/api/billing/webhook`. Check the
  endpoint URL in the provider's dashboard and that the path is reachable.

**Replaying is safe.** The handler is idempotent by `ProviderSubscriptionId`, so redelivering
an event cannot create a second row. That is the reason it is safe to retry rather than to
guess.

## Database pressure

`/health/ready` failing with a timeout, or requests slow across the board:

```sql
-- who is holding locks
select pid, state, wait_event_type, left(query, 80) as query, now() - query_start as age
from pg_stat_activity where state <> 'idle' order by age desc limit 10;
```

Long-running queries from this application are usually a missing index on a new query, not a
database problem. Note the query, kill nothing, and look at it after service is restored.

## Rolling back

**The image tag is the rollback.** Production runs pre-built images, so a rollback is:

```bash
IMAGE_TAG=<previous> docker compose -f docker-compose.prod.yml up -d api web
```

**The database does not roll back, and does not need to.** Migrations are gated as
expand/contract: a migration that would break the *previous* version fails CI
(`scripts/ci/migrations.ps1`). That is why rolling back an image is a legitimate operation
rather than a gamble.

If a migration was destructive despite the gate, the gate was bypassed with a
`// swarm:destructive-ok` comment. Read the commit that added it before touching the data.

## Where the truth is

| Question | Source |
|---|---|
| What is running? | the image tag in the deploy command, not the repository |
| What changed? | `CHANGELOG.md` for the template, git history for the product |
| What should it do? | the tests — they are the executable specification |
| What is on? | `/health/config` lists the *names* of live feature flags, never values of secrets |
| Is anything expiring? | the provider's dashboard for certificates and domains; this app cannot know |

## Escalation

If the answer is not in this runbook, that is a documentation defect as much as an incident.
The order that works: **restore service first** (roll back the image), then find the cause,
then write down what you learned here.
