import { useAuth } from '@/features/auth/auth-context'
import { Button } from '@/components/ui/button'

export function DashboardPage() {
  const { user, logout } = useAuth()

  return (
    <div className="mx-auto mt-16 w-full max-w-2xl space-y-6">
      <header className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">Dashboard</h1>
          <p className="text-slate-500">Signed in as {user?.email}</p>
        </div>
        <Button variant="outline" onClick={() => void logout()}>
          Sign out
        </Button>
      </header>

      <section className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
        <h2 className="text-lg font-medium">Your template is alive</h2>
        <p className="mt-2 text-sm text-slate-500">
          React + Vite + Tailwind on the front, .NET 10 + PostgreSQL + Identity on the back.
          Ask the swarm to build the first feature.
        </p>
      </section>
    </div>
  )
}
