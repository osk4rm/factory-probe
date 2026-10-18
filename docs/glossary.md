# Glossary

- **Swarm HQ** — the control plane that installs and drives the agent swarm for
  this repository.
- **Gate** — a step in the pipeline that produces a verdict (review, verify, e2e).
- **Verdict** — `APPROVED` / `CHANGES_REQUESTED`, `TESTS_SOUND` /
  `TESTS_INSUFFICIENT`, `E2E_PASS` / `E2E_FAIL` / `E2E_BLOCKED`.
- **Dispatcher** — the local process that runs agents for `hybrid`/`local` repos.
- **Slice** — a vertical feature: data + API + UI + tests, end to end.
