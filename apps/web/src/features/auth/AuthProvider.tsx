import { useCallback, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '@/lib/api'
import {
  AuthContext,
  type LoginInput,
  type RegisterInput,
  type User,
} from './auth-context'

const ME_QUERY_KEY = ['me'] as const

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ME_QUERY_KEY,
    queryFn: () => apiFetch<User | null>('/api/auth/me', { allowUnauthorized: true }),
    retry: false,
    staleTime: 30_000,
  })

  const loginMutation = useMutation({
    mutationFn: (input: LoginInput) =>
      apiFetch<User>('/api/auth/login', { method: 'POST', body: input }),
    onSuccess: (user) => queryClient.setQueryData(ME_QUERY_KEY, user),
  })

  const registerMutation = useMutation({
    mutationFn: (input: RegisterInput) =>
      apiFetch<User>('/api/auth/register', { method: 'POST', body: input }),
    onSuccess: (user) => queryClient.setQueryData(ME_QUERY_KEY, user),
  })

  const logoutMutation = useMutation({
    mutationFn: () => apiFetch<void>('/api/auth/logout', { method: 'POST' }),
    onSuccess: () => queryClient.setQueryData(ME_QUERY_KEY, null),
  })

  const login = useCallback(
    async (input: LoginInput) => {
      await loginMutation.mutateAsync(input)
    },
    [loginMutation],
  )

  const register = useCallback(
    async (input: RegisterInput) => {
      await registerMutation.mutateAsync(input)
    },
    [registerMutation],
  )

  const logout = useCallback(async () => {
    await logoutMutation.mutateAsync()
  }, [logoutMutation])

  return (
    <AuthContext.Provider value={{ user: data ?? null, isLoading, login, register, logout }}>
      {children}
    </AuthContext.Provider>
  )
}
