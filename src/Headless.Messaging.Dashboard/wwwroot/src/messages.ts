import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { createCommandsApi, type CommandDefinition, type RowResult } from '@headless/dashboard-sdk/commands'
import type { DashboardStateRole } from '@headless/dashboard-sdk'
import {
  isNewerVersion,
  type CappedCount,
  type DashboardApi,
  type DashboardListPage,
} from '@headless/dashboard-sdk/data'
import type { ListPageRequest, ListRange } from '@headless/dashboard-sdk/table'
import { useQuery } from '@headless/dashboard-sdk/query'
import { useNavigate } from '@headless/dashboard-sdk/router'
import { useState } from 'react'

export type MessageKind = 'published' | 'received'

export const messageStatuses = ['Failed', 'Scheduled', 'Succeeded', 'Delayed', 'Queued'] as const

export type MessageStatus = (typeof messageStatuses)[number]

/** The lanes a list filters by, as the server names them. */
export const messageLanes = ['Bus', 'Queue'] as const

export type MessageLane = (typeof messageLanes)[number]

/**
 * One stored row, as a list, a by-id read, and a live change's projection carry it. Its id is
 * `{node}~{store}~{storageId}`. A list row carries no version; a live change does.
 */
export interface MessageRow {
  id: string
  store: string
  storageId: string
  messageId?: string
  name?: string
  lane: string
  status?: MessageStatus
  consumer?: string
  retries: number
  added: string
  expiresAt?: string
  nextRetryAt?: string
  changedAt?: string
  failureFingerprint?: string
  tenantId?: string
  /** Whether a received row is held; missing when the read did not say. */
  isHeld?: boolean
  /** Failed with no retry pending. */
  terminal: boolean
  version?: string
}

export interface MessageDetail {
  row: MessageRow
  headers: Readonly<Record<string, string | null>>
  content: string
  exceptionInfo?: string
}

/** A list's filter, as the URL carries it; the flags are `true` or empty. */
export interface MessageFilter {
  store: string
  status: MessageStatus | ''
  terminal: string
  retrying: string
  consumer: string
  name: string
  fingerprint: string
  held: string
  lane: MessageLane | ''
  messageId: string
}

// Typed as the whole filter, so a field added to MessageFilter must be listed here, and so in filterNames.
const emptyFilter: MessageFilter = {
  store: '',
  status: '',
  terminal: '',
  retrying: '',
  consumer: '',
  name: '',
  fingerprint: '',
  held: '',
  lane: '',
  messageId: '',
}

export const filterNames = Object.keys(emptyFilter) as (keyof MessageFilter)[]

const flags = new Set<keyof MessageFilter>(['terminal', 'retrying', 'held'])

export function readFilter(params: URLSearchParams, kind: MessageKind): MessageFilter {
  const filter = { ...emptyFilter }

  for (const name of filterNames) {
    const value = params.get(name) ?? ''

    if (name === 'status') {
      filter.status = messageStatuses.find((status) => status.toLowerCase() === value.toLowerCase()) ?? ''
    } else if (name === 'lane') {
      filter.lane = messageLanes.find((lane) => lane.toLowerCase() === value.toLowerCase()) ?? ''
    } else if (flags.has(name)) {
      // A flag reads only as `true`, the one value the server reads as set.
      filter[name] = value.toLowerCase() === 'true' ? 'true' : ''
    } else {
      filter[name] = value
    }
  }

  // A published row has no consumer and is never held; the server takes neither for it.
  if (kind === 'published') {
    filter.consumer = ''
    filter.held = ''
  } else {
    // Received rows live in the main store alone, so the received list takes no store.
    filter.store = ''
  }

  // The server refuses a retrying list with a status or terminal, so retrying wins as the sidebar's own list.
  if (filter.retrying) {
    filter.status = ''
    filter.terminal = ''
  }

  return filter
}

/**
 * Whether `row` matches `filter`, and the added time falls in `range`, as the server reads them, so a live change can
 * tell a row it left the filter. A search the browser cannot check, since a row carries no content, is the view's.
 */
