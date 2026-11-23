// The React Compiler's memo caches cost the shell more bytes than these rare renders save, and the shell's assets
// sit near their size budget.
'use no memo'

import { useDashboard, type DashboardStateRole } from '@headless/dashboard-sdk'
import type { CommandDefinition } from '@headless/dashboard-sdk/commands'
import { useDashboardApi, type CappedCount } from '@headless/dashboard-sdk/data'
import { useQuery, useQueryClient } from '@headless/dashboard-sdk/query'
import { useStreamEvent, useTopics } from '@headless/dashboard-sdk/stream'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useShell, type ObjectRef } from './shell-context.tsx'

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

/** What a lookup found: a failed request, or the matches with the names of the modules that did not answer. */
export type Resolved = { failed: true } | { failed: false; objects: ResolvedObject[]; stale: string[] }

/**
 * The objects `query` names in any module, looked up once typing settles. `undefined` until the answer for the
 * current query lands, so a caller can tell "no match" from "not asked yet", and a failed or partial answer from
 * an empty one.
 */
export function useResolve(query: string, enabled: boolean): Resolved | undefined {
  const api = useDashboardApi()
  const [found, setFound] = useState<Resolved & { query: string }>()
  const latest = useRef(0)
  const asked = enabled && looksLikeId(query)

  useEffect(() => {
    if (!asked) {
      return undefined
    }

    const request = ++latest.current
    const timer = setTimeout(() => {
      api
        .request<{ modules: ModuleSection<ResolvedObject>[] }>({
          method: 'GET',
          path: 'resolve',
          query: { q: query.trim() },
        })
        .then(
          ({ modules }): Resolved => ({
            failed: false,
            objects: modules.flatMap((section) => section.items),
            stale: modules.filter((section) => section.stale).map((section) => section.displayName),
          }),
          (): Resolved => ({ failed: true }),
        )
        .then((answer) => {
          // Only the answer to the newest query may land; an older one would show objects for text no longer typed.
          if (request === latest.current) {
            setFound({ query, ...answer })
          }
        })
    }, resolveDelayMs)

    return () => clearTimeout(timer)
  }, [api, asked, query])

  return asked && found?.query === query ? found : undefined
}

const stateRoles = ['sched', 'enq', 'proc', 'ok', 'fail', 'off'] as const

/** One module's part of a trace: its state, how many rows it gave, and the list that holds the rest when limited. */
export interface TraceSection {
  id: string
  displayName: string
  stale: boolean
  /** The sources, such as stores or nodes, that did not answer. */
  unavailable?: string[]
  /** More objects carry the id than the module's limit let it return. */
  limited: boolean
  count: number
  /** The module's own list, a path inside the module, opened with `state` as history state. */
  list?: { path: string; state?: unknown } | null
}

/** One row of the trace tree, in the server's depth-first order. */
export interface TraceRow {
  module: string
  target: ObjectRef
  title: string
  status?: string | null
  statusRole?: DashboardStateRole | null
  at: string
  timestampKind: 'ran' | 'due' | 'added'
  node?: string | null
  depth: number
  tags: string[]
}

export interface Trace {
  modules: TraceSection[]
  rows: TraceRow[]
  /** How many trace-capable modules the user may not view. */
  hidden: number
}

function isStateRole(value: unknown): value is DashboardStateRole {
  return stateRoles.includes(value as DashboardStateRole)
}

/** The trace as the page draws it: a status role the shell does not know reads as none, and a missing depth as a root. */
function readTrace(answer: Trace): Trace {
  return {
    modules: answer.modules ?? [],
    hidden: typeof answer.hidden === 'number' ? answer.hidden : 0,
    rows: (answer.rows ?? []).map((row) => ({
      ...row,
      statusRole: isStateRole(row.statusRole) ? row.statusRole : null,
      depth: typeof row.depth === 'number' && row.depth >= 0 ? row.depth : 0,
      tags: Array.isArray(row.tags) ? row.tags : [],
    })),
  }
}

