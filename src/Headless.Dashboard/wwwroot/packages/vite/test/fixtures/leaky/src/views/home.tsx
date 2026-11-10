// Imports TanStack Query directly instead of through the SDK, so its build must fail the bundle check.
import { useQueryClient } from '@tanstack/react-query'

export default function Home() {
  const client = useQueryClient()

  return <p>{client.isFetching()}</p>
}