export function messageMatches(row: MessageRow, filter: MessageFilter, range?: ListRange) {
  const added = Date.parse(row.added)

  return (
    (!range || (added >= Date.parse(range.start) && added < Date.parse(range.end))) &&
    (!filter.lane || row.lane === filter.lane) &&
    (!filter.messageId || row.messageId === filter.messageId) &&
    (!filter.store || row.store === filter.store) &&
    (!filter.status || row.status === filter.status) &&
    (!filter.terminal || row.terminal) &&
    (!filter.retrying || (row.status === 'Failed' && !row.terminal)) &&
    (!filter.consumer || row.consumer === filter.consumer) &&
    (!filter.name || row.name === filter.name) &&
    (!filter.fingerprint || row.failureFingerprint === filter.fingerprint) &&
    (!filter.held || row.isHeld === true)
  )
}

/**
 * Whether an Operation can scope by `filter` on a `kind` list. An Operation's filter names a store, status, terminal,
 * consumer, name, fingerprint, lane, message id, and added range; it cannot say retrying or held, so those lists act on
 * the checked rows only. An Operation also runs on one store, so a published list that merges every store acts on the
 * checked rows until a store is picked: otherwise its count would cover rows the Operation never touches.
 */
export function canScope(filter: MessageFilter, kind: MessageKind) {
  return !filter.retrying && !filter.held && (kind !== 'published' || Boolean(filter.store))
}

/**
 * The filter as an Operation's scope, on `node`: the server freezes exactly the rows it matches at confirm. A search
 * travels in the confirm's body and the server's encrypted token, never a URL; the operator log records it redacted.
 */
export function operationScope(
  filter: MessageFilter,
  node: string,
  range?: ListRange,
  search: MessageSearch = noSearch,
) {
  return {
    ...(search.content ? { content: search.content } : {}),
    ...(search.correlationId ? { correlationId: search.correlationId } : {}),
    ...(filter.lane ? { lane: filter.lane } : {}),
    ...(filter.messageId ? { messageId: filter.messageId } : {}),
    ...(range ? { from: range.start, before: range.end } : {}),
    ...(node ? { node } : {}),
    ...(filter.store ? { store: filter.store } : {}),
    ...(filter.status ? { status: filter.status } : {}),
    ...(filter.terminal ? { terminalOnly: true } : {}),
    ...(filter.consumer ? { consumer: filter.consumer } : {}),
    ...(filter.name ? { name: filter.name } : {}),
    ...(filter.fingerprint ? { fingerprint: filter.fingerprint } : {}),
  }
}

/** Which of the sidebar's lists `filter` is: `failed`, `retrying`, `held`, a status, or `all`. */
export function listName(filter: MessageFilter) {
  if (filter.held) {
    return 'held'
  }

  if (filter.retrying) {
    return 'retrying'
  }

  return filter.status ? filter.status.toLowerCase() : 'all'
}

/** What the filtered rows are, for the confirm and progress text: `failed received messages of OrderPlaced`. */
export function messagesNoun(kind: MessageKind, filter: MessageFilter, node: string) {
  const list = listName(filter)
  const state = list === 'all' ? '' : `${list} `

  return `${state}${kind} messages${filter.name ? ` of ${filter.name}` : ''}${node ? ` on ${node}` : ''}`
}

const statusRoles: Readonly<Record<MessageStatus, DashboardStateRole>> = {
  Failed: 'fail',
  Scheduled: 'sched',
  Succeeded: 'ok',
  Delayed: 'sched',
  Queued: 'enq',
}

/**
 * The mark a row shows. A held row reads held whatever its status; a failure with a retry pending reads retrying. A
 * command the server finished reads its pending label until the stream moves the row's version, keeping the stored
 * status's role.
 */
export function rowMark(
  row: Pick<MessageRow, 'status' | 'terminal' | 'isHeld'>,
  pending?: string,
): { tone: DashboardStateRole; label: string } {
  const mark: { tone: DashboardStateRole; label: string } = row.isHeld
    ? { tone: 'off', label: 'Held' }
    : row.status === 'Failed' && !row.terminal
      ? { tone: 'proc', label: 'Retrying' }
      : row.status
        ? { tone: statusRoles[row.status] ?? 'off', label: row.status }
        : { tone: 'off', label: 'Unknown' }

  return pending ? { tone: mark.tone, label: pending } : mark
}

