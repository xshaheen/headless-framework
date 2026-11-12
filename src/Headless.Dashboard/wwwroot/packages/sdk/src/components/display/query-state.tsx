'use no memo'

import type { ReactNode } from 'react'
import { useDelayed } from '../use-delayed.ts'
import { LoadError } from './banner.tsx'
import { Skeleton, type SkeletonShape } from './skeleton.tsx'

/**
 * A read as a view holds it: pending, failed (with the last good answer, when a refresh failed), or answered. A
 * TanStack Query result fits it.
 */
export type LoadState<T> =
  | { isPending: true; isError: false }
  | { isPending: false; isError: true; error: Error; data?: T; refetch: () => Promise<unknown> }
  | { isPending: false; isError: false; data: T }

/**
 * A load's skeleton, once it has taken 150 ms, or its error and Retry, else what `children` draws from its data. A
 * refresh that fails keeps the data on screen under the error. The skeleton takes the `shape` of what loads: `table`
 * for a list region, `rows` by default.
 */
export function QueryState<T>({
  query,
  label,
  rows,
  shape,
  children,
}: {
  query: LoadState<T>
  label: string
  rows: number
  shape?: SkeletonShape
  children: (data: T) => ReactNode
}) {
  const showLoading = useDelayed(query.isPending)

  if (query.isPending) {
    return showLoading ? <Skeleton label={label} rows={rows} shape={shape} /> : null
  }

  if (query.isError) {
    // A refresh that failed still holds the last good read: the rows stay under the error, as stale data does.
    const kept = query.data !== undefined
    const error = <LoadError message={query.error.message} kept={kept} onRetry={() => void query.refetch()} />

    return query.data === undefined ? (
      error
    ) : (
      <>
        {error}
        {children(query.data)}
      </>
    )
  }

  return children(query.data)
}
