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
 * Orders two versions as the server's event publisher does, so the page never applies a change the server would
 * drop or drops one it sent: digits compare as numbers of any length, so a sequence that gains a digit stays ordered,
 * and anything else ordinally, which orders fixed-width timestamps.
 */
export function compareVersions(left: string, right: string) {
  const digits = /^\d+$/
  const [a, b] = digits.test(left) && digits.test(right) ? [BigInt(left), BigInt(right)] : [left, right]

  return a === b ? 0 : a < b ? -1 : 1
}

/**
 * Whether `candidate` is a later version than `held`, the one rule every merge of fetched data and live changes
 * uses. A holder with no version (`undefined`), such as a list row the server serves without one, is older than any
 * version; no version is never newer than anything.
 */
export function isNewerVersion(candidate: string | undefined, held: string | undefined) {
  return candidate !== undefined && (held === undefined || compareVersions(candidate, held) > 0)
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

/**
 * Whether a fetched value is older than `held`. A fetched value with no version is the server's answer now, so
 * nothing the cache learned overrides it.
 */
function isOlderFetch(fetched: unknown, held: string | undefined) {
  const version = versionOf(fetched)

  return version !== undefined && isNewerVersion(held, version)
}

/** An object that changed while no loaded view held it: its change's version and projection. */
interface ArrivedRow {
  version: string
  projection: Record<string, unknown> | null
  /** Whether a fetch that landed older than this change already asked its holders to refetch. */
  hasRefetched?: boolean
}

/**
 * How many arrived objects one topic keeps. A busy topic on a page left open would otherwise grow without end; past
 * the cap the oldest arrival goes, and the notice reads `500+ new`, since Show reads the list again anyway.
 */
export const maxNewRows = 500

/** Per topic, each arrived object by id, oldest first. */
const newRows = new WeakMap<QueryClient, Map<string, Map<string, ArrivedRow>>>()
/** The topics that dropped an arrival at the cap since the operator last showed them. */
const cappedTopics = new WeakMap<QueryClient, Set<string>>()
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

/** Whether `topic` dropped an arrival at the cap, so its new rows count is a floor. */
export function isNewRowsCapped(client: QueryClient, topic: string) {
  return cappedTopics.get(client)?.has(topic) === true
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
  cappedTopics.get(client)?.delete(topic)
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
    if (!arrived || isNewerVersion(change.version, arrived.version)) {
      const rows = new Map(topicRows.get(change.topic)).set(change.id, {
        version: change.version,
        projection: change.projection ?? null,
      })

      if (rows.size > maxNewRows) {
        rows.delete(rows.keys().next().value!)
        cappedTopics.set(client, (cappedTopics.get(client) ?? new Set()).add(change.topic))
      }

      topicRows.set(change.topic, rows)
      notifyNewRows(client)
    }

    return 'new'
  }

  const objectIsOlder = cached !== undefined && isNewerVersion(change.version, versionOf(cached))
  const isOlderRow = (item: ListItem) => item.id === change.id && isNewerVersion(change.version, item.version)

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
  return isOlderFetch(fetched, versionOf(cached)) ? (cached as T) : fetched
}

/** Whether an arrived change is newer than the fetched row and carries no projection to apply to it. */
function needsRefetch(fetched: unknown, arrived: ArrivedRow | undefined) {
  return arrived !== undefined && !arrived.projection && isOlderFetch(fetched, arrived.version)
}

/** The fetched row with an arrived change applied, when the change is newer than the row. */
function withArrived<T>(fetched: T, arrived: ArrivedRow | undefined): T {
  return arrived?.projection && isOlderFetch(fetched, arrived.version)
    ? { ...fetched, ...arrived.projection, version: arrived.version }
    : fetched
}

/**
 * Refetches the object and the lists of `topic` that hold `id` older than `version`, once the fetch now landing has
 * settled into the cache: invalidating while it is still in flight would cancel it. The arrived change is marked, so
 * a server that still answers the older row is asked once, not in a loop; its new rows notice stays until a fetch or
 * a later change catches up.
 */
function refetchOlderHolders(client: QueryClient, topic: string, id: string, arrived: ArrivedRow) {
  if (arrived.hasRefetched) {
    return
  }

  newRows.get(client)?.get(topic)?.set(id, { ...arrived, hasRefetched: true })
  const isOlder = (held: unknown) => isNewerVersion(arrived.version, versionOf(held))

  setTimeout(() => {
    void client.invalidateQueries({
      queryKey: objectKey(topic, id),
      predicate: (query) => query.state.data !== undefined && isOlder(query.state.data),
    })
    void client.invalidateQueries({
      queryKey: listKey(topic),
      predicate: (query) => rowsOf(query.state.data).some((row) => row.id === id && isOlder(row)),
    })
  }, 0)
}

/**
 * Settles a fetched object against what the cache learned while the request was in flight. A stream change can patch
 * the cached object after the server read it, so a fetch that lands later must not put the older version back; and a
 * change that arrived before the object's first fetch resolved still applies to it, or refetches the object when it
 * carried no projection.
 */
export function mergeFetchedObject<T>(client: QueryClient, topic: string, id: string, fetched: T): T {
  const arrived = newRows.get(client)?.get(topic)?.get(id)
  const merged = withArrived(newerRow(fetched, client.getQueryData(objectKey(topic, id))), arrived)

  if (arrived && needsRefetch(merged, arrived)) {
    refetchOlderHolders(client, topic, id, arrived)
  }

  return merged
}

/**
 * Settles a fetched list page against what the cache learned while the request was in flight: each row keeps the
 * newest version any list of its topic holds, so a fetch that lands after a newer stream patch never puts the older
 * row back. A row that changed before the list first loaded takes that change, and leaves the new rows notice
 * because the list now shows it. A change with no projection cannot be applied, so it stays in the notice and the
 * lists holding the older row refetch.
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
  const settled = items.filter((item) => arrived?.has(item.id) && !needsRefetch(item, arrived.get(item.id)))

  if (arrived && settled.length > 0) {
    const remaining = new Map(arrived)
    settled.forEach((item) => remaining.delete(item.id))
    newRows.get(client)?.set(topic, remaining)
    notifyNewRows(client)
  }

  for (const item of items) {
    const row = newRows.get(client)?.get(topic)?.get(item.id)

    if (row && needsRefetch(item, row)) {
      refetchOlderHolders(client, topic, item.id, row)
    }
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
