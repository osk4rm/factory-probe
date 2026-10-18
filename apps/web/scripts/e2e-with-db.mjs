import { spawn } from 'node:child_process'
import { rmSync } from 'node:fs'
import path from 'node:path'
import EmbeddedPostgres from 'embedded-postgres'

/**
 * Runs the end-to-end suite against a real PostgreSQL, with no Docker and no setup.
 *
 * The product being tested is a web application whose most interesting behaviour lives in
 * the interaction between a browser, an API and a database. That interaction is exactly what
 * a host without a Docker daemon cannot test — so rather than making end-to-end coverage
 * conditional on infrastructure, this starts a real PostgreSQL as an ordinary user process
 * and hands its connection string to both the API and the Playwright run.
 *
 * If `ConnectionStrings__Default` is already set, that database is used and nothing is
 * started: CI should point at its own service, and a developer debugging a specific database
 * should not have it swapped underneath them.
 */
const provided = process.env.ConnectionStrings__Default
if (provided) {
  console.log('[e2e] using the database from ConnectionStrings__Default')
  process.exit(await run())
}

const port = Number(process.env.SWARM_E2E_PG_PORT ?? 55441)
const dataDir = path.join('e2e-data', 'pg')

// A directory left behind by a killed run makes initdb refuse to start, and the error reads
// like a code defect rather than stale state.
rmSync(dataDir, { recursive: true, force: true })

const database = new EmbeddedPostgres({
  databaseDir: dataDir,
  user: 'e2e',
  password: 'e2e',
  port,
  persistent: false,
})

console.log('[e2e] starting a PostgreSQL for the run…')
await database.initialise()
await database.start()
console.log(`[e2e] PostgreSQL ready on port ${port}`)

const code = await run({
  ConnectionStrings__Default: `Host=127.0.0.1;Port=${port};Database=postgres;Username=e2e;Password=e2e`,
})

await database.stop()
rmSync(dataDir, { recursive: true, force: true })
process.exit(code)

/** Runs Playwright and resolves with its exit code. */
function run(extraEnv = {}) {
  const cli = path.join('node_modules', '@playwright', 'test', 'cli.js')
  const child = spawn(process.execPath, [cli, 'test', ...process.argv.slice(2)], {
    stdio: 'inherit',
    env: { ...process.env, ...extraEnv },
  })

  return new Promise((resolve) => child.on('exit', (code) => resolve(code ?? 1)))
}