/**
 * Every object that carries `correlationId`, from every module the user may view, as one tree. The id travels in the
 * body, never the URL, so it stays out of server and proxy logs. Nothing is asked for an empty id.
 */
export function useTrace(correlationId: string) {
  const api = useDashboardApi()

  return useQuery({
    queryKey: ['shell', 'trace', correlationId],
    enabled: correlationId !== '',
    queryFn: async () =>
      readTrace(await api.request<Trace>({ method: 'POST', path: 'trace', body: { correlationId } })),
  })
}

/** How rarely the sidebar's counts refetch on live changes: a busy module would otherwise recount on every run. */
const countsDebounceMs = 5_000

/** A product's sidebar counts by count key; `unavailable` names the sources a partial count leaves out. */
export type ProductCounts = Readonly<Record<string, CappedCount & { unavailable?: string[] }>>

/**
 * The active product's sidebar counts, from one request, and the modules whose counts did not answer. They refetch
 * on each mount, on a stream reset or a reopened topic, and on a change of any of `topics` at most once every 5 s. A
 * hidden or paused tab owes the refetch instead, and pays it once it shows and runs live again.
 */
export function useProductCounts(product: string | undefined, topics: readonly string[]) {
  const api = useDashboardApi()
  const client = useQueryClient()
  const { live } = useShell()
  const queryKey = ['shell', 'counts', product]
  const last = useRef(0)
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined)
  const owed = useRef(false)
  // A stable key, so a new array with the same topics keeps the same listener.
  const topicKey = topics.join(',')

  useTopics(topics)

  const refetch = useCallback(
    () => {
      clearTimeout(timer.current)
      timer.current = undefined
      owed.current = document.visibilityState === 'hidden' || live.getPending() !== null

      if (!owed.current) {
        last.current = Date.now()
        void client.invalidateQueries({ queryKey })
      }
    },
    // The query key is rebuilt from `product` each render.
    [client, live, product],
  )

  const onChange = useCallback(
    (change: { topic: string }) => {
      if (topicKey.split(',').includes(change.topic) && timer.current === undefined) {
        timer.current = setTimeout(refetch, Math.max(0, last.current + countsDebounceMs - Date.now()))
      }
    },
    [refetch, topicKey],
  )

  const onSubscribed = useCallback(
    ({ added }: { added: string[] }) => {
      if (added.some((topic) => topicKey.split(',').includes(topic))) {
        refetch()
      }
    },
    [refetch, topicKey],
  )

  useEffect(() => {
    const catchUp = () => {
      if (owed.current) {
        refetch()
      }
    }

    document.addEventListener('visibilitychange', catchUp)
    const stop = live.onPauseChange(catchUp)

    return () => {
      document.removeEventListener('visibilitychange', catchUp)
      stop()
    }
  }, [live, refetch])

  useEffect(() => () => clearTimeout(timer.current), [])
  // A stream reset reads every query again, these counts included, through the data client's own binding.
  useStreamEvent('change', onChange)
  useStreamEvent('subscribed', onSubscribed)

  return useQuery({
    queryKey,
    enabled: product !== undefined,
    // A revisit, by a remount or a switch back to the product, counts again once: changes that arrived meanwhile
    // never reached it.
    staleTime: 0,
    queryFn: async () => {
      last.current = Date.now()
      const answer = await api.request<{
        counts: Record<string, { count: number; capped: boolean; unavailable?: string[] }>
        stale: { id: string; displayName: string }[]
      }>({
        method: 'GET',
        path: 'counts',
        query: { product: product! },
      })

      return {
        counts: Object.fromEntries(
          // A count that leaves sources out is a floor, so it reads `N+` as a capped one does.
          Object.entries(answer.counts).map(([key, { count, capped, unavailable }]) => [
            key,
            { value: count, capped: capped || !!unavailable, unavailable },
          ]),
        ) as ProductCounts,
        stale: answer.stale,
      }
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
