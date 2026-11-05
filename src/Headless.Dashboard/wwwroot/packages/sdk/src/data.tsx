import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'

/** The cache key of one object: its kind and its id. */
export function objectKey(kind: string, id: string) {
  return ['object', kind, id] as const
}

/** Creates the dashboard's data client. The shell owns the one instance every module reads through. */
export function createDashboardClient() {
  return new QueryClient({ defaultOptions: { queries: { staleTime: Infinity, retry: false } } })
}

/** Stores one object in the data client, so every view that shows it renders the new value. */
export function setObject<T>(client: QueryClient, kind: string, id: string, value: T) {
  client.setQueryData(objectKey(kind, id), value)
}

export function DashboardDataProvider({ client, children }: { client: QueryClient; children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

/** Reads one object from the data client's cache; `undefined` until something stores it. */
export function useObject<T>(kind: string, id: string): T | undefined {
  // Read-only: the cache is filled by the shell and by stream events, never fetched from here.
  const { data } = useQuery<T>({ queryKey: objectKey(kind, id), enabled: false })

  return data
}
