import { useInfiniteQuery } from '@tanstack/react-query'
import { useMemo, useRef } from 'react'
import { useTopics } from '../stream/hooks.ts'
import { listKey } from './client.tsx'
import type { DashboardListPage, ListItem } from './patch.ts'

export interface DashboardKeysetListOptions<T extends ListItem> {
  /** `{module}.{kind}`: the stream topic whose changes patch this list. */
  topic: string
  /** The list's filter and sort; part of its cache key, so a new filter starts again from the first page. */
  params?: unknown
  /**
   * Reads one page: the first when `cursor` is `undefined`, else the page after the row the cursor names. The
   * page's `nextCursor` is the keyset cursor of its last row, or `null` when no row follows.
   */
  fetchPage: (cursor: string | undefined) => Promise<DashboardListPage<T>>
}

/**
 * A live keyset-paged list: the first page, then each next page when `loadMore` asks for it, patched row by row from
 * the stream while mounted. `loadMore` asks for a page once: called again before that page answers, or for a cursor
 * already read, it does nothing, so a table can call it on every scroll and focus near its end.
 */
export function useDashboardKeysetList<T extends ListItem>(options: DashboardKeysetListOptions<T>) {
  useTopics([options.topic])

  const query = useInfiniteQuery({
    queryKey: listKey(options.topic, options.params),
    queryFn: ({ pageParam }) => options.fetchPage(pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })
  const requested = useRef(new Set<string>())
  const pages = query.data?.pages
  const rows = useMemo(() => pages?.flatMap((page) => page.items) ?? [], [pages])
  const nextCursor = pages?.at(-1)?.nextCursor ?? undefined

  function loadMore() {
    if (nextCursor === undefined || requested.current.has(nextCursor)) {
      return
    }

    requested.current.add(nextCursor)
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
