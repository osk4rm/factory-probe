import { createContext, useContext } from 'react'

export interface User {
  id: string
  email: string
  displayName: string | null
  roles: string[]
}

export interface LoginInput {
  email: string
  password: string
}

export interface RegisterInput extends LoginInput {
  displayName?: string
}

export interface AuthContextValue {
  user: User | null
  isLoading: boolean
  login: (input: LoginInput) => Promise<void>
  register: (input: RegisterInput) => Promise<void>
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}
