import { act } from '@testing-library/react'
import type { DashboardStream, StreamEventMap, StreamEventName, StreamStatus } from '../stream/types.ts'

/** A stream a test drives by hand: it pushes events and sets the status, and reads the topics views subscribed. */
export interface FakeStream extends DashboardStream {
  push<K extends StreamEventName>(type: K, data: StreamEventMap[K]): void
  setStatus(status: StreamStatus): void
  /** The topics the mounted views hold now. */
  topics(): string[]
}

export function createFakeStream(): FakeStream {
  const topics = new Map<string, number>()
  const handlers = new Map<StreamEventName, Set<(data: never) => void>>()
  const statusListeners = new Set<() => void>()
  let status: StreamStatus = 'live'
  let staleSince: number | null = null

  return {
    subscribe(list) {
      list.forEach((topic) => topics.set(topic, (topics.get(topic) ?? 0) + 1))

      return () =>
        list.forEach((topic) => {
          const count = (topics.get(topic) ?? 1) - 1

          if (count > 0) {
            topics.set(topic, count)
          } else {
            topics.delete(topic)
          }
        })
    },
    on(type, handler) {
      const set = handlers.get(type) ?? new Set()
      handlers.set(type, set)
      set.add(handler as (data: never) => void)

      return () => {
        set.delete(handler as (data: never) => void)
      }
    },
    getStatus: () => status,
    getStaleSince: () => staleSince,
    onStatusChange(listener) {
      statusListeners.add(listener)

      return () => {
        statusListeners.delete(listener)
      }
    },
    reconnect() {},
    push(type, data) {
      act(() => {
        for (const handler of [...(handlers.get(type) ?? [])]) {
          ;(handler as (value: typeof data) => void)(data)
        }
      })
    },
    setStatus(next) {
      act(() => {
        status = next
        staleSince = next === 'stale' ? Date.now() : null
        statusListeners.forEach((listener) => listener())
      })
    },
    topics: () => [...topics.keys()].sort(),
  }
}
