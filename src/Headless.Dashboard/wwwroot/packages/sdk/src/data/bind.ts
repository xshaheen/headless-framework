import type { QueryClient } from '@tanstack/react-query'
import type { DashboardStream } from '../stream/types.ts'
import { applyChange, refetchTopics } from './patch.ts'

/**
 * Keeps the data client current from the stream: changes patch the cache, `reset` refetches every live query, and
 * `subscribed` refetches only the topics that just became live, whose views fetched before the stream carried them.
 * Returns the call that stops it.
 */
export function bindStreamToClient(stream: DashboardStream, client: QueryClient) {
  const stops = [
    stream.on('change', (change) => {
      applyChange(client, change)
    }),
    stream.on('reset', () => {
      void refetchTopics(client)
    }),
    stream.on('subscribed', ({ added }) => {
      void refetchTopics(client, added)
    }),
  ]

  return () => stops.forEach((stop) => stop())
}
