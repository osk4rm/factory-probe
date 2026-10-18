import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

// The client caches the CSRF token in module state, so each test gets a fresh module or it
// inherits the previous test's token and proves nothing about the fetch.
let apiFetch: typeof import('./api').apiFetch
let ApiError: typeof import('./api').ApiError

/**
 * The CSRF handling in this client is the difference between "the app works" and "the app
 * fails once after every sign-in", because signing in rotates the antiforgery token. These
 * tests pin that behaviour, and they exist because the same bug appeared in the backend
 * tests and cost two rounds to find.
 */

interface Call {
  url: string
  headers: Record<string, string>
}

let calls: Call[] = []
let csrfFetches = 0

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/** Installs a fetch whose behaviour is scripted per call. */
function scriptFetch(handler: (call: Call, index: number) => Response | Promise<Response>): void {
  calls = []
  csrfFetches = 0

  vi.stubGlobal('fetch', async (url: string, init: RequestInit = {}) => {
    const call: Call = { url, headers: (init.headers ?? {}) as Record<string, string> }
    calls.push(call)

    if (url === '/api/auth/csrf') {
      csrfFetches += 1
      return jsonResponse({ token: `token-${csrfFetches}` })
    }

    return handler(call, calls.length) as Response
  })
}

beforeEach(async () => {
  calls = []
  csrfFetches = 0
  vi.resetModules()
  const module = await import('./api')
  apiFetch = module.apiFetch
  ApiError = module.ApiError
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('apiFetch', () => {
  it('sends the CSRF token on an unsafe method', async () => {
    scriptFetch(() => jsonResponse({ ok: true }))

    await apiFetch('/api/things', { method: 'POST', body: { a: 1 } })

    const post = calls.find((call) => call.url === '/api/things')
    expect(post?.headers['X-CSRF-TOKEN']).toBe('token-1')
    expect(post?.headers['Content-Type']).toBe('application/json')
  })

  it('does not fetch a token for a safe method', async () => {
    scriptFetch(() => jsonResponse({ ok: true }))

    await apiFetch('/api/things')

    expect(csrfFetches).toBe(0)
    expect(calls[0]?.headers['X-CSRF-TOKEN']).toBeUndefined()
  })

  it('retries once with a fresh token when the cached one is rejected', async () => {
    // Sign-in rotates the token: the first POST is refused, the second must succeed.
    scriptFetch((_call, index) => (index === 2 ? new Response(null, { status: 400 }) : jsonResponse({ ok: true })))

    const result = await apiFetch<{ ok: boolean }>('/api/things', { method: 'POST', body: {} })

    expect(result.ok).toBe(true)
    expect(csrfFetches).toBe(2)
    const posts = calls.filter((call) => call.url === '/api/things')
    expect(posts).toHaveLength(2)
    expect(posts[1]?.headers['X-CSRF-TOKEN']).toBe('token-2')
  })

  it('does not retry forever: a second rejection surfaces as an error', async () => {
    scriptFetch(() => new Response(null, { status: 400 }))

    await expect(apiFetch('/api/things', { method: 'POST', body: {} })).rejects.toBeInstanceOf(ApiError)

    // Two attempts, not an unbounded loop.
    expect(calls.filter((call) => call.url === '/api/things')).toHaveLength(2)
  })

  it('does not retry a safe request', async () => {
    scriptFetch(() => new Response(null, { status: 400 }))

    await expect(apiFetch('/api/things')).rejects.toBeInstanceOf(ApiError)
    expect(calls.filter((call) => call.url === '/api/things')).toHaveLength(1)
  })

  it('returns undefined for 204 rather than trying to parse a body', async () => {
    scriptFetch(() => new Response(null, { status: 204 }))

    await expect(apiFetch('/api/things', { method: 'DELETE' })).resolves.toBeUndefined()
  })

  it('returns null for an allowed 401 so the session probe does not throw', async () => {
    scriptFetch(() => new Response(null, { status: 401 }))

    await expect(apiFetch('/api/auth/me', { allowUnauthorized: true })).resolves.toBeNull()
  })

  it('throws a typed error carrying the status and the problem details', async () => {
    scriptFetch(() => jsonResponse({ title: 'Bad', errors: { a: ['b'] } }, 422))

    await expect(apiFetch('/api/things', { method: 'POST', body: {} })).rejects.toMatchObject({
      status: 422,
      name: 'ApiError',
    })
  })
})
