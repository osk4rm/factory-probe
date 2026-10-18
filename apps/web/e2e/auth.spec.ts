import { expect, test } from '@playwright/test'

/**
 * The whole product, once, as a user experiences it.
 *
 * These are the assertions that cannot be satisfied by unit tests: a cookie set by the API
 * is accepted by the browser, the CSRF token the client fetches is honoured by the server,
 * and a redirect actually changes the page.
 */

const password = 'Sup3rSecret!'

function uniqueEmail(): string {
  return `e2e-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.test`
}

test('a visitor without a session is sent to the login page', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('heading', { name: /sign in|login/i })).toBeVisible()
  // The dashboard must not be reachable without a session.
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toHaveCount(0)
})

test('a new user can register, land on the dashboard, and sign out', async ({ page }) => {
  const email = uniqueEmail()

  await page.goto('/register')
  await page.getByLabel(/email/i).fill(email)
  await page.getByLabel(/password/i).fill(password)
  await page.getByRole('button', { name: /create account/i }).click()

  // Registering signs the user in, so the dashboard is the destination.
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
  await expect(page.getByText(email)).toBeVisible()

  await page.getByRole('button', { name: /sign out/i }).click()
  await expect(page.getByRole('heading', { name: /sign in|login/i })).toBeVisible()
})

test('a registered user can sign in again in a fresh session', async ({ page, context }) => {
  const email = uniqueEmail()

  await page.goto('/register')
  await page.getByLabel(/email/i).fill(email)
  await page.getByLabel(/password/i).fill(password)
  await page.getByRole('button', { name: /create account/i }).click()
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()

  // A new context has no cookies: this is the sign-in path, not a cached session.
  const fresh = await context.browser()!.newContext()
  const freshPage = await fresh.newPage()
  await freshPage.goto('http://localhost:5173/login')
  await freshPage.getByLabel(/email/i).fill(email)
  await freshPage.getByLabel(/password/i).fill(password)
  await freshPage.getByRole('button', { name: /sign in/i }).click()

  await expect(freshPage.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
  await fresh.close()
})

test('a wrong password is refused and does not create a session', async ({ page }) => {
  const email = uniqueEmail()

  await page.goto('/register')
  await page.getByLabel(/email/i).fill(email)
  await page.getByLabel(/password/i).fill(password)
  await page.getByRole('button', { name: /create account/i }).click()
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
  await page.getByRole('button', { name: /sign out/i }).click()

  await page.getByLabel(/email/i).fill(email)
  await page.getByLabel(/password/i).fill('WrongPassword!')
  await page.getByRole('button', { name: /sign in/i }).click()

  await expect(page.getByRole('heading', { name: 'Dashboard' })).toHaveCount(0)
})
