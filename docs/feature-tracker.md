# Feature tracker

Single source of truth for what this product does. Only the **tracker** agent
writes here; humans and other agents read it. Status values: `done`,
`in-progress`, `proposed`, `gap`.

The target: a **modular-monolith SaaS base** where everything that can be is a
**config-selected provider with a zero-infrastructure default**. An implementer
should wire a provider by setting environment variables, then spend their time
only on features (domain + UI) — never on plumbing. Items marked `gap` are the
swarm's backlog; `done` is what the bootstrap already ships.

Conventions for every pluggable concern:
- a provider-neutral interface in a building block;
- one or more providers shipped in the template;
- selection + options read from configuration (env), validated at startup;
- a safe default that needs no external service (dev/tests).

## Configuration & pluggable providers

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| CFG-1 | Strongly-typed options records + `ValidateOnStart` (fail fast on bad config) | gap | | Every provider exposes an options record |
| CFG-2 | One config switch per concern selects the provider (mail/storage/auth/cache/broker/search/telemetry/jobs/limits) | gap | | e.g. `Mail:Provider=file|smtp|resend` |
| CFG-3 | Documented env switchboard + `.env.example` + redacted `/health/config` endpoint | gap | | Implementer only edits env |
| CFG-4 | Secrets providers: env, user-secrets, Azure Key Vault, HashiCorp Vault, AWS Secrets Manager | gap | | Layered `IConfiguration` |
| CFG-5 | Feature flags (OpenFeature-compatible) with local/env provider + per-tenant overrides | gap | | Swap to LaunchDarkly/etc. by config |

## Provider matrix (target; zero-infra default, alternatives via config)

| Concern | Default (no infra) | Alternatives selected by config |
|---|---|---|
| Mail | File sink | SMTP/Mailpit, Resend, SendGrid, Azure Communication Services |
| Storage | Local filesystem | S3/MinIO, Azure Blob, Google Cloud Storage |
| Cache | In-memory (`HybridCache`) | Redis, hybrid (memory + Redis) |
| Broker | In-process outbox | RabbitMQ, Azure Service Bus, Kafka |
| Search | PostgreSQL full-text | Meilisearch, Elasticsearch |
| Vector / embeddings | `pgvector` | none |
| Auth | ASP.NET Identity (cookie) | OIDC (Entra, Google, Auth0, Keycloak), SAML |
| Secrets | Env / user-secrets | Key Vault, Vault, AWS Secrets Manager |
| Telemetry | OTLP collector | Console, Azure Monitor, Grafana Cloud |
| Jobs / scheduler | In-process scheduler | Hangfire, Quartz |
| Rate limiting | In-memory | Redis-backed |
| Distributed lock | PostgreSQL advisory lock | Redis |
| Payments/billing | Disabled | Stripe |

## Architecture & building blocks

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| ARCH-1 | Modular monolith: `IModule` + module registry, per-module DbContext and PostgreSQL schema | gap | | Host composes modules; modules never reference each other's internals |
| ARCH-2 | SharedKernel: `Result`/`Error`, `Entity`/`AggregateRoot`, `ValueObject`, strongly-typed IDs | gap | | No exceptions for control flow |
| ARCH-3 | CQRS dispatcher + pipeline behaviors (validation, logging, transaction, tenant) | gap | | Avoid a commercial mediator; keep it dependency-light |
| ARCH-4 | Architecture boundary tests (modules may only talk via abstractions/events) | gap | | NetArchTest |
| ARCH-5 | Per-module EF migrations + migrate-all-on-startup + design-time factories | gap | | One physical DB, schema per module |

## Messaging

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| MSG-1 | Transactional outbox written atomically with the state change | gap | | Same DbContext/transaction |
| MSG-2 | Outbox processor background worker with retries + backoff | gap | | Batch, max attempts, error capture |
| MSG-3 | Integration event bus + in-process handlers + serializer | gap | | Pluggable transport |
| MSG-4 | Broker transport selectable by config (in-process / RabbitMQ / ASB / Kafka) + compose profile | gap | | Off by default |
| MSG-5 | Inbox / idempotent consumers | gap | | Required once a broker is enabled |

