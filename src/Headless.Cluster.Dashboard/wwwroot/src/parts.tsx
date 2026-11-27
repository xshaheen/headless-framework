import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { StaleNotice as SdkStaleNotice, Tag } from '@headless/dashboard-sdk/components'
import { useQuery, useQueryClient, type QueryKey } from '@headless/dashboard-sdk/query'
import { useModuleStale, useStreamEvent, useStreamStatus, useTopics } from '@headless/dashboard-sdk/stream'
import { useState, type ReactNode } from 'react'

export function CellText({ title, children }: { title?: string; children: ReactNode }) {
  return <span title={title}>{children}</span>
}

/** Whether what the view shows may be out of date: the stream is down, or this module's changes stopped. */
export function useStale() {
  const module = useModule()
  const { status } = useStreamStatus()
  const { isStale } = useModuleStale(module.id)

  return status === 'stale' || isStale
}

/** `Stale` while the stream is down or the server reports the module's changes stopped; the data on screen is kept. */
export function StaleNotice({ what, onRefresh, failed }: { what: string; onRefresh: () => void; failed?: boolean }) {
  return <SdkStaleNotice stale={useStale()} what={what} onRefresh={onRefresh} failed={failed} />
}

/**
 * The Cluster sidebar's counts. The query key and answer shape are the shell sidebar's own, so the strip and the
 * sidebar read one cached answer; no second request is made. A key the host does not serve is missing.
 */
export function useClusterCounts() {
  const { api } = useDashboard()

  return useQuery({
    queryKey: ['shell', 'counts', 'cluster'],
    queryFn: async () => {
      const answer = await api.request<{
        counts: Record<string, { count: number; capped: boolean }>
        stale: { id: string; displayName: string }[]
      }>({ method: 'GET', path: 'counts', query: { product: 'cluster' } })

      return {
        counts: Object.fromEntries(
          Object.entries(answer.counts).map(([key, count]) => [key, { value: count.count, capped: count.capped }]),
        ),
        stale: answer.stale,
      }
    },
  })
}

/**
 * Reads the module's data again when one of `kinds` changes on the stream: every query under `key` refetches, the
 * open panel's own read included, so a removed object reaches the panel too.
 */
export function useLiveRefetch(kinds: readonly ('node' | 'check')[], keys: readonly QueryKey[]) {
  const client = useQueryClient()

  useTopics(kinds.map((kind) => `cluster.${kind}`))
  useStreamEvent('change', (change) => {
    if (change.module !== 'cluster' || !kinds.some((kind) => kind === change.kind)) {
      return
    }

    for (const key of keys) {
      void client.invalidateQueries({ queryKey: key })
    }
  })
}

/**
 * The list's rows, plus the open row when it has left the active filter, so the operator does not lose what they are
 * reading: the kept row is marked, and is dropped once the panel closes or the filter matches it again.
 */
export function useKeptOpenRow<T extends { id: string }>(rows: readonly T[], openId: string | null) {
  const [kept, setKept] = useState<T | undefined>()
  const current = openId ? rows.find((row) => row.id === openId) : undefined

  if (current && current !== kept) {
    setKept(current)
  }

  if (!openId && kept) {
    setKept(undefined)
  }

  const isKept = openId !== null && !current && kept?.id === openId

  return {
    rows: isKept && kept ? [...rows, kept] : rows,
    isKept: (row: T) => isKept && row.id === openId,
  }
}

export function FilterNote() {
  return <Tag>No longer matches the filter</Tag>
}
