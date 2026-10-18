import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { AuthContext, type AuthContextValue, type User } from './auth-context'
import { RequireAuth } from './RequireAuth'

/**
 * The route guard is the only thing standing between an unauthenticated visitor and a
 * protected page, so these tests are about what happens when it is *wrong*, not about the
 * happy path looking nice.
 */

const noop = async (): Promise<void> => undefined

function authValue(overrides: Partial<AuthContextValue> = {}): AuthContextValue {
  return {
    user: null,
    isLoading: false,
    login: noop,
    register: noop,
    logout: noop,
    ...overrides,
  }
}

const user: User = { id: 'u1', email: 'a@example.test', displayName: null, roles: ['user'] }

function renderAt(path: string, value: AuthContextValue) {
  return render(
    <AuthContext.Provider value={value}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route element={<RequireAuth />}>
            <Route path="/dashboard" element={<div>dashboard content</div>} />
          </Route>
          <Route path="/login" element={<div>login page</div>} />
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

describe('RequireAuth', () => {
  it('sends an unauthenticated visitor to the login page', () => {
    renderAt('/dashboard', authValue({ user: null }))

    expect(screen.getByText('login page')).toBeInTheDocument()
    // The protected content must not merely be hidden; it must not render at all.
    expect(screen.queryByText('dashboard content')).not.toBeInTheDocument()
  })

  it('renders the protected page for a signed-in user', () => {
    renderAt('/dashboard', authValue({ user }))

    expect(screen.getByText('dashboard content')).toBeInTheDocument()
  })

  it('shows a loading state instead of redirecting while the session is being resolved', () => {
    // Redirecting during load is the classic bug: a refresh on a protected page bounces
    // the user to login even though they have a valid session cookie.
    renderAt('/dashboard', authValue({ user: null, isLoading: true }))

    expect(screen.getByText(/loading/i)).toBeInTheDocument()
    expect(screen.queryByText('login page')).not.toBeInTheDocument()
  })

  it('waits rather than redirecting when the user is only slow to arrive', () => {
    renderAt('/dashboard', authValue({ user, isLoading: true }))

    expect(screen.getByText(/loading/i)).toBeInTheDocument()
    expect(screen.queryByText('dashboard content')).not.toBeInTheDocument()
  })
})