## Mailing & notifications

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| MAIL-1 | `IEmailSender` abstraction + file provider for dev/tests | gap | | Zero vendor lock-in |
| MAIL-2 | SMTP provider (MailKit) + Mailpit in compose | gap | | Default local provider |
| MAIL-3 | API providers (Resend / SendGrid / Azure ACS) with resilience, selected by config | gap | | Config-driven |
| MAIL-4 | Template rendering (Fluid) + outbox-backed sending with retries | gap | | Emails sent from a worker, not inline |
| NOT-1 | `INotificationSender` abstraction + channels: email, in-app, webhook, SMS (Twilio), push | gap | | Channel chosen by config/preferences |
| NOT-2 | Per-user / per-tenant notification preferences | gap | | Opt-outs |
| NOT-3 | Shared templating for mail + in-app + push | gap | | One template engine |

## Webhooks

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| HOOK-1 | Outbound webhook subscriptions + delivery with retry and HMAC signing | gap | | Uses the outbox |
| HOOK-2 | Inbound webhook receiver pattern with signature verification + idempotency | gap | | For provider callbacks (Stripe, etc.) |

## Identity & access

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| AUTH-1 | Email/password sign-in with HttpOnly cookie sessions | done | | Antiforgery on all mutations |
| AUTH-2 | Self-service registration | done | | Password min length 10 |
| AUTH-3 | Permission-based authorization (policies, not just roles) + endpoint metadata | gap | | Roles seeded today; no admin endpoints |
| AUTH-4 | Password reset and email confirmation | gap | | Needs the mail provider |
| AUTH-5 | API keys + OAuth2 client-credentials for machine-to-machine | gap | | For integrations |
| AUTH-6 | Session/refresh management + auth rate limiting + lockout metrics | gap | | |
| AUTH-7 | Pluggable auth mode: local Identity **or** external OIDC, selected by config, with JIT user provisioning | gap | | Entra/Google/Auth0/Keycloak |
| AUTH-8 | SAML / enterprise SSO per tenant | gap | | B2B customers |
| AUTH-9 | SCIM 2.0 user/group provisioning endpoint | gap | | Enterprise IT |
| AUTH-10 | MFA / TOTP + recovery codes | gap | | |
| AUTH-11 | Impersonation (admin-as-user) with audit | gap | | Support tooling |

## Multitenancy (SaaS)

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| TEN-1 | Tenants module: tenant catalog + memberships + plans | gap | | Users global, membership per tenant |
| TEN-2 | Tenant resolution (header / subdomain / JWT claim) + middleware | gap | | Sets `ITenantContext` |
| TEN-3 | Row-level isolation: global query filter + auto-stamp `TenantId` | gap | | Shared schema, EF filters |
| TEN-4 | Per-tenant settings, entitlements and quotas (plan-driven) | gap | | Feature flags per tenant |
| TEN-5 | Tenant invitations/onboarding via email | gap | | Prove the mail + outbox path |
| TEN-6 | Tenant id in logs, traces and audit | gap | | Enrichment |
| TEN-7 | Tenant branding / custom domain (optional) | gap | | Config per tenant |

## Storage & files

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| STOR-1 | `IFileStorage` abstraction + providers: local, S3/MinIO, Azure Blob, GCS | gap | | Config-selected |
| STOR-2 | Pre-signed upload/download URLs | gap | | Direct-to-blob |
| STOR-3 | Attachment/upload endpoints with size/type limits + optional antivirus hook | gap | | |

## Search & AI

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| SRCH-1 | `ISearchIndex` abstraction + providers: Postgres FTS, Meilisearch, Elasticsearch | gap | | Default: Postgres |
| SRCH-2 | Vector store abstraction on `pgvector` for embeddings / RAG | gap | | Optional |

## Data integrity & compliance-grade persistence

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| DATA-1 | Optimistic concurrency (`rowversion`/`xmin`) + ETag / `If-Match` helper | gap | | Prevents lost updates |
| DATA-2 | UUIDv7 identifiers + EF value converters | gap | | Index-friendly |
| DATA-3 | PII column encryption + persistent Data Protection key ring | gap | | Ring in DB/blob/Key Vault |
| DATA-4 | Migration CI gate + expand/contract guidance + seed/demo data provider | gap | | Zero-downtime |
| DATA-5 | Retention + cleanup jobs for outbox/audit; GDPR export/erase helpers | gap | | DSAR |
| DATA-6 | PII classification attributes + redaction in logs/traces | gap | | |