/** A node's stream topic for one kind: the local node's is `messaging.{kind}`, another's `messaging.{kind}@{node}`. */
export function topicOf(kind: MessageKind, node: string) {
  return node ? `messaging.${kind}@${node}` : `messaging.${kind}`
}

/** The query a read names its node with; the local node is the one that answers without it. */
export function nodeQuery(node: string): Record<string, string> {
  return node ? { node } : {}
}

interface MessageListAnswer {
  items: MessageRow[]
  count: number
  countCapped: boolean
  nextCursor?: string | null
  /** The stores that did not answer; their rows are missing from the page. */
  unavailable?: string[]
  /** A search that ran out of time in some store, whose matches may be missing from the page. */
  incomplete?: boolean
}

/** A list's search through stored messages. It lives in the view's state only: never the URL, the cursor, or a log. */
export interface MessageSearch {
  /** Text in the stored headers or body, as a literal substring, without regard to case. */
  content: string
  /** The correlation id header, exactly, without regard to case. */
  correlationId: string
}

export const noSearch: MessageSearch = { content: '', correlationId: '' }

/**
 * The search as the keyset list's `search`, which keys its cache and comes back in each page request: empty when
 * nothing is searched, so the list reads the plain route.
 */
export function searchKey(search: MessageSearch) {
  return search.content || search.correlationId ? JSON.stringify([search.content, search.correlationId]) : ''
}

function readSearchKey(key: string | undefined): MessageSearch {
  if (!key) {
    return noSearch
  }

  const parsed: unknown = JSON.parse(key)

  return Array.isArray(parsed) && typeof parsed[0] === 'string' && typeof parsed[1] === 'string'
    ? { content: parsed[0], correlationId: parsed[1] }
    : noSearch
}

export type MessagePage = DashboardListPage<MessageRow> & {
  count: CappedCount
  unavailable: string[]
  incomplete: boolean
}

const pageSize = 100
const flagFields = new Set<keyof MessageFilter>(['terminal', 'retrying', 'held'])

/**
 * One page of a list in the server order `request.sort` names, newest first by default: the first page, or the page
 * after the row `cursor` names. A search goes in the body of the list's search route, so its text never enters a URL.
 */
export async function fetchMessages(
  api: DashboardApi,
  kind: MessageKind,
  node: string,
  filter: MessageFilter,
  cursor?: string,
  request: Partial<Pick<ListPageRequest, 'sort' | 'search' | 'range'>> = {},
  limit = pageSize,
): Promise<MessagePage> {
  const fields: Record<string, string> = {}

  for (const name of filterNames) {
    const value = filter[name]

    if (value) {
      fields[name] = value
    }
  }

  if (request.sort) {
    fields.sort = request.sort.key
    fields.direction = request.sort.direction
  }

  if (request.range) {
    fields.from = request.range.start
    fields.before = request.range.end
  }

  if (cursor) {
    fields.after = cursor
  }

  const search = readSearchKey(request.search)
  const answer =
    search.content || search.correlationId
      ? await api.request<MessageListAnswer>({
          method: 'POST',
          path: `messaging/${kind}/search`,
          query: nodeQuery(node),
          // The body is JSON, so the flags travel as booleans and the limit as a number.
          body: {
            ...Object.fromEntries(
              Object.entries(fields).map(([name, value]) => [
                name,
                flagFields.has(name as keyof MessageFilter) ? value === 'true' : value,
              ]),
            ),
            limit,
            ...(search.content ? { content: search.content } : {}),
            ...(search.correlationId ? { correlationId: search.correlationId } : {}),
          },
        })
      : await api.request<MessageListAnswer>({
          method: 'GET',
          path: `messaging/${kind}`,
          query: { ...fields, limit: String(limit), ...nodeQuery(node) },
        })
  const unavailable = answer.unavailable ?? []
  const incomplete = answer.incomplete === true

  return {
    items: answer.items,
    // A store that did not answer, or a search that ran out of time, leaves rows out of the count: it reads as at least.
    count: { value: answer.count, capped: answer.countCapped || unavailable.length > 0 || incomplete },
    nextCursor: answer.nextCursor ?? null,
    unavailable,
    incomplete,
  }
}

