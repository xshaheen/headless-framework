import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useSyncExternalStore } from 'react'
import { useDashboard } from '../context.tsx'
import { useTopics } from '../stream/hooks.ts'
import { listKey, objectKey } from './client.tsx'
import { getNewRows, onNewRowsChange, showNewRows, type DashboardListPage, type ListItem } from './patch.ts'

export interface DashboardListOptions<T extends ListItem> {
  /** `{module}.{kind}`: the stream topic whose changes patch this list. */
  topic: string
  /** The list's filter, sort, and page; part of its cache key. */
  params?: unknown
  fetch: () => Promise<DashboardListPage<T>>
}

/**
 * A live list: fetched once, then patched row by row from the stream while mounted. A change for a row no loaded
 * list holds shows as a new row notice instead of a reload.
 */
export function useDashboardList<T extends ListItem>(options: DashboardListOptions<T>) {
  useTopics([options.topic])

  return useQuery({ queryKey: listKey(options.topic, options.params), queryFn: options.fetch })
}

export interface DashboardObjectOptions<T> {
  topic: string
  id: string
  fetch: () => Promise<T>
}

/** A live object: fetched once, then patched from the stream while mounted. */
export function useDashboardObject<T>(options: DashboardObjectOptions<T>) {
  useTopics([options.topic])

  return useQuery({ queryKey: objectKey(options.topic, options.id), queryFn: options.fetch })
}

/** The new rows notice of `topic`: how many rows arrived, and the call that shows them. */
export function useNewRows(topic: string) {
  const client = useQueryClient()
  const count = useSyncExternalStore(
    (listener) => onNewRowsChange(client, listener),
    () => getNewRows(client, topic).size,
  )

  return { count, show: () => showNewRows(client, topic) }
}

/** The dashboard's API client, as the shell configured it. */
export function useDashboardApi() {
  return useDashboard().api
}