## Reliability & scale

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| SCALE-1 | Distributed lock abstraction (Postgres advisory default, Redis optional) + leader election for workers | gap | | Single outbox leader |
| SCALE-2 | Graceful shutdown + liveness/readiness/startup probes | gap | | |
| SCALE-3 | Cache abstraction (HybridCache + optional Redis) + invalidation conventions | gap | | Cache-aside helper |
| SCALE-4 | Load-test harness (k6/NBomber) + performance budgets | gap | | |
| SCALE-5 | DB transient-failure retry + optional read/write split | gap | | |

## Cross-cutting infrastructure

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| INFRA-1 | Background jobs abstraction (in-process scheduler, Hangfire/Quartz by config) | gap | | Recurring + delayed |
| INFRA-2 | Idempotency keys for mutating APIs (store + filter), in-memory/Redis/DB by config | gap | | |
| INFRA-3 | Audit log (who did what, tenant-aware, tamper-evident) | gap | | Distinct from Serilog |
| INFRA-4 | Outbound HTTP resilience (typed clients, retries, circuit breaker) | gap | | Polly via resilience handler |
| INFRA-5 | API versioning (URL/header) + deprecation policy | gap | | |
| INFRA-6 | Soft delete convention + restore | gap | | `ISoftDeletable` |
| INFRA-7 | Global rate limiting + request size/timeout limits | gap | | Provider by config |
| INFRA-8 | Response compression + output caching | gap | | |

## API governance

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| APIGOV-1 | `Result`→HTTP mapping + stable error codes in ProblemDetails | gap | | Consistent error contract |
| APIGOV-2 | Pagination / filtering / sorting conventions + response envelope | gap | | One shape everywhere |
| APIGOV-3 | OpenAPI → generated TypeScript client + contract tests | gap | | FE stays in sync |
| APIGOV-4 | Rate-limit headers + request/correlation id response headers | gap | | |

## Observability

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| OBS-1 | Traces, metrics and logs via OpenTelemetry | done | | OTLP → Collector |
| OBS-2 | Grafana dashboards: auth failures, latency, error rate | gap | | Provisioned |
| OBS-3 | Metrics for outbox/queue depth and processing lag | gap | | Custom meters |
| OBS-4 | Structured audit/security events | gap | | |
| OBS-5 | End-to-end correlation ids + PII redaction in logs | gap | | |
| OBS-6 | SLOs / alert rules as code | gap | | |
| OBS-7 | Frontend telemetry (Sentry / OTel web) | gap | | |

## Frontend foundation

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| FE-1 | Permission- and feature-flag-aware UI guards/hooks | gap | | Hides what the API forbids |
| FE-2 | i18n + timezone/currency formatting | gap | | |
| FE-3 | Global error boundary + error/toasts conventions | gap | | |
| FE-4 | Design system baseline (shadcn) + theming / dark mode | gap | | |
| FE-5 | Runtime env config (no rebuild per environment) + CSP-friendly | gap | | |
| FE-6 | Accessibility baseline + lint | gap | | |
| FE-7 | API client generated from OpenAPI (see APIGOV-3) | gap | | |
| FE-8 | Component-test conventions (Vitest + Testing Library) | gap | | |

## Reference & delivery

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| DOM-1 | Example domain module wiring the whole stack (CQRS + outbox + mail + tenant) | gap | | Pattern to copy |
| API-1 | Versioned, documented public API | done | | OpenAPI + Scalar |
| E2E-1 | Playwright smoke suite in CI | gap | | Baseline: login, navigation, no-console-errors |
| OPS-1 | One-command local stack | done | | docker compose |
| OPS-2 | Compose profiles for mailpit / redis / rabbitmq / minio | gap | | Opt-in infra |
| OPS-3 | Environments (dev/staging/prod) + IaC baseline (Bicep/Terraform) | gap | | |
| OPS-4 | Kubernetes/Helm manifests (or prod compose) + probes + resources | gap | | |
| OPS-5 | CI supply chain: dependency/SAST/container scanning, SBOM, image signing | gap | | |
| OPS-6 | Canary / blue-green + rollback; preview env per PR (optional) | gap | | |
| OPS-7 | Versioning, changelog and release automation | gap | | |
| OPS-8 | OIDC-federated CI credentials (no long-lived secrets) | gap | | |