/** One row with its headers, body, and failure, read on its node. */
export async function fetchMessage(api: DashboardApi, kind: MessageKind, node: string, id: string) {
  return api.request<MessageDetail>({
    method: 'GET',
    path: `messaging/messages/${kind}/${encodeURIComponent(id)}`,
    query: nodeQuery(node),
  })
}

/** An inbox generation of a received row, as the store's inbox operations keep it. */
export interface InboxGeneration {
  /** `0` for the first delivery, one more per re-execution. */
  number: number
  incarnationId: string
  origin: 'original' | 'replay'
  isCurrent: boolean
  replayOfIncarnationId?: string | null
  /** The object id of the row a replay re-executed, while the store keeps it. */
  replayOfId?: string | null
  /** When retention may delete the ended generation; absent while it runs. */
  expiresAt?: string | null
  /** `orphaned` while no host has the consumer registered. */
  recovery: 'orphaned' | 'routable'
}

/** A received row with its generation, when the store keeps one for it. */
export type GenerationRow = MessageRow & { generation?: InboxGeneration | null }

/** The rows of one received message across its consumers and re-executions, oldest first. */
export async function fetchGenerations(api: DashboardApi, node: string, messageId: string, consumer?: string) {
  const query = { messageId, ...(consumer ? { consumer } : {}), ...nodeQuery(node) }

  return (
    await api.request<{ items: GenerationRow[] }>({ method: 'GET', path: 'messaging/received/generations', query })
  ).items
}

export const viewOnlyReason = 'Needs operate permission'

export type MessageCommandName =
  | 're-execute'
  | 'purge'
  | 'hold'
  | 'release'
  | 're-send'
  | 'delete'
  | 'dispatch-now'
  | 'revoke'
  | 'reset-circuit'
  | 'force-open'
  | 'reset-backpressure'

/** The module's server commands as the server describes them to this user, with a viewer's commands disabled. */
export function useMessagingCommands() {
  const { api } = useDashboard()
  const module = useModule()
  const commands = useQuery({
    queryKey: ['commands', module.id],
    queryFn: () => createCommandsApi(api, module.id).list(),
    staleTime: Infinity,
  })
  const all = commands.data ?? []

  function find(name: MessageCommandName): CommandDefinition | undefined {
    const command = all.find((candidate) => candidate.id === `${module.id}.${name}`)

    if (!command || module.canOperate) {
      return command
    }

    return { ...command, canRun: false, disabledReason: command.disabledReason ?? viewOnlyReason }
  }

  return { all, find }
}

/** The words a row reads while a finished command waits for its change: `Re-executing…`. */
export const pendingLabels: Readonly<Partial<Record<MessageCommandName, string>>> = {
  're-execute': 'Re-executing…',
  purge: 'Purging…',
  hold: 'Holding…',
  release: 'Releasing…',
  're-send': 'Re-sending…',
  delete: 'Deleting…',
  'dispatch-now': 'Dispatching…',
  revoke: 'Revoking…',
}

/**
 * The rows a command finished on whose change has not arrived: each reads the command's pending label until its
 * version moves past the one it had when the operator ran it. A list row has no version, so the first change moves it.
 */
export function usePending() {
  const [pending, setPending] = useState<ReadonlyMap<string, { version: string; label: string }>>(new Map())

  return {
    labelOf: (row: Pick<MessageRow, 'id' | 'version'>) => {
      const entry = pending.get(row.id)

      // An entry marked on a row with no version holds `''`, which every version is newer than. A row read back from
      // the list carries no version again, so it matches only such an entry: a row whose change already arrived must
      // not read pending once a refetch drops its version.
      const isWaiting =
        row.version === undefined ? entry?.version === '' : !isNewerVersion(row.version, entry?.version ?? '')

      return entry && isWaiting ? entry.label : undefined
    },
    mark: (ids: readonly string[], versions: ReadonlyMap<string, string>, label: string) =>
      setPending((current) => {
        const next = new Map(current)

        for (const id of ids) {
          const version = versions.get(id)

          if (version !== undefined) {
            next.set(id, { version, label })
          }
        }

        return next
      }),
  }
}

export type Pending = ReturnType<typeof usePending>

