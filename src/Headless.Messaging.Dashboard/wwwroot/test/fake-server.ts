import type { CommandDefinition } from '@headless/dashboard-sdk/commands'
import { DashboardApiError, type ApiRequest } from '@headless/dashboard-sdk/data'
import { renderModule, type RenderModuleOptions } from '@headless/dashboard-sdk/testing'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type { MessageKind, MessageRow } from '../src/messages.ts'
import messaging from '../src/module.ts'
import type { MessagingCircuit, MessagingConsumer, MessagingContract, MessagingNodeList, RealtimeMetrics } from '../src/parts.tsx'
import type { ScheduledList } from '../src/views/scheduled.tsx'

export const now = new Date('2026-11-05T10:00:00Z')

let sequence = 0

/** A stored row on node-a's primary store; a failed one has run out of retries unless `terminal` says otherwise. */
export function message(kind: MessageKind, status: NonNullable<MessageRow['status']>, overrides: Partial<MessageRow> = {}): MessageRow {
  sequence += 1
  const store = overrides.store ?? 'primary'

  return {
    id: `node-a~${store}~${sequence}`,
    store,
    storageId: String(sequence),
    messageId: `m-${sequence}`,
    name: 'OrderPlaced',
    lane: 'default',
    status,
    consumer: kind === 'received' ? 'Billing.OrderPlacedConsumer' : undefined,
    retries: status === 'Failed' ? 3 : 0,
    added: '2026-11-05T09:50:00Z',
    changedAt: '2026-11-05T09:56:00Z',
    failureFingerprint: status === 'Failed' ? 'v1:9f2c41' : undefined,
    isHeld: kind === 'received' ? false : undefined,
    terminal: status === 'Failed',
    version: '1',
    ...overrides,
  }
}

export const localNodes: MessagingNodeList = { items: [{ name: 'node-a', isLocal: true, stores: ['primary'] }], discovery: null }

export const twoNodes: MessagingNodeList = {
  items: [
    { name: 'node-a', isLocal: true, stores: ['primary'] },
    { name: 'node-b', isLocal: false, stores: ['primary', 'orders'], state: 'answering' },
  ],
  discovery: '2 nodes',
}

const base = {
  permission: 'operate' as const,
  typedConfirmationAbove: 100,
  canRun: true,
  arguments: { type: 'object' as const, required: [], properties: {} },
}

function definition(id: string, label: string, objectKind: string, confirmation: 'confirm' | 'none', supportsFilter = false): CommandDefinition {
  return { ...base, id: `messaging.${id}`, label, objectKind, confirmation, requiresReason: confirmation === 'confirm', supportsFilter }
}

export const commands: CommandDefinition[] = [
  definition('re-execute', 'Re-execute', 'received', 'confirm', true),
  definition('hold', 'Hold', 'received', 'confirm'),
  definition('release', 'Release', 'received', 'confirm'),
  definition('purge', 'Purge', 'received', 'confirm', true),
  definition('re-send', 'Re-send', 'published', 'confirm', true),
  definition('dispatch-now', 'Dispatch now', 'published', 'confirm'),
  definition('revoke', 'Revoke', 'published', 'confirm'),
  definition('delete', 'Delete', 'published', 'confirm', true),
  definition('reset-circuit', 'Reset circuit', 'circuit', 'none'),
  definition('force-open', 'Force open', 'circuit', 'confirm'),
  definition('reset-backpressure', 'Reset backpressure', 'node', 'confirm'),
]

/** Why the fake server refuses `commandId` on a row now, as the module's rules would. */
function refusal(row: MessageRow | undefined, commandId: string): string | undefined {
  if (!row) {
    return undefined
  }

  if (commandId === 'messaging.re-execute' || commandId === 'messaging.re-send') {
    return row.status === 'Failed' ? undefined : 'Not failed'
  }

  return undefined
}

export const consumer: MessagingConsumer = {
  identity: 'Billing.OrderPlacedConsumer',
  lane: 'default',
  delivery: 'competing',
  runState: 'runs-here',
  messages: [{ name: 'OrderPlaced', contractVersion: '1' }],
  concurrency: 4,
}

export function circuit(overrides: Partial<MessagingCircuit> = {}): MessagingCircuit {
  return {
    id: 'node-a~Billing.OrderPlacedConsumer',
    consumer: 'Billing.OrderPlacedConsumer',
    state: 'Open',
    consecutiveFailures: 5,
    failureThreshold: 5,
    openedAt: '2026-11-05T09:59:00Z',
    estimatedRemainingOpenDuration: '00:00:45',
    ...overrides,
  }
}

