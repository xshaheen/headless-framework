import { QueryClient, QueryClientProvider, useQuery, type QueryKey } from '@tanstack/react-query'
import type { ReactNode } from 'react'

/**
 * The cache key of one object: its kind and its id. A module object's kind is its topic, `{module}.{kind}`, so a
 * stream change for that topic finds the object.
 */
export function objectKey(kind: string, id: string) {
  return ['object', kind, id] as const
}

/** The cache key of one list of a topic; `params` are the list's filter, sort, and page. */
export function listKey(topic: string, params?: unknown) {
  return params === undefined ? (['list', topic] as const) : (['list', topic, params] as const)
}

/** The topic a dashboard query key belongs to, or `undefined` for a key the stream does not patch. */
export function topicOfKey(key: QueryKey): string | undefined {
  return (key[0] === 'object' || key[0] === 'list') && typeof key[1] === 'string' ? key[1] : undefined
}

/** Creates the dashboard's data client. The shell owns the one instance every module reads through. */
export function createDashboardClient() {
  // Stream events keep cached data current, so nothing refetches on a timer, on focus, or on remount.
  return new QueryClient({
    defaultOptions: {
      queries: { staleTime: Infinity, retry: false, refetchOnWindowFocus: false, refetchOnReconnect: false },
    },
  })
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
