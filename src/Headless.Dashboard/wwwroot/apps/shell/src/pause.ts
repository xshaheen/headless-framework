import type { DashboardStream, StreamEventName } from '@headless/dashboard-sdk/stream'

/** The stream every view reads, plus this tab's pause: data changes wait in order until Resume applies them. */
export interface PausableStream extends DashboardStream {
  setPaused(paused: boolean): void
  /** How many updates wait while paused; `null` while live. */
  getPending(): number | null
  onPauseChange(listener: () => void): () => void
}

// Data changes wait while paused. A stale or session signal is not data, and the operator's own command results come
// back through the API, not the stream, so both still apply at once.
const held: ReadonlySet<StreamEventName> = new Set(['change', 'attention.changed', 'reset', 'subscribed'])

/**
 * Wraps the shared stream so this tab can pause applying data changes. The connection stays open for the other tabs;
 * this tab only holds what arrives, and Resume replays it through the same handlers, in the order it came.
 */
export function pausable(stream: DashboardStream): PausableStream {
  const handlers = new Map<StreamEventName, Set<(data: never) => void>>()
  const listeners = new Set<() => void>()
  let queue: [StreamEventName, unknown][] | null = null

  const dispatch = (type: StreamEventName, data: unknown) => handlers.get(type)?.forEach((handler) => handler(data as never))
  const notify = () => listeners.forEach((listener) => listener())

  return {
    subscribe: (topics) => stream.subscribe(topics),
    getStatus: () => stream.getStatus(),
    getStaleSince: () => stream.getStaleSince(),
    onStatusChange: (listener) => stream.onStatusChange(listener),
    reconnect: () => stream.reconnect(),

    on(type, handler) {
      const own = handlers.get(type) ?? new Set<(data: never) => void>()

      if (!handlers.has(type)) {
        handlers.set(type, own)
        // One listener per event type on the shared stream, for the tab's lifetime; handlers come and go below it.
        stream.on(type, (data) => {
          if (queue && held.has(type)) {
            queue.push([type, data])
            notify()
          } else {
            dispatch(type, data)
          }
        })
      }

      own.add(handler as (data: never) => void)

      return () => {
        own.delete(handler as (data: never) => void)
      }
    },

    setPaused(paused) {
      if (paused === (queue !== null)) {
        return
      }

      const waiting = queue
      queue = paused ? [] : null
      waiting?.forEach(([type, data]) => dispatch(type, data))
      notify()
    },

    // A reopened stream's `subscribed` is bookkeeping, not an update the operator would count.
    getPending: () => (queue ? queue.filter(([type]) => type !== 'subscribed').length : null),

    onPauseChange(listener) {
      listeners.add(listener)

      return () => {
        listeners.delete(listener)
      }
    },
  }
}