## Developer experience

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| DX-1 | Code generation: `dotnet new` module/feature/endpoint templates | gap | | Copy-paste-free new features |
| DX-2 | Frontend feature generator (page + route + hook + api client + test) | gap | | Plop/hygen |
| DX-3 | Seed/demo data + local bootstrap script | gap | | |
| DX-4 | Pre-commit hooks + task runner (up/migrate/seed/build/lint/test/e2e) | gap | | lefthook/Husky.NET |
| DX-5 | Test suite wiring: architecture, contract, integration, load | gap | | |
| DX-6 | Roslyn analyzers + ruleset + format-on-build (Meziantou/StyleCop) | gap | | Strict by default |
| DX-7 | Dev container + `.vscode` launch/tasks for a reproducible env | gap | | |
| DX-8 | Branching, commit and release conventions (conventional commits + commitlint) | gap | | |
| DX-9 | Local overrides for providers/feature flags (dev without external infra) | gap | | |
| DX-10 | EditorConfig + consistent formatting across .NET/TS + markdown | gap | | |

## Documentation & decision records

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| DOC-1 | Docs-as-code site (DocFX/MkDocs) built and published in CI | gap | | |
| DOC-2 | Architecture doc + C4 context/container/component diagrams as code | gap | | Mermaid/D2 |
| DOC-3 | ADR process + template + seed ADRs (modular monolith, outbox, tenancy, providers, auth, AI) | gap | | Proposed/accepted/superseded |
| DOC-4 | Runbooks: deploy, rollback, DB migration, secret rotation, stuck outbox, tenant onboarding, incident | gap | | Ops-facing |
| DOC-5 | Standards: coding style, API design, error handling, testing strategy, security, accessibility | gap | | |
| DOC-6 | Onboarding guide: local setup, env switchboard, seed data, first task | gap | | |
| DOC-7 | API reference generated from OpenAPI + generated client docs | gap | | Ties APIGOV-3 |
| DOC-8 | Changelog + release notes automation | gap | | Ties OPS-7 |
| DOC-9 | Markdown lint + link check + spell check in CI | gap | | |
| DOC-10 | CONTRIBUTING, CODEOWNERS, SECURITY.md, CODE_OF_CONDUCT, LICENSE, privacy/DPA templates | gap | | |

## Skills & agent enablement

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| SK-1 | `module-authoring` skill: create a module with boundaries, DbContext, schema, migrations | gap | | Read by the swarm |
| SK-2 | `feature-slice` skill: command/query + validator + endpoint + tests | gap | | |
| SK-3 | `add-migration` skill: EF migration in the right module + format + verify | gap | | |
| SK-4 | `add-provider` skill: implement a new pluggable provider end-to-end (interface, options, config, default) | gap | | Core to the config-first goal |
| SK-5 | `rag` skill: ingestion/chunking/embeddings/retrieval on pgvector | gap | | |
| SK-6 | `security-review` skill: authz, tenancy isolation, secrets, injection, PII | gap | | For the reviewer/verifier agents |
| SK-7 | `adr-writer` + `docs-writer` skills | gap | | Keep decisions and docs current |
| SK-8 | Customized `.opencode/agent/*` subagents for .NET/React/db/AI | gap | | Beyond installer defaults |
| SK-9 | Living `AGENTS.md`: module map, commands, decision pointers, DoD | gap | | Bootstrap version exists |
| SK-10 | Issue/PR templates + conventions the agents follow when proposing work | gap | | Analyst/implementer inputs |

## Compliance

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| COMP-1 | DSAR: data export + right-to-erasure endpoints/helpers | gap | | GDPR |
| COMP-2 | Retention policies + data-residency notes | gap | | |
| COMP-3 | Consent tracking + security policy + licensing | gap | | |

