import { useDashboard } from '@headless/dashboard-sdk'
import type { CommandDefinition } from '@headless/dashboard-sdk/commands'
import type { ScheduleTick, ScheduleTrack } from '@headless/dashboard-sdk/components'
import { useDashboardApi } from '@headless/dashboard-sdk/data'
import { useQuery, useQueryClient } from '@headless/dashboard-sdk/query'
import { useStreamEvent } from '@headless/dashboard-sdk/stream'
import { useCallback } from 'react'
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

export interface Figure {
  id: string
  label: string
  value: number
  unit?: string | null
  /** `value` is `null` when the earlier period has nothing to compare; `label` then says why. */
  comparison?: { label: string; value: number | null; higherIsBetter?: boolean } | null
}

/** One run or fire on a module's strip track, and the object it opens. */
export interface OverviewTick extends ScheduleTick {
  target?: ObjectRef | null
}

/** A module's Overview: its figures as `items`, and its schedule strip tracks. */
export interface OverviewSection extends ModuleSection<Figure> {
  tracks?: (Omit<ScheduleTrack, 'ticks'> & { ticks: OverviewTick[] })[]
}

export interface ResolvedObject {
  target: ObjectRef
  title: string
  status?: string | null
  traceId?: string | null
  traceUrl?: string | null
}

export interface RelatedGroup {
  label: string
  ids: string[]
  items: ResolvedObject[]
  hasMore: boolean
}

const needsYouKey = ['shell', 'needs-you'] as const
const overviewKey = ['shell', 'overview'] as const

/**
 * Every module's attention items. `attention.changed` refetches them, so Needs you and the rail's count stay current
 * while no module's code is loaded.
 */
export function useNeedsYou() {
  const api = useDashboardApi()
  const client = useQueryClient()

  const refetch = useCallback(() => {
    void client.invalidateQueries({ queryKey: needsYouKey })
  }, [client])

  useStreamEvent('attention.changed', refetch)

  return useQuery({
    queryKey: needsYouKey,
    queryFn: async () =>
      (await api.request<{ modules: ModuleSection<AttentionItem>[] }>({ method: 'GET', path: 'needs-you' })).modules,
  })
}

/**
 * Every module's figures. They refetch on `attention.changed`, as Needs you does, because the shell subscribes to no
 * module topic and that event is the one a module's change reaches it by while no module's code is loaded.
 */
export function useOverview() {
  const api = useDashboardApi()
  const client = useQueryClient()

  const refetch = useCallback(() => {
    void client.invalidateQueries({ queryKey: overviewKey })
  }, [client])

  useStreamEvent('attention.changed', refetch)

  return useQuery({
    queryKey: overviewKey,
    queryFn: async () =>
      (await api.request<{ modules: OverviewSection[] }>({ method: 'GET', path: 'overview' })).modules,
  })
}

export function useRelated(target: ObjectRef | null | undefined) {
  const api = useDashboardApi()

  return useQuery({
    queryKey: ['shell', 'related', target?.module, target?.kind, target?.id],
    enabled: Boolean(target),
    queryFn: () =>
      api.request<{ groups: RelatedGroup[]; stale: { id: string; displayName: string }[] }>({
        method: 'GET',
        path: 'related',
        query: { module: target!.module, kind: target!.kind, id: target!.id },
      }),
  })
}

/** The total the rail shows beside Needs you: every item's count, with `+` when any count was capped. */
export function attentionTotal(sections: readonly ModuleSection<AttentionItem>[] | undefined) {
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

/** The modules a user may operate in, from the session's permissions. */
export function useCanOperate(moduleId?: string) {
  const { permissions } = useDashboard()

  if (moduleId) {
    return permissions[moduleId]?.canOperate ?? false
  }

  return Object.values(permissions).some((permission) => permission.canOperate)
}
