import { useEffect, useSyncExternalStore } from 'react'
import { useDashboard } from '../context.tsx'
import type { StreamEventMap, StreamEventName, StreamStatus } from './types.ts'

/** Keeps `topics` subscribed on the shared stream while the calling view is mounted. */
export function useTopics(topics: readonly string[]) {
  const { stream } = useDashboard()
  // A stable key, so a new array with the same topics does not resubscribe.
  const key = [...topics].sort().join(',')

  useEffect(() => (key ? stream.subscribe(key.split(',')) : undefined), [stream, key])
}

/** Calls `handler` for each `type` event while the calling view is mounted. */
export function useStreamEvent<K extends StreamEventName>(type: K, handler: (data: StreamEventMap[K]) => void) {
  const { stream } = useDashboard()

  useEffect(() => stream.on(type, handler), [stream, type, handler])
}

/** The stream's status and, while stale, since when. */
export function useStreamStatus(): { status: StreamStatus; staleSince: number | null } {
  const { stream } = useDashboard()
  const status = useSyncExternalStore(
    (listener) => stream.onStatusChange(listener),
    () => stream.getStatus(),
  )
  const staleSince = useSyncExternalStore(
    (listener) => stream.onStatusChange(listener),
    () => stream.getStaleSince(),
  )

  return { status, staleSince }
}
