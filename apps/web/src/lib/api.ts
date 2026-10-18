const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

let csrfToken: string | null = null

export class ApiError extends Error {
  readonly status: number
  readonly details?: unknown

  constructor(status: number, message: string, details?: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.details = details
  }
}

export interface ApiFetchOptions {
  method?: string
  body?: unknown
  /** Return null instead of throwing on 401 (used to probe the session). */
  allowUnauthorized?: boolean
}

async function getCsrfToken(): Promise<string> {
  if (csrfToken) {
    return csrfToken
  }

  const response = await fetch('/api/auth/csrf', { credentials: 'include' })
  if (!response.ok) {
    throw new ApiError(response.status, 'Failed to fetch the CSRF token')
  }

  const data = (await response.json()) as { token: string }
  csrfToken = data.token
  return csrfToken
}

/**
 * Thin fetch wrapper: same-origin relative URLs, HttpOnly cookies, and the
 * X-CSRF-TOKEN header on unsafe methods.
 *
 * **The token is cached, and a rejected unsafe request is retried exactly once.** Signing
 * in rotates the antiforgery token, so the next POST carrying the cached one is refused —
 * the user would see an inexplicable failure and have to click again. One retry with a
 * freshly fetched token makes that invisible. It is capped at one so a genuinely invalid
 * request cannot loop.
 */
export async function apiFetch<T>(path: string, options: ApiFetchOptions = {}): Promise<T> {
  return request<T>(path, options, true)
}

async function request<T>(path: string, options: ApiFetchOptions, mayRetry: boolean): Promise<T> {
  const method = (options.method ?? 'GET').toUpperCase()
  const headers: Record<string, string> = { Accept: 'application/json' }

  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  const unsafe = UNSAFE_METHODS.has(method)
  if (unsafe) {
    headers['X-CSRF-TOKEN'] = await getCsrfToken()
  }

  const response = await fetch(path, {
    method,
    credentials: 'include',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  })

  if (response.status === 401 && options.allowUnauthorized) {
    return null as T
  }

  if (!response.ok) {
    // A refused unsafe request is usually a rotated token: clear it and try once more.
    if (mayRetry && unsafe && (response.status === 400 || response.status === 403)) {
      csrfToken = null
      return request<T>(path, options, false)
    }

    csrfToken = null
    let details: unknown
    try {
      details = await response.json()
    } catch {
      details = undefined
    }
    throw new ApiError(response.status, `Request failed with status ${response.status}`, details)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}