## AI platform (config-first)

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| AI-1 | Provider-neutral `IChatClient` (chat, streaming, tool calling); providers: OpenAI, Azure OpenAI, Anthropic, Google, OpenRouter, Ollama | gap | | Selected by config; disabled default |
| AI-2 | `IEmbeddingGenerator` abstraction; providers: OpenAI, Azure, Ollama, local ONNX | gap | | Config-selected |
| AI-3 | Per-task model routing (task → model), fallbacks, timeouts and token budgets | gap | | e.g. `AI:Tasks:Summarize:Model` |
| AI-4 | Prompt registry with versioning + template storage | gap | | Same engine as mail templates |
| AI-5 | Structured outputs (JSON schema) validated into `Result`/ProblemDetails | gap | | No free-text parsing |
| AI-6 | Streaming to the SPA (SSE) + a React `useChat` hook | gap | | Cancellation-aware |

## RAG & knowledge

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| RAG-1 | Document ingestion pipeline (PDF/Word/HTML/MD → parse → chunk → embed → index) as a background job | gap | | Progress + retries |
| RAG-2 | Vector store abstraction on `pgvector`, tenant-scoped, with hybrid (BM25 + vector) search and reranking | gap | | Tenant filter on vectors is mandatory |
| RAG-3 | Retrieval + context assembly + citations/grounding | gap | | Answers must cite sources |
| RAG-4 | Per-tenant knowledge base (upload, sync, reindex) | gap | | Config-driven |

## Agents & conversations

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| AGENT-1 | Tool/function-calling registry + multi-step agent loop | gap | | Bounded steps/time |
| AGENT-2 | MCP (Model Context Protocol) **client**: consume MCP servers as tools | gap | | Config lists servers |
| AGENT-3 | MCP **server**: expose the app's tools/resources so external agents can drive it | gap | | AI-operable app |
| AGENT-4 | Conversation/thread store + history + summarization/compaction | gap | | Per user/tenant |
| AGENT-5 | Long-term memory abstraction (`pgvector`) per user/tenant | gap | | Opt-in |
| AGENT-6 | Long-running AI jobs: progress, cancellation, resumability | gap | | Uses background jobs |

## AI safety, governance & cost

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| AISEC-1 | Moderation / safety filters (provider) + topic policy guardrails | gap | | Config-selected |
| AISEC-2 | Prompt-injection defenses + PII redaction before any model call | gap | | Ties DATA-6 |
| AISEC-3 | Output guardrails: schema validation, allow-lists, refusal handling | gap | | |
| AISEC-4 | AI audit log: prompt, model, tokens, cost, tenant, user | gap | | |
| AISEC-5 | Per-tenant AI quotas/budgets + cost metering | gap | | Ties BILL-1, TEN-4 |
| AISEC-6 | Prompt/semantic cache to cut cost on repeated queries | gap | | Ties INFRA cache |

## AI observability & evaluation

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| AIOBS-1 | GenAI tracing via OpenTelemetry GenAI conventions (Langfuse/OpenLLMetry provider) | gap | | Config-selected |
| AIOBS-2 | Token / cost / latency dashboards per model, feature and tenant | gap | | |
| AIOBS-3 | Offline eval harness: golden sets, LLM-as-judge, regression gate in CI | gap | | |
| AIOBS-4 | Online feedback (thumbs) + A/B testing of prompts/models | gap | | |

## AI developer experience

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| AIDEV-1 | Ollama + models compose profile; local default needs no API keys | gap | | Zero-infra default |
| AIDEV-2 | Deterministic fake provider + recorded fixtures (cassettes) for tests/CI | gap | | No live calls in CI |
| AIDEV-3 | Documented `AI:` env switchboard (provider, keys, model names, budgets) | gap | | Template-only wiring |

## AI product features (config-enabled)

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| AIFEAT-1 | Chat assistant UI shell (streaming) behind a feature flag | gap | | |
| AIFEAT-2 | Semantic search over the app's own data | gap | | |
| AIFEAT-3 | Task library: summarize, classify, extract, translate helpers | gap | | Reusable commands |

## Optional product plugins (config-enabled)

| ID | Capability | Status | Work Item | Notes |
|---|---|---|---|---|
| BILL-1 | Billing abstraction (`IBillingProvider`) + Stripe provider + plans/quotas | gap | | Disabled by default |
| ANLY-1 | Product analytics abstraction (PostHog/Segment) | gap | | Off by default |
