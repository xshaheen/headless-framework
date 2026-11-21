import { useDashboard } from '@headless/dashboard-sdk'
import { useQuery } from '@headless/dashboard-sdk/query'
import { nodeQuery } from './messages.ts'

/** How hosts share a sequential consumer, as the host manifest's `activeHost` reports it. */
export const sequentialIntakes = ['keyRouted', 'broker', 'lock', 'none'] as const

export type SequentialIntake = (typeof sequentialIntakes)[number]

/** Who holds one subscription of a sequential consumer that the messaging lock elects a host for. */
export type IntakeHolder =
  | { state: 'held'; subscription: string; node: string; waiting?: number }
  | { state: 'none'; subscription: string }
  | { state: 'unavailable'; subscription: string; reason: string }

/** A sequential consumer's holders, or the serving node's waiting count when no one host holds it. */
export interface SequentialState {
  intake?: SequentialIntake
  holders: IntakeHolder[]
  waiting?: number
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function count(value: unknown) {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 ? value : undefined
}

function readHolder(value: unknown): IntakeHolder {
  const subscription = isRecord(value) && typeof value.subscription === 'string' ? value.subscription : ''

  if (isRecord(value) && value.state === 'held' && typeof value.node === 'string') {
    return { state: 'held', subscription, node: value.node, waiting: count(value.waiting) }
  }

  if (isRecord(value) && value.state === 'none') {
    return { state: 'none', subscription }
  }

  // Anything the view cannot read is shown as unknown, never as no holder.
  const reason = isRecord(value) && typeof value.reason === 'string' ? value.reason : 'The node sent no holder.'

  return { state: 'unavailable', subscription, reason }
}

/** Narrows the server's answer, so a malformed holder reads as unknown rather than as none. */
export function readSequentialState(value: unknown): SequentialState {
  const record = isRecord(value) ? value : {}

  return {
    intake: sequentialIntakes.find((intake) => intake === record.intake),
    holders: Array.isArray(record.holders) ? record.holders.map(readHolder) : [],
    waiting: count(record.waiting),
  }
}

/**
 * The host that holds a sequential consumer's subscription and the messages waiting to run there. Read each time the
 * consumer's panel opens, since the holder moves when a host stops or loses its lease and no change announces it.
 */
export function useSequentialState(consumer: { identity: string; lane: string }, node: string, isEnabled: boolean) {
  const { api } = useDashboard()

  return useQuery({
    queryKey: ['messaging', 'sequential', node, consumer.lane, consumer.identity],
    queryFn: async () =>
      readSequentialState(
        await api.request<unknown>({
          method: 'GET',
          path: 'messaging/consumers/sequential',
          query: { identity: consumer.identity, lane: consumer.lane.toLowerCase(), ...nodeQuery(node) },
        }),
      ),
    enabled: isEnabled,
    staleTime: 0,
    refetchOnMount: 'always',
  })
}
