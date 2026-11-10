import type { QueryClient } from '@tanstack/react-query'
import type { StreamChange } from '../stream/types.ts'
import { listKey, objectKey, topicOfKey } from './client.tsx'
import type { CappedCount } from './counts.ts'

/** A row a live list holds: every row has an id, and a version once the server or the stream sets one. */
export interface ListItem {
  id: string
  version?: string
}

/** One page of a live list, as a list query caches it. */
export interface DashboardListPage<T extends ListItem = ListItem> {
  items: T[]
  count?: CappedCount
  nextCursor?: string | null
}

/**
 * What one change did to the cache:
 * - `ignored`: the cache already held this version or a newer one.
 * - `patched`: a cached object or list row took the projection in place.
 * - `refetch`: the change carried no projection, so the views showing the object refetch it.
 * - `new`: no view holds the object; a list shows it as a new row notice instead of moving under the reader.
 */
export type ChangeOutcome = 'ignored' | 'patched' | 'refetch' | 'new'

/**
 * Orders two versions. The server's versions are opaque strings a module stamps from a sequence or the store's
 * clock, so digits compare as numbers of any length and anything else in natural order.
 */
export function compareVersions(left: string, right: string) {
  if (/^\d+$/.test(left) && /^\d+$/.test(right)) {
    const a = BigInt(left)
    const b = BigInt(right)

    return a === b ? 0 : a < b ? -1 : 1
  }

  // A fixed collation, not the operator's locale: every browser must order the same two versions the same way, or
  // one would drop a change another applies.
  return left.localeCompare(right, 'en', { numeric: true })
}

function isListPage(value: unknown): value is DashboardListPage {
  return typeof value === 'object' && value !== null && Array.isArray((value as DashboardListPage).items)
}

/** A keyset-paged list as its infinite query caches it: the pages loaded so far, in order. */
interface KeysetPages {
  pages: DashboardListPage[]
  pageParams: unknown[]
}

function isKeysetPages(value: unknown): value is KeysetPages {
  return typeof value === 'object' && value !== null && Array.isArray((value as KeysetPages).pages)
}

/** The rows a cached list holds, whether it is one page or a keyset list of several. */
function rowsOf(value: unknown): ListItem[] {
  return isListPage(value) ? value.items : isKeysetPages(value) ? value.pages.flatMap((page) => page.items) : []
}

/** Replaces each row of a cached list that `replace` changes, keeping the list's shape and unchanged pages. */
function mapRows(value: unknown, replace: (item: ListItem) => ListItem): unknown {
  const mapPage = (page: DashboardListPage) => {
    const items = page.items.map(replace)

    return items.every((item, index) => item === page.items[index]) ? page : { ...page, items }
  }

  if (isListPage(value)) {
    return mapPage(value)
  }

  if (!isKeysetPages(value)) {
    return value
  }

  const pages = value.pages.map(mapPage)

  return pages.every((page, index) => page === value.pages[index]) ? value : { ...value, pages }
}

function versionOf(value: unknown): string | undefined {
  return typeof value === 'object' && value !== null && typeof (value as ListItem).version === 'string'
    ? (value as ListItem).version
    : undefined
}

function isNewer(change: StreamChange, held: string | undefined) {
  return held === undefined || compareVersions(change.version, held) > 0
}

/** An object that changed while no loaded view held it: its change's version and projection. */
interface ArrivedRow {
  version: string
  projection: Record<string, unknown> | null
}

/** Per topic, each arrived object by id. */
const newRows = new WeakMap<QueryClient, Map<string, Map<string, ArrivedRow>>>()
const newRowListeners = new WeakMap<QueryClient, Set<() => void>>()

function notifyNewRows(client: QueryClient) {
  for (const listener of [...(newRowListeners.get(client) ?? [])]) {
    listener()
  }
}

/** The objects that changed on `topic` while no loaded list held them: each one's projection by id. */
export function getNewRows(client: QueryClient, topic: string): ReadonlyMap<string, Record<string, unknown> | null> {
  const arrived = newRows.get(client)?.get(topic)

  return new Map(arrived ? [...arrived].map(([id, row]) => [id, row.projection]) : [])
}

export function onNewRowsChange(client: QueryClient, listener: () => void) {
  let listeners = newRowListeners.get(client)

  if (!listeners) {
    listeners = new Set()
    newRowListeners.set(client, listeners)
  }

  listeners.add(listener)

  return () => {
    listeners.delete(listener)
  }
}

/** Clears the new rows notice of `topic` and refetches its lists, when the operator asks to see them. */
export async function showNewRows(client: QueryClient, topic: string) {
  newRows.get(client)?.delete(topic)
  notifyNewRows(client)
  await client.invalidateQueries({ queryKey: listKey(topic) })
}

/**
 * Applies one stream change to the cache: patches the cached object and each list row that holds it, without
 * refetching the list. Each holder decides alone: one already at or past the change's version keeps what it has,
 * while the others still take the change, because the server never sends that version again.
 */
