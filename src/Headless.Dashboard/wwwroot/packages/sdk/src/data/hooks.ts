import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useSyncExternalStore } from 'react'
import { useDashboard } from '../context.tsx'
import { useTopics } from '../stream/hooks.ts'
import { listKey, objectKey } from './client.tsx'
import {
  getNewRows,
  isNewRowsCapped,
  mergeFetchedObject,
  mergeFetchedPage,
  onNewRowsChange,
  showNewRows,
  type DashboardListPage,
  type ListItem,
} from './patch.ts'

export interface DashboardListOptions<T extends ListItem> {
  /** `{module}.{kind}`: the stream topic whose changes patch this list. */
  topic: string
  /** The list's filter, sort, and page; part of its cache key. */
  params?: unknown
  fetch: () => Promise<DashboardListPage<T>>
}

/**
 * A live list: fetched once, then patched row by row from the stream while mounted. A fetch that lands after a newer
 * stream change keeps the newer row. A change for a row no loaded
 * list holds shows as a new row notice instead of a reload.
 */
export function useDashboardList<T extends ListItem>(options: DashboardListOptions<T>) {
  useTopics([options.topic])
  const client = useQueryClient()

  return useQuery({
    queryKey: listKey(options.topic, options.params),
    queryFn: async () => mergeFetchedPage(client, options.topic, await options.fetch()),
  })
}

export interface DashboardObjectOptions<T> {
  topic: string
  id: string
  fetch: () => Promise<T>
}

/** A live object: fetched once, then patched from the stream while mounted. */
export function useDashboardObject<T>(options: DashboardObjectOptions<T>) {
  useTopics([options.topic])
  const client = useQueryClient()

  return useQuery({
    queryKey: objectKey(options.topic, options.id),
    queryFn: async () => mergeFetchedObject(client, options.topic, options.id, await options.fetch()),
  })
}

/**
 * The new rows notice of `topic`: how many rows arrived, whether that count is a floor because the topic passed its
 * cap of 500 and dropped the oldest (`isCapped`, read `500+ new`), and the call that shows them. With `matches`, only
 * an arrived row whose projection matches the list's filter counts, because only those would appear on `show`.
 */
export function useNewRows<T>(topic: string, matches?: (row: T) => boolean) {
  const client = useQueryClient()
  const subscribe = (listener: () => void) => onNewRowsChange(client, listener)
  const count = useSyncExternalStore(
    subscribe,
    () => [...getNewRows(client, topic).values()].filter((row) => !matches || (row !== null && matches(row as T))).length,
  )
  const isCapped = useSyncExternalStore(subscribe, () => isNewRowsCapped(client, topic))

  return { count, isCapped, show: () => showNewRows(client, topic) }
}

/** The dashboard's API client, as the shell configured it. */
export function useDashboardApi() {
  return useDashboard().api
}
