// The React Compiler's memo caches cost the shell more bytes than these rare renders save, and the shell's assets
// sit near their size budget.
'use no memo'

import { useDashboard } from '@headless/dashboard-sdk'
import type { CommandDefinition } from '@headless/dashboard-sdk/commands'
import { useDashboardApi, type CappedCount } from '@headless/dashboard-sdk/data'
import { useQuery, useQueryClient } from '@headless/dashboard-sdk/query'
import { useStreamEvent, useTopics } from '@headless/dashboard-sdk/stream'
import { useCallback, useEffect, useRef, useState } from 'react'
import type { ObjectRef } from './shell-context.tsx'

/** One module's part of an aggregated answer; `stale` when the module timed out or failed. */
export interface ModuleSection<T> {
  id: string
  displayName: string
  stale: boolean
  items: T[]
}

export interface AttentionItem {
  id: string
  /** Items that share a cause share a group key. */
  groupKey: string
  title: string
  severity: 'critical' | 'warning'
  count: number
  /** The server stopped counting at its cap. */
  capped: boolean
  target?: ObjectRef | null
  /** When the item began, an ISO time. */
  since?: string | null
  /** When its cause last occurred, an ISO time. */
  lastSeenAt?: string | null
  /** How many objects joined it in the module's latest interval; above zero, the cause is still growing. */
  recentCount?: number
  /** The command that resolves it, described for this user, and the filter that scopes its Operation to the group. */
  recommended?: { command: CommandDefinition; filter: unknown } | null
}

export interface ResolvedObject {
  target: ObjectRef
  title: string
  status?: string | null
  traceId?: string | null
  traceUrl?: string | null
}

const attentionKey = ['shell', 'attention'] as const

/**
 * Every module's attention items, from the server's `needs-you` aggregate. `attention.changed` refetches them, so
 * the Attention pill and the product tabs' dots stay current while no module's code is loaded.
 */
export function useAttention() {
  const api = useDashboardApi()
  const client = useQueryClient()

  const refetch = useCallback(() => {
    void client.invalidateQueries({ queryKey: attentionKey })
  }, [client])

  useStreamEvent('attention.changed', refetch)

  return useQuery({
    queryKey: attentionKey,
    queryFn: async () =>
      (await api.request<{ modules: ModuleSection<AttentionItem>[] }>({ method: 'GET', path: 'needs-you' })).modules,
  })
}

/** The Attention pill's total: every item's count, with `+` when any count was capped. */
export function attentionTotal(sections: readonly ModuleSection<AttentionItem>[] | undefined): CappedCount {
  let value = 0
  let capped = false

  for (const section of sections ?? []) {
    for (const item of section.items) {
      value += item.count
      capped ||= item.capped
    }
  }

  return { value, capped }
}

/** How long typing settles before a pasted id is looked up in every module. */
const resolveDelayMs = 250

/** A query that may be an id: one token, long enough not to ask every module about each keystroke. */
export function looksLikeId(query: string) {
  const text = query.trim()

  return text.length >= 3 && text.length <= 256 && !/\s/.test(text)
}

/**
 * The objects `query` names in any module, looked up once typing settles. `objects` is `undefined` until the answer
 * for the current query lands, so a caller can tell "no match" from "not asked yet".
 */
export function useResolve(query: string, enabled: boolean): ResolvedObject[] | undefined {
  const api = useDashboardApi()
  const [found, setFound] = useState<{ query: string; objects: ResolvedObject[] }>({ query: '', objects: [] })
  const latest = useRef(0)
  const asked = enabled && looksLikeId(query)

  useEffect(() => {
    if (!asked) {
      return undefined
    }

    const request = ++latest.current
    const timer = setTimeout(() => {
      api
        .request<{ modules: ModuleSection<ResolvedObject>[] }>({ method: 'GET', path: 'resolve', query: { q: query.trim() } })
        .then((answer) => answer.modules.flatMap((section) => section.items))
        .catch(() => [])
        .then((objects) => {
          // Only the answer to the newest query may land; an older one would show objects for text no longer typed.
          if (request === latest.current) {
            setFound({ query, objects })
          }
        })
    }, resolveDelayMs)

    return () => clearTimeout(timer)
  }, [api, asked, query])

  return asked && found.query === query ? found.objects : undefined
}

/** How rarely the sidebar's counts refetch on live changes: a busy module would otherwise recount on every run. */
const countsDebounceMs = 5_000

/** A product's sidebar counts by count key. */
export type ProductCounts = Readonly<Record<string, CappedCount>>

/**
 * The active product's sidebar counts, from one request. A change on any of `topics` refetches them, at most once
 * every 5 s, and not while the tab is hidden; a paused tab hears no change until it resumes, so it does not refetch.
 */
export function useProductCounts(product: string | undefined, topics: readonly string[]) {
  const api = useDashboardApi()
  const client = useQueryClient()
  const queryKey = ['shell', 'counts', product]
  const last = useRef(0)
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined)
  // A stable key, so a new array with the same topics keeps the same listener.
  const topicKey = topics.join(',')

  useTopics(topics)

  const onChange = useCallback(
    (change: { topic: string }) => {
      if (!topicKey.split(',').includes(change.topic) || timer.current !== undefined) {
        return
      }

      const refetch = () => {
        timer.current = undefined

        // A hidden tab refetches nothing; the next change after it shows again does.
        if (typeof document === 'undefined' || document.visibilityState !== 'hidden') {
          last.current = Date.now()
          void client.invalidateQueries({ queryKey })
        }
      }

      timer.current = setTimeout(refetch, Math.max(0, last.current + countsDebounceMs - Date.now()))
    },
    // The query key is rebuilt from `product` each render.
    [client, product, topicKey],
  )

  useEffect(() => () => clearTimeout(timer.current), [])
  useStreamEvent('change', onChange)

  return useQuery({
    queryKey,
    enabled: product !== undefined,
    queryFn: async () => {
      last.current = Date.now()
      const answer = await api.request<{ counts: Record<string, { count: number; capped: boolean }> }>({
        method: 'GET',
        path: 'counts',
        query: { product: product! },
      })

      return Object.fromEntries(
        Object.entries(answer.counts).map(([key, count]) => [key, { value: count.count, capped: count.capped }]),
      ) as ProductCounts
    },
  })
}

/** The modules a user may operate in, from the session's permissions. */
export function useCanOperate(moduleId?: string) {
  const { permissions } = useDashboard()

  if (moduleId) {
    return permissions[moduleId]?.canOperate ?? false
  }

  return Object.values(permissions).some((permission) => permission.canOperate)
}