export function applyChange(client: QueryClient, change: StreamChange): ChangeOutcome {
  const key = objectKey(change.topic, change.id)
  const cached = client.getQueryData(key)
  const lists = client.getQueriesData({ queryKey: listKey(change.topic) })
  const rows = lists.flatMap(([, data]) => rowsOf(data).filter((item) => item.id === change.id))
  const held = cached !== undefined || rows.length > 0

  if (!held) {
    let topicRows = newRows.get(client)

    if (!topicRows) {
      topicRows = new Map()
      newRows.set(client, topicRows)
    }

    const arrived = topicRows.get(change.topic)?.get(change.id)

    // A later change of the same object replaces its projection, so a list's filter reads where the row is now; an
    // older one arriving late does not.
    if (!arrived || isNewer(change, arrived.version)) {
      topicRows.set(
        change.topic,
        new Map(topicRows.get(change.topic)).set(change.id, {
          version: change.version,
          projection: change.projection ?? null,
        }),
      )
      notifyNewRows(client)
    }

    return 'new'
  }

  const objectIsOlder = cached !== undefined && isNewer(change, versionOf(cached))
  const isOlderRow = (item: ListItem) => item.id === change.id && isNewer(change, item.version)

  if (!objectIsOlder && !rows.some(isOlderRow)) {
    return 'ignored'
  }

  if (!change.projection) {
    if (objectIsOlder) {
      void client.invalidateQueries({ queryKey: key })
    }

    void client.invalidateQueries({
      queryKey: listKey(change.topic),
      predicate: (query) => rowsOf(query.state.data).some(isOlderRow),
    })

    return 'refetch'
  }

  const patch = { ...change.projection, version: change.version }

  if (objectIsOlder) {
    client.setQueryData(key, typeof cached === 'object' && cached !== null ? { ...cached, ...patch } : patch)
  }

  for (const [listQueryKey, data] of lists) {
    if (rowsOf(data).some(isOlderRow)) {
      client.setQueryData(
        listQueryKey,
        mapRows(data, (item) => (isOlderRow(item) ? { ...item, ...patch } : item)),
      )
    }
  }

  return 'patched'
}

/** The newer of a cached row and a fetched one; the fetched one on a tie or when either has no version. */
function newerRow<T>(fetched: T, cached: unknown): T {
  const fetchedVersion = versionOf(fetched)
  const cachedVersion = versionOf(cached)

  return fetchedVersion !== undefined && cachedVersion !== undefined && compareVersions(cachedVersion, fetchedVersion) > 0
    ? (cached as T)
    : fetched
}

/** The fetched row with an arrived change applied, when the change is newer than the row. */
function withArrived<T>(fetched: T, arrived: ArrivedRow | undefined): T {
  if (!arrived?.projection || typeof fetched !== 'object' || fetched === null) {
    return fetched
  }

  const version = versionOf(fetched)

  return version !== undefined && compareVersions(arrived.version, version) > 0
    ? { ...fetched, ...arrived.projection, version: arrived.version }
    : fetched
}

/**
 * Settles a fetched object against what the cache learned while the request was in flight. A stream change can patch
 * the cached object after the server read it, so a fetch that lands later must not put the older version back; and a
 * change that arrived before the object's first fetch resolved still applies to it.
 */
export function mergeFetchedObject<T>(client: QueryClient, topic: string, id: string, fetched: T): T {
  return withArrived(newerRow(fetched, client.getQueryData(objectKey(topic, id))), newRows.get(client)?.get(topic)?.get(id))
}

/**
 * Settles a fetched list page against what the cache learned while the request was in flight: each row keeps the
 * newest version any list of its topic holds, so a fetch that lands after a newer stream patch never puts the older
 * row back. A row that changed before the list first loaded takes that change, and leaves the new rows notice
 * because the list now shows it.
 */
export function mergeFetchedPage<T extends ListItem>(
  client: QueryClient,
  topic: string,
  page: DashboardListPage<T>,
): DashboardListPage<T> {
  const held = new Map<string, ListItem>()

  for (const [, data] of client.getQueriesData({ queryKey: listKey(topic) })) {
    for (const row of rowsOf(data)) {
      held.set(row.id, newerRow(row, held.get(row.id)))
    }
  }

  const arrived = newRows.get(client)?.get(topic)
  const items = page.items.map((item) => withArrived(newerRow(item, held.get(item.id)), arrived?.get(item.id)))

  if (arrived && page.items.some((item) => arrived.has(item.id))) {
    const remaining = new Map(arrived)
    page.items.forEach((item) => remaining.delete(item.id))
    newRows.get(client)?.set(topic, remaining)
    notifyNewRows(client)
  }

  return items.every((item, index) => item === page.items[index]) ? page : { ...page, items }
}

/** Refetches every active query of `topics`, or of every topic when `topics` is omitted. */
export async function refetchTopics(client: QueryClient, topics?: readonly string[]) {
  const wanted = topics ? new Set(topics) : undefined

  if (wanted?.size === 0) {
    return
  }

  await client.invalidateQueries({
    predicate: (query) => {
      const topic = topicOfKey(query.queryKey)

      return topic !== undefined && (!wanted || wanted.has(topic))
    },
  })
}
