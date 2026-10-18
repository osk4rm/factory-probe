import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end tests against the real application: real .NET API, real PostgreSQL, real
 * browser, real cookies.
 *
 * The database comes from the environment (`ConnectionStrings__Default`), the same way the
 * .NET integration fixture takes it. That keeps this runnable on a host with no Docker
 * daemon - the API is started as an ordinary process and the database is an ordinary
 * process too - instead of making E2E conditional on infrastructure.
 *
 * `webServer` starts both halves and waits for them to answer before the first test, so a
 * failure here is a broken application rather than a race against startup.
 */
export default defineConfig({
  testDir: './e2e',
  // One worker: every test creates a user and shares one database, so parallelism would
  // buy seconds and cost determinism.
  workers: 1,
  fullyParallel: false,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [['list']],
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'dotnet run --project ../../apps/api --urls http://localhost:5080 --no-launch-profile',
      url: 'http://localhost:5080/health/live',
      reuseExistingServer: false,
      timeout: 240_000,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        Database__MigrateOnStartup: 'true',
        Email__Provider: 'capture',
        Email__PublicBaseUrl: 'http://localhost:5173',
        Billing__WebhookSecret: 'whsec_e2e',
      },
    },
    {
      command: 'npm run dev',
      url: 'http://localhost:5173',
      reuseExistingServer: false,
      timeout: 180_000,
    },
  ],
})
