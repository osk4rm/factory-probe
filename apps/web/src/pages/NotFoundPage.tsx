import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return (
    <div className="mx-auto mt-24 max-w-md text-center">
      <h1 className="text-3xl font-semibold">404</h1>
      <p className="mt-2 text-slate-500">This page does not exist.</p>
      <Link to="/" className="mt-6 inline-block font-medium text-slate-900 underline">
        Back to dashboard
      </Link>
    </div>
  )
}