export function versionsOf(rows: readonly Pick<MessageRow, 'id' | 'version'>[]) {
  return new Map(rows.map((row) => [row.id, row.version ?? '']))
}

/** A row action's `onDone`: the rows it finished on read its pending label until their change arrives. */
export function markDone(pending: Pending, versions: ReadonlyMap<string, string>, name: MessageCommandName) {
  const label = pendingLabels[name]

  return (rows: readonly RowResult[]) => {
    if (label) {
      pending.mark(
        rows.filter((row) => row.outcome === 'done').map((row) => row.id),
        versions,
        label,
      )
    }
  }
}

/** Opens a path of this module, such as a list filtered by a fingerprint. */
export function useModuleNavigate() {
  const navigate = useNavigate()
  const module = useModule()

  return (path: string) => void navigate(`/${module.id}/${path}`)
}

/** A failure group the attention aggregate counts, read from its id: `[{node}:]failed:{kind}:{fingerprint}:{consumer}:{store}`. */
export interface FailureGroup {
  node: string
  kind: MessageKind
  fingerprint: string
  consumer: string
  store: string
}

export function parseFailureGroup(id: string): FailureGroup | undefined {
  const parts = id.split(':')
  const at = parts.indexOf('failed')

  // A node name holds no colon, so the node is the one part before `failed`, when there is one.
  if (at < 0 || at > 1 || parts.length < at + 6) {
    return undefined
  }

  const kind = parts[at + 1]
  const version = parts[at + 2]!

  // A fingerprint is always `v{n}:{token}` with no colon in the token, so it spans exactly two parts.
  if ((kind !== 'published' && kind !== 'received') || !/^v\d+$/.test(version)) {
    return undefined
  }

  return {
    node: at === 1 ? parts[0]! : '',
    kind,
    fingerprint: `${version}:${parts[at + 3]!}`,
    // A consumer identity may hold a colon; the store, last, is a slug that holds none.
    consumer: parts.slice(at + 4, -1).join(':'),
    store: parts.at(-1)!,
  }
}

/** The list a failure group opens: the failed rows of its store, consumer, and fingerprint, on its node. */
export function failureGroupPath(group: FailureGroup) {
  const params = new URLSearchParams({ status: 'Failed', terminal: 'true' })

  if (group.fingerprint) {
    params.set('fingerprint', group.fingerprint)
  }

  if (group.kind === 'received') {
    if (group.consumer) {
      params.set('consumer', group.consumer)
    }
  } else {
    params.set('store', group.store)
  }

  if (group.node) {
    params.set('node', group.node)
  }

  return `${group.kind}?${params.toString()}`
}

/** A peer's node state, as the last forwarded call found it. */
const nodeStates: Readonly<Record<string, { tone: DashboardStateRole; label: string }>> = {
  answering: { tone: 'ok', label: 'Answering' },
  'not-answering': { tone: 'fail', label: 'Not answering' },
  'key-rejected': { tone: 'fail', label: 'Key rejected' },
  'policy-denied': { tone: 'fail', label: 'Policy denied' },
  'view-only': { tone: 'proc', label: 'View only' },
  'insecure-peer': { tone: 'fail', label: 'Insecure peer' },
}

export function nodeMark(node: { isLocal: boolean; state?: string | null }): {
  tone: DashboardStateRole
  label: string
} {
  if (node.isLocal) {
    return { tone: 'ok', label: 'This node' }
  }

  if (!node.state) {
    return { tone: 'sched', label: 'Not called yet' }
  }

  return nodeStates[node.state] ?? { tone: 'fail', label: node.state }
}

export const circuitTones: Readonly<Record<'Closed' | 'Open' | 'HalfOpen', DashboardStateRole>> = {
  Closed: 'ok',
  Open: 'fail',
  HalfOpen: 'proc',
}

/** A duration in seconds with its unit: `45 s`, `3 min`, `2 h`. */
export function formatSeconds(seconds: number, number: (value: number, options?: Intl.NumberFormatOptions) => string) {
  if (seconds < 60) {
    return `${number(seconds)} s`
  }

  if (seconds < 3_600) {
    return `${number(Math.round(seconds / 60))} min`
  }

  return `${number(seconds / 3_600, { maximumFractionDigits: 1 })} h`
}