export interface FakeServer {
  published?: MessageRow[]
  received?: MessageRow[]
  /** The sidebar counts by key, as `GET api/counts?product=messaging` answers them. */
  counts?: Readonly<Record<string, number>>
  /** The Messaging attention items, as `GET api/needs-you` serves them. */
  attention?: unknown[]
  nodes?: MessagingNodeList
  consumers?: MessagingConsumer[]
  contracts?: MessagingContract[]
  circuits?: MessagingCircuit[]
  scheduled?: ScheduledList
  realtime?: RealtimeMetrics
  receipts?: unknown[]
  extra?: (request: ApiRequest) => unknown
}

function matches(row: MessageRow, query: Readonly<Record<string, string | string[] | undefined>>) {
  return (
    (!query.store || row.store === query.store) &&
    (!query.status || row.status === query.status) &&
    (!query.terminal || row.terminal) &&
    (!query.retrying || (row.status === 'Failed' && !row.terminal)) &&
    (!query.consumer || row.consumer === query.consumer) &&
    (!query.name || row.name === query.name) &&
    (!query.fingerprint || row.failureFingerprint === query.fingerprint) &&
    (!query.held || row.isHeld === true)
  )
}

/** A fake of the Messaging module's API, the shell's aggregates it reads, and the shell's command endpoints for it. */
export function server({
  published = [],
  received = [],
  counts = {},
  attention = [],
  nodes = localNodes,
  consumers = [consumer],
  contracts = [],
  circuits = [],
  scheduled = { store: 'primary', supported: true, items: [], totalCount: 0 },
  realtime = { seconds: [], publishedPerSecond: [], consumedPerSecond: [], consumeElapsedMs: [] },
  receipts = [],
  extra,
}: FakeServer) {
  const stored: Readonly<Record<MessageKind, MessageRow[]>> = { published, received }

  return (request: ApiRequest) => {
    const answered = extra?.(request)

    if (answered !== undefined) {
      return answered
    }

    const query = (request.query ?? {}) as Readonly<Record<string, string | string[] | undefined>>

    if (request.path === 'counts') {
      return {
        counts: Object.fromEntries(Object.entries(counts).map(([key, count]) => [key, { count, capped: false }])),
        stale: [],
      }
    }

    if (request.path === 'needs-you') {
      return { modules: [{ id: 'messaging', displayName: 'Messaging', stale: false, items: attention }] }
    }

    if (request.method === 'GET' && (request.path === 'messaging/published' || request.path === 'messaging/received')) {
      const kind = request.path === 'messaging/published' ? 'published' : 'received'
      const items = stored[kind].filter((row) => matches(row, query))

      return { items, count: items.length, countCapped: false }
    }

    const one = /^messaging\/messages\/(published|received)\/([^/]+)$/.exec(request.path)

    if (request.method === 'GET' && one) {
      const kind = one[1] as MessageKind
      const row = stored[kind].find((candidate) => candidate.id === decodeURIComponent(one[2]!))

      if (!row) {
        throw new DashboardApiError(404, 'Not found')
      }

      return { row, headers: {}, content: '' }
    }

    if (request.method === 'GET' && request.path === 'messaging/received/generations') {
      return { items: received.filter((row) => row.messageId === query.messageId) }
    }

    switch (request.path) {
      case 'messaging/nodes':
        return nodes
      case 'messaging/consumers':
        return { items: consumers }
      case 'messaging/contracts':
        return { items: contracts }
      case 'messaging/circuits':
        return { items: circuits }
      case 'messaging/scheduled':
        return scheduled
      case 'messaging/metrics/realtime':
        return realtime
      case 'messaging/receipts':
        return { items: receipts, nextCursor: null }
      case 'messaging/commands':
        return { commands }
      default:
        break
    }

    if (request.path === 'messaging/commands/allowed') {
      const ids = [query.id ?? []].flat()
      const rows = [...published, ...received]

      return {
        objects: ids.map((id) => ({
          kind: query.kind,
          id,
          commands: commands.map((command) => {
            const reason = refusal(
              rows.find((candidate) => candidate.id === id),
              command.id,
            )

            return { id: command.id, allowed: !reason, disabledReason: reason }
          }),
        })),
      }
    }

    const command = /^messaging\/commands\/([^/]+)$/.exec(request.path)

    if (request.method === 'POST' && command) {
      return { rows: (request.body as { ids: string[] }).ids.map((id) => ({ id, outcome: 'done' })) }
    }

    throw new DashboardApiError(404, `No fake for ${request.method} ${request.path}`)
  }
}

export function open(fake: FakeServer, options: RenderModuleOptions = {}) {
  return renderModule(messaging, { api: server(fake), ...options })
}

export function change(kind: MessageKind, row: MessageRow, node = '') {
  return { module: 'messaging', kind, id: row.id, topic: node ? `messaging.${kind}@${node}` : `messaging.${kind}`, version: row.version ?? '1', projection: { ...row } }
}

export function user() {
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
}

export function described(button: HTMLElement) {
  return document.getElementById(button.getAttribute('aria-describedby')!)?.textContent
}
