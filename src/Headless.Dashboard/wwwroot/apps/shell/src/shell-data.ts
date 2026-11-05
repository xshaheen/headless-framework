import { useDashboard } from '@headless/dashboard-sdk'
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
}

export interface Figure {
  id: string
  label: string
  value: number
  unit?: string | null
  comparison?: { label: string; value: number; higherIsBetter?: boolean } | null
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

export function useOverview() {
  const api = useDashboardApi()

  return useQuery({
    queryKey: overviewKey,
    queryFn: async () =>
      (await api.request<{ modules: ModuleSection<Figure>[] }>({ method: 'GET', path: 'overview' })).modules,
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
