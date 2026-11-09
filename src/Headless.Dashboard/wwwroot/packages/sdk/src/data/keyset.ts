import { useInfiniteQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { useTopics } from '../stream/hooks.ts'
import { listKey } from './client.tsx'
import type { DashboardListPage, ListItem } from './patch.ts'

/** The server order of a list: a column's sort key, read up or down. */
export interface ListSort {
  key: string
  direction: 'ascending' | 'descending'
}

/** A time window as UTC instants: `start` is inclusive, `end` exclusive, so a day ends at the next day's midnight. */
export interface ListRange {
  start: string
  end: string
}

/** What one page request carries besides its cursor: the list's order, text, and window, and the request's abort. */
export interface ListPageRequest {
  sort?: ListSort
  search?: string
  range?: ListRange
  /** Aborts when the list's scope changes or it unmounts, so the server stops the read nobody will show. */
  signal: AbortSignal
}

export interface DashboardKeysetListOptions<T extends ListItem> {
  /** `{module}.{kind}`: the stream topic whose changes patch this list. */
  topic: string
  /** The list's filter; part of its cache key, so a new filter starts again from the first page. */
  params?: unknown
  /** The server order; changing it starts again from the first page, with no cursor. */
  sort?: ListSort
  /** The text the rows must match; an empty string searches nothing. Changing it starts again from the first page. */
  search?: string
  /** The time window the rows fall in; changing it starts again from the first page. */
  range?: ListRange
  /**
   * Reads one page: the first when `cursor` is `undefined`, else the page after the row the cursor names. The
   * page's `nextCursor` is the keyset cursor of its last row, or `null` when no row follows. A cursor is only valid
   * for the sort, filter, search, and range it came from, so it is never sent with another.
   */
  fetchPage: (cursor: string | undefined, request: ListPageRequest) => Promise<DashboardListPage<T>>
}

/**
 * A live keyset-paged list: the first page, then each next page when `loadMore` asks for it, patched row by row from
 * the stream while mounted. `loadMore` does nothing while a next page is loading, so a table can call it on every
 * scroll and focus near its end; after a page fails, the next call asks for it again.
 *
 * The sort, search, and range are part of the cache key with the filter. Changing any of them drops the cursor and
 * reads the first page again; a page still loading for the old scope is aborted, and its late result is never shown.
 * While the new first page loads under the same filter, the old rows stay in view rather than flash empty.
 */
export function useDashboardKeysetList<T extends ListItem>(options: DashboardKeysetListOptions<T>) {
  useTopics([options.topic])

  const { sort, range } = options
  const search = options.search || undefined
  const scoped = sort !== undefined || search !== undefined || range !== undefined
  // The scope follows the filter in the key, so the stream's prefix patches and a view's invalidation of
  // `listKey(topic, filter)` still reach every sort and search of that filter.
  const queryKey = scoped ? [...listKey(options.topic, options.params ?? null), { sort, search, range }] : listKey(options.topic, options.params)

  const query = useInfiniteQuery({
    queryKey,
    queryFn: ({ pageParam, signal }) => options.fetchPage(pageParam, { sort, search, range, signal }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
    // A new order or search of the same filter keeps the old rows until the first page of the new one arrives; a new
    // filter starts empty, as it always has.
    placeholderData: (previous, previousQuery) =>
      previousQuery !== undefined && sameFilter(previousQuery.queryKey, queryKey) ? previous : undefined,
  })
  const pages = query.data?.pages
  const rows = useMemo(() => pages?.flatMap((page) => page.items) ?? [], [pages])
  // A placeholder's cursor belongs to the scope it came from, so it offers no next page.
  const nextCursor = query.isPlaceholderData ? undefined : (pages?.at(-1)?.nextCursor ?? undefined)

  function loadMore() {
    if (nextCursor === undefined || query.isFetchingNextPage) {
      return
    }

    void query.fetchNextPage({ cancelRefetch: false })
  }

  return {
    query,
    rows,
    /** The count of the filter, from the first page: `10,000+` when the server capped it. */
    count: pages?.[0]?.count,
    hasMore: nextCursor !== undefined,
    isLoadingMore: query.isFetchingNextPage,
    loadMore,
  }
}

// Both keys are `['list', topic, filter?, scope?]`; the filter is the same when the third parts serialize alike.
function sameFilter(previous: readonly unknown[], next: readonly unknown[]) {
  return previous[1] === next[1] && JSON.stringify(previous[2] ?? null) === JSON.stringify(next[2] ?? null)
}
