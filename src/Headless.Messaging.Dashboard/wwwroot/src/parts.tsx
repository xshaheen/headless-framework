import { useDashboard, useModule, type DashboardStateRole } from '@headless/dashboard-sdk'
import { CommandAction, useAllowedActions, type CommandDefinition } from '@headless/dashboard-sdk/commands'
import {
  Button,
  DetailPanel,
  ExceptionBlock,
  FieldGrid,
  GridField,
  HistoryList,
  Mono,
  NodeChip,
  PayloadView,
  SelectField,
  StatCard,
  StatStrip,
  useDelayed,
  type HistoryItem,
} from '@headless/dashboard-sdk/components'
import { DashboardApiError, refetchTopics, type CappedCount } from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery, useQueryClient } from '@headless/dashboard-sdk/query'
import { Link, useSearchParams } from '@headless/dashboard-sdk/router'
import { useStreamEvent, useStreamStatus } from '@headless/dashboard-sdk/stream'
import { useEffect, useState, type ReactNode } from 'react'
import {
  fetchGenerations,
  fetchMessage,
  fetchMessages,
  markDone,
  nodeQuery,
  readFilter,
  rowMark,
  topicOf,
  useMessagingCommands,
  versionsOf,
  type MessageCommandName,
  type MessageKind,
  type MessageRow,
  type Pending,
} from './messages.ts'

/**
 * A timestamp as relative time, with the exact time, zone, and offset on hover. `short` reads `4 min. ago`, so a list
 * column stays narrow enough for a phone.
 */
export function Time({ value, short, missing = 'Not yet' }: { value: string | undefined | null; short?: boolean; missing?: string }) {
  const format = useFormat()

  if (!value) {
    return <span className="hd-hint">{missing}</span>
  }

  return (
    <time dateTime={value} title={format.exactTime(value)}>
      {format.relativeTime(value, short ? { style: 'short' } : undefined)}
    </time>
  )
}

/**
 * A list cell's content, with `title` giving the full text on hover. The table ends a cell's only child in an
 * ellipsis when the column is too narrow for it.
 */
export function CellText({ title, children }: { title?: string; children: ReactNode }) {
  return <span title={title}>{children}</span>
}

/** The skeleton of a list or page, shown only once its load has taken 150 ms. */
export function Skeleton({ label, rows = 3 }: { label: string; rows?: number }) {
  return (
    <div role="status" aria-busy="true" aria-label={label} className="hd-fields">
      {Array.from({ length: rows }, (_, index) => (
        <div key={index} className="hd-skeleton" />
      ))}
    </div>
  )
}

/** A failed load: the error and Retry, never an empty view. */
export function LoadError({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="hd-banner">
      <span role="alert" className="hd-error">
        {message}
      </span>
      <Button onPress={onRetry}>Retry</Button>
    </div>
  )
}

type LoadState<T> =
  | { isPending: true; isError: false }
  | { isPending: false; isError: true; error: Error; refetch: () => Promise<unknown> }
  | { isPending: false; isError: false; data: T }

/** A load's skeleton, once it has taken 150 ms, or its error and Retry, else what `children` draws from its data. */
export function QueryState<T>({
  query,
  label,
  rows,
  children,
}: {
  query: LoadState<T>
  label: string
  rows: number
  children: (data: T) => ReactNode
}) {
  const showLoading = useDelayed(query.isPending)

  if (query.isPending) {
    return showLoading ? <Skeleton label={label} rows={rows} /> : null
  }

  if (query.isError) {
    return <LoadError message={query.error.message} onRetry={() => void query.refetch()} />
  }

  return children(query.data)
}

/** `Stale` while the stream is down, or the server reports this node's Messaging changes stopped; the data is kept. */
export function StaleNotice({ what, onRefresh }: { what: string; onRefresh: () => void }) {
  const stale = useStale()

  if (!stale) {
    return null
  }

  return (
    <div className="hd-banner" role="status">
      <span>{`Stale. Live updates stopped, so ${what} may be out of date.`}</span>
      <Button variant="ghost" onPress={onRefresh}>
        Refresh
      </Button>
    </div>
  )
}

/**
 * Whether what the view shows may be out of date: the stream is down, this module's changes stopped, or the node the
 * view reads stopped sending its changes.
 */
export function useStale() {
  const module = useModule()
  const { node } = useNode()
  const { status } = useStreamStatus()
  const [moduleStale, setModuleStale] = useState(false)
  const [staleNodes, setStaleNodes] = useState<ReadonlySet<string>>(new Set())

  useStreamEvent('module.stale', (event) => {
    if (event.module === module.id) {
      setModuleStale(event.stale)
    }
  })

  useStreamEvent('node.stale', (event) => {
    if (event.module === module.id) {
      setStaleNodes((current) => {
        const next = new Set(current)

        if (event.stale) {
          next.add(event.node)
        } else {
          next.delete(event.node)
        }

        return next
      })
    }
  })

  return status === 'stale' || moduleStale || (node !== '' && staleNodes.has(node))
}

export interface MessagingNode {
  name: string
  isLocal: boolean
  stores: string[]
  /** What the last forwarded call to the node found, such as `not-answering`; none before the first call. */
  state?: string | null
}

export interface MessagingNodeList {
  items: MessagingNode[]
  /** What the last discovery read found; missing when forwarding is off. */
  discovery?: string | null
}

/** The nodes serving the module, as this node knows them: itself and the nodes discovery lists. */
export function useNodes() {
  const { api } = useDashboard()

  return useQuery({
    queryKey: ['messaging', 'nodes'],
    // The local node answers who it is, so the read never names a node.
    queryFn: () => api.request<MessagingNodeList>({ method: 'GET', path: 'messaging/nodes' }),
  })
}

/**
 * The node last chosen in this dashboard session, by the session's query client. The shell builds the sidebar,
 * palette, and command links from fixed routes that carry no `node`, so without it every such link would fall back to
 * the local node. A fresh load starts empty, so a shared link without `node` still reads the local node.
 */
const chosenNodes = new WeakMap<object, string>()

/**
 * The node the views read: the URL's `node`, else the node last chosen in this session, else empty for the local
 * node. The local node's own name reads as empty, so its views share one cache and one topic whichever link opened
 * them.
 */
export function useNode() {
  const [params] = useSearchParams()
  const client = useQueryClient()
  const nodes = useNodes()
  const inUrl = params.get('node')
  const requested = inUrl ?? chosenNodes.get(client) ?? ''
  const local = nodes.data?.items.find((item) => item.isLocal)
  const node = local && requested === local.name ? '' : requested

  useEffect(() => {
    // A link that names its node, such as an object's, is a choice too.
    if (inUrl !== null) {
      chosenNodes.set(client, inUrl)
    }
  }, [client, inUrl])

  return {
    node,
    nodes,
    local,
    // Discovery answers only while forwarding is on; without it every read stays on the local node.
    forwarding: nodes.data ? nodes.data.discovery !== undefined && nodes.data.discovery !== null : false,
  }
}

/**
 * Which node the views read, and the switch between nodes when forwarding is on; without it, the local node's name.
 * A dashboard-only host serves no node of its own, so it opens on the first node discovery lists. A view reached by a
 * link without `node` gets the session's node written back to its URL, so the URL keeps naming what the view shows.
 */
export function NodeSwitch() {
  const [params, setParams] = useSearchParams()
  const client = useQueryClient()
  const { node, nodes, local, forwarding } = useNode()
  const peers = (nodes.data?.items ?? []).filter((item) => !item.isLocal)
  const first = peers[0]?.name
  const needsPeer = nodes.data !== undefined && !local && node === ''
  const missing = params.get('node') === null ? node || (needsPeer ? first : undefined) : undefined

  useEffect(() => {
    if (missing) {
      setParams(
        (current) => {
          const next = new URLSearchParams(current)
          next.set('node', missing)

          return next
        },
        { replace: true },
      )
    }
  }, [missing, setParams])

  if (!nodes.data) {
    return null
  }

  if (needsPeer && !first) {
    return (
      <span role="alert" className="hd-error">
        No Messaging node reachable
      </span>
    )
  }

  if (!forwarding) {
    return local ? <NodeChip node={local.name} /> : null
  }

  const options = nodes.data.items.map((item) => ({ value: item.name, label: item.isLocal ? `${item.name} (this node)` : item.name }))

  // A switch is a new place to go back from, and the open row belongs to the node left behind.
  function choose(value: string) {
    const toLocal = !value || value === local?.name
    chosenNodes.set(client, toLocal ? '' : value)
    setParams((current) => {
      const next = new URLSearchParams(current)
      next.delete('open')

      if (toLocal) {
        next.delete('node')
      } else {
        next.set('node', value)
      }

      return next
    })
  }

  return <SelectField label="Node" isRequired value={node || (local?.name ?? '')} onChange={choose} options={options} />
}

/**
 * Refetches a node's lists when the server says that node's changes restarted, since changes sent while it was away
 * never arrived.
 */
export function useNodeReset() {
  const module = useModule()
  const client = useQueryClient()

  useStreamEvent('node.reset', (event) => {
    if (event.module === module.id) {
      void refetchTopics(client, [topicOf('published', event.node), topicOf('received', event.node)])
      void client.invalidateQueries({ queryKey: ['messaging', 'counts', event.node] })
    }
  })
}

/**
 * A product's sidebar counts by count key, and the modules whose counts did not answer: the shell sidebar's cached
 * answer for `GET api/counts`, field for field. Whichever of the two fetches first fills the cache the other reads, so
 * a different shape here breaks the sidebar.
 */
interface ProductCounts {
  counts: Readonly<Record<string, CappedCount & { unavailable?: string[] }>>
  stale: readonly { id: string; displayName: string }[]
}

/** The counts the strip shows for another node, which the shell's counts do not cover. */
const peerCounts: readonly { key: string; kind: MessageKind; params: string }[] = [
  { key: 'received.failed', kind: 'received', params: 'status=Failed&terminal=true' },
  { key: 'received.retrying', kind: 'received', params: 'retrying=true' },
  { key: 'published.failed', kind: 'published', params: 'status=Failed&terminal=true' },
  { key: 'published.retrying', kind: 'published', params: 'retrying=true' },
]

/**
 * The Messaging counts of the node the views read. The local node's query key and answer shape are the shell
 * sidebar's own, so the strip and the sidebar read one cached answer. Another node's are read from its lists, one
 * row each, since the shell counts only the node that serves it.
 */
export function useMessagingCounts() {
  const { api } = useDashboard()
  const { node } = useNode()

  return useQuery({
    queryKey: node ? ['messaging', 'counts', node] : ['shell', 'counts', 'messaging'],
    staleTime: 0,
    queryFn: async (): Promise<ProductCounts> => {
      if (node) {
        const read = await Promise.all(
          peerCounts.map(async ({ key, kind, params }) => {
            const page = await fetchMessages(api, kind, node, readFilter(new URLSearchParams(params), kind), undefined, 1)

            return [key, page.count] as const
          }),
        )

        return { counts: Object.fromEntries(read), stale: [] }
      }

      const answer = await api.request<{
        counts: Record<string, { count: number; capped: boolean; unavailable?: string[] }>
        stale: { id: string; displayName: string }[]
      }>({
        method: 'GET',
        path: 'counts',
        query: { product: 'messaging' },
      })

      return {
        counts: Object.fromEntries(
          // A count that leaves sources out is a floor, so it reads `N+` as a capped one does.
          Object.entries(answer.counts).map(([key, { count, capped, unavailable }]) => [key, { value: count, capped: capped || !!unavailable, unavailable }]),
        ),
        stale: answer.stale,
      }
    },
  })
}

export interface AttentionItem {
  id: string
  title: string
  severity: 'critical' | 'warning'
  count: number
  capped: boolean
  lastSeenAt?: string | null
  target?: { kind: string; id: string } | null
}

interface AttentionSection {
  id: string
  displayName: string
  /** The module timed out or failed, so `items` says nothing. */
  stale: boolean
  /** The stores or nodes the module answered without, so `items` may be missing some. */
  unavailable?: string[]
  items: AttentionItem[]
}

/**
 * The Messaging items of the shell's Attention aggregate. The query key and answer shape are the Attention pill's own,
 * so both read one cached answer and the pill's refetch on `attention.changed` updates these too. `complete` holds only
 * when every source answered: an empty list means nothing needs attention only then.
 */
export function useMessagingAttention() {
  const { api } = useDashboard()
  const answer = useQuery({
    queryKey: ['shell', 'attention'],
    queryFn: async () => (await api.request<{ modules: AttentionSection[] }>({ method: 'GET', path: 'needs-you' })).modules,
  })
  const section = answer.data?.find((candidate) => candidate.id === 'messaging')
  // No Messaging section in a successful answer means the module did not answer at all.
  const stale = answer.data !== undefined && (!section || section.stale)
  const unavailable = section?.unavailable ?? []

  return {
    answer,
    items: stale ? [] : (section?.items ?? []),
    stale,
    unavailable,
    complete: answer.data !== undefined && !stale && unavailable.length === 0,
  }
}

/** Why the Attention answer cannot be trusted as complete: stale with Retry, or the sources it left out. */
export function AttentionGaps({ stale, unavailable, onRetry }: { stale: boolean; unavailable: string[]; onRetry: () => void }) {
  if (stale) {
    return <LoadError message="Messaging did not answer, so what needs attention is unknown." onRetry={onRetry} />
  }

  if (unavailable.length > 0) {
    return (
      <div className="hd-banner" role="status">
        <span>{`Not answering: ${unavailable.join(', ')}. Items from these may be missing.`}</span>
        <Button variant="ghost" onPress={onRetry}>
          Retry
        </Button>
      </div>
    )
  }

  return null
}

export interface RealtimeMetrics {
  seconds: number[]
  publishedPerSecond: (number | null)[]
  consumedPerSecond: (number | null)[]
  consumeElapsedMs: (number | null)[]
}

const realtimeWindow = 60

/** The mean of the measured seconds of the last minute; `undefined` when none was measured. */
export function lastMinuteRate(values: readonly (number | null)[]) {
  const measured = values.slice(-realtimeWindow).filter((value): value is number => value !== null)

  return measured.length > 0 ? measured.reduce((sum, value) => sum + value, 0) / measured.length : undefined
}

/** The node's throughput, second by second for five minutes; the server measures it in memory, so it is polled. */
export function useRealtime() {
  const { api } = useDashboard()
  const { node } = useNode()

  return useQuery({
    queryKey: ['messaging', 'realtime', node],
    queryFn: () => api.request<RealtimeMetrics>({ method: 'GET', path: 'messaging/metrics/realtime', query: nodeQuery(node) }),
    refetchInterval: 5_000,
  })
}

const stripCounts: readonly { key: string; label: string; tone: DashboardStateRole; to: string }[] = [
  { key: 'received.failed', label: 'Failed received', tone: 'fail', to: 'received?status=Failed&terminal=true' },
  { key: 'received.retrying', label: 'Retrying received', tone: 'proc', to: 'received?retrying=true' },
  { key: 'published.failed', label: 'Failed published', tone: 'fail', to: 'published?status=Failed&terminal=true' },
  { key: 'published.retrying', label: 'Retrying published', tone: 'proc', to: 'published?retrying=true' },
]

/** A count or rate still loading, failed, or unmeasured reads as an en dash, never 0. */
export function MessagingStats() {
  const format = useFormat()
  const { node } = useNode()
  const counts = useMessagingCounts()
  const realtime = useRealtime()
  const nodeSuffix = node ? `&node=${encodeURIComponent(node)}` : ''

  function rate(values: readonly (number | null)[] | undefined) {
    const value = values ? lastMinuteRate(values) : undefined

    return value === undefined ? '–' : format.number(value, { maximumFractionDigits: 1 })
  }

  return (
    <StatStrip label="Messaging at a glance">
      {stripCounts.map(({ key, label, tone, to }) => {
        const count = counts.data?.counts[key]

        return (
          <StatCard
            key={key}
            label={label}
            tone={tone}
            value={count ? format.count(count) : '–'}
            to={`/messaging/${to}${nodeSuffix}`}
          />
        )
      })}
      <StatCard label="Published/s" value={rate(realtime.data?.publishedPerSecond)} caption="Last minute" />
      <StatCard label="Received/s" value={rate(realtime.data?.consumedPerSecond)} caption="Last minute" />
    </StatStrip>
  )
}

/** The stored exception's first line, and the rest of it as the frames under it. */
function exceptionParts(info: string) {
  const [first = '', ...rest] = info.split('\n')
  // A stored stack's lines start `at`; the block adds its own.
  const frames = rest.map((line) => line.trim().replace(/^at /, '')).filter(Boolean)

  return { message: first, frames }
}

function Optional({ label, value, span }: { label: string; value: ReactNode; span?: 2 }) {
  return value === undefined || value === null || value === '' ? null : (
    <GridField label={label} span={span}>
      {value}
    </GridField>
  )
}

/** A row's own fields, which a list row and a live change carry. */
export function MessageFields({ kind, row }: { kind: MessageKind; row: MessageRow }) {
  const format = useFormat()

  return (
    <FieldGrid>
      <GridField label="Status">{rowMark(row).label}</GridField>
      <GridField label="Retries">{format.number(row.retries)}</GridField>
      <GridField label="Lane">{row.lane}</GridField>
      {kind === 'published' ? <GridField label="Store">{row.store}</GridField> : null}
      <Optional label="Consumer" span={2} value={row.consumer ? <Mono>{row.consumer}</Mono> : undefined} />
      <GridField label="Added" span={2}>
        <Time value={row.added} />
      </GridField>
      <Optional label="Next retry" span={2} value={row.nextRetryAt ? <Time value={row.nextRetryAt} /> : undefined} />
      <Optional label="Expires" span={2} value={row.expiresAt ? <Time value={row.expiresAt} /> : undefined} />
      <Optional label="Changed" span={2} value={row.changedAt ? <Time value={row.changedAt} /> : undefined} />
      <Optional label="Message id" span={2} value={row.messageId ? <Mono>{row.messageId}</Mono> : undefined} />
      <Optional label="Fingerprint" span={2} value={row.failureFingerprint ? <Mono>{row.failureFingerprint}</Mono> : undefined} />
      <Optional label="Tenant" value={row.tenantId ? <Mono>{row.tenantId}</Mono> : undefined} />
      <GridField label="Storage id" span={2}>
        <Mono>{row.storageId}</Mono>
      </GridField>
    </FieldGrid>
  )
}

/** A row's headers, body, and failure, read when the panel opens and again whenever the row's version moves. */
export function useMessageDetail(kind: MessageKind, node: string, row: Pick<MessageRow, 'id' | 'version'>) {
  const { api } = useDashboard()

  return useQuery({
    queryKey: ['messaging', 'message', kind, node, row.id, row.version ?? ''],
    queryFn: () => fetchMessage(api, kind, node, row.id),
  })
}

function DetailState({ detail, label, children }: { detail: ReturnType<typeof useMessageDetail>; label: string; children: (data: NonNullable<ReturnType<typeof useMessageDetail>['data']>) => ReactNode }) {
  if (detail.isPending) {
    return <Skeleton label={label} />
  }

  if (detail.isError) {
    return detail.error instanceof DashboardApiError && detail.error.status === 404 ? (
      <p role="alert">This message no longer exists.</p>
    ) : (
      <LoadError message={detail.error.message} onRetry={() => void detail.refetch()} />
    )
  }

  return children(detail.data)
}

function MessageOverview({ kind, node, row }: { kind: MessageKind; node: string; row: MessageRow }) {
  const detail = useMessageDetail(kind, node, row)
  const info = detail.data?.exceptionInfo

  return (
    <>
      {info ? <ExceptionBlock {...exceptionParts(info)} /> : null}
      <div>
        <MessageFields kind={kind} row={row} />
      </div>
    </>
  )
}

function MessageHeaders({ kind, node, row }: { kind: MessageKind; node: string; row: MessageRow }) {
  const detail = useMessageDetail(kind, node, row)

  return (
    <section aria-label="Headers">
      <DetailState detail={detail} label="Loading the headers">
        {(data) => {
          const headers = Object.entries(data.headers)

          return headers.length > 0 ? (
            <FieldGrid columns={2}>
              {headers.map(([name, value]) => (
                <GridField key={name} label={name} span={2}>
                  {value === null ? <span className="hd-faint">No value</span> : <Mono>{value}</Mono>}
                </GridField>
              ))}
            </FieldGrid>
          ) : (
            <p>This message has no headers.</p>
          )
        }}
      </DetailState>
    </section>
  )
}

function MessageBody({ kind, node, row }: { kind: MessageKind; node: string; row: MessageRow }) {
  const detail = useMessageDetail(kind, node, row)

  return (
    <section aria-label="Body">
      <DetailState detail={detail} label="Loading the body">
        {(data) =>
          data.content ? (
            // The server stores the serialized message, so its own content type names the format.
            <PayloadView content={data.content} contentType={data.headers['headless-content-type'] ?? data.headers['content-type'] ?? 'application/json'} />
          ) : (
            <p>This message has no body.</p>
          )
        }
      </DetailState>
    </section>
  )
}

/**
 * The rows of one received message across its consumers and re-executions, newest first. The store keeps each
 * generation's row, not each move between states, so only those show.
 */
function MessageGenerations({ node, row }: { node: string; row: MessageRow }) {
  const { api } = useDashboard()
  const messageId = row.messageId
  const generations = useQuery({
    queryKey: ['messaging', 'generations', node, messageId, row.version ?? ''],
    queryFn: () => fetchGenerations(api, node, messageId!),
    enabled: messageId !== undefined,
  })

  if (!messageId) {
    return <p>This row has no message id, so its generations cannot be read.</p>
  }

  if (!generations.data) {
    return (
      <section aria-label="Generations">
        {generations.isError ? (
          <LoadError message={generations.error.message} onRetry={() => void generations.refetch()} />
        ) : (
          <Skeleton label="Loading the generations" />
        )}
      </section>
    )
  }

  const items: HistoryItem[] = [...generations.data].reverse().map((generation) => {
    const mark = rowMark(generation)

    return {
      key: generation.id,
      time: <Time value={generation.changedAt ?? generation.added} short />,
      tone: mark.tone,
      state: generation.id === row.id ? `${mark.label} (this row)` : mark.label,
      note: [generation.consumer, `${generation.retries} retries`, generation.failureFingerprint && `fingerprint ${generation.failureFingerprint}`]
        .filter(Boolean)
        .join(' · '),
    }
  })

  return <HistoryList label="Generations" items={items} />
}

/** The commands each kind's rows offer, in order; the first is the recovery action. */
export const rowCommands: Readonly<Record<MessageKind, readonly { name: MessageCommandName; variant?: 'primary' | 'danger' }[]>> = {
  received: [{ name: 're-execute', variant: 'primary' }, { name: 'hold' }, { name: 'release' }, { name: 'purge', variant: 'danger' }],
  published: [{ name: 're-send', variant: 'primary' }, { name: 'dispatch-now' }, { name: 'revoke' }, { name: 'delete', variant: 'danger' }],
}

/** Hold and release undo each other, so a row offers the one that applies to it. */
export function offers(row: Pick<MessageRow, 'isHeld'>, name: MessageCommandName) {
  return name === 'hold' ? row.isHeld !== true : name === 'release' ? row.isHeld === true : true
}

/**
 * The actions the server allows now on one row. A refused action renders disabled with the server's reason, and a
 * finished one marks the row pending until its change arrives.
 */
export function MessageActions({ kind, row, pending }: { kind: MessageKind; row: MessageRow; pending: Pending }) {
  const module = useModule()
  const commands = useMessagingCommands()
  const refusal = useAllowedActions(kind, [row])
  // Captured at render, so the version is the one the operator saw when they ran the command.
  const versions = versionsOf([row])

  return (
    <>
      {rowCommands[kind]
        .filter(({ name }) => offers(row, name))
        .map(({ name, variant }, index) => {
          const command: CommandDefinition | undefined = commands.find(name)

          return command ? (
            <CommandAction
              key={name}
              module={module.id}
              command={command}
              ids={[row.id]}
              noun="message"
              variant={variant}
              recovery={index === 0}
              commands={commands.all}
              disabledReason={refusal(command.id)}
              onDone={markDone(pending, versions, name)}
            />
          ) : null
        })}
    </>
  )
}

export function MessagePanel({ kind, node, row, pending }: { kind: MessageKind; node: string; row: MessageRow; pending: Pending }) {
  const name = row.name ?? '(unnamed)'

  return (
    <DetailPanel
      label={`Message ${row.id}`}
      name={name}
      id={row.messageId ?? row.storageId}
      status={rowMark(row, pending.labelOf(row))}
      // Keyed by row, so a command still busy or failed for one row does not show on the next one J/K opens.
      actions={<MessageActions key={row.id} kind={kind} row={row} pending={pending} />}
      tabs={[
        { id: 'overview', label: 'Overview', content: <MessageOverview kind={kind} node={node} row={row} /> },
        ...(kind === 'received' ? [{ id: 'generations', label: 'Generations', content: <MessageGenerations node={node} row={row} /> }] : []),
        { id: 'headers', label: 'Headers', content: <MessageHeaders kind={kind} node={node} row={row} /> },
        { id: 'body', label: 'Body', content: <MessageBody kind={kind} node={node} row={row} /> },
      ]}
    />
  )
}

export interface MessagingConsumer {
  identity: string
  lane: string
  delivery: 'competing' | 'every-instance'
  runState: 'runs-here' | 'registered-not-run-here'
  /** Whether the host's consume-only list names the consumer; missing when the host has no such list. */
  consumeOnly?: 'listed' | 'not-listed'
  messages: { name: string; contractVersion: string }[]
  concurrency: number
  inboxRetentionSeconds?: number
  rateLimit?:
    | { algorithm: 'token-bucket'; tokenLimit: number; tokensPerPeriod: number; replenishmentPeriodSeconds: number }
    | { algorithm: 'fixed-window'; permitLimit: number; windowSeconds: number }
  circuitBreaker?: { enabled: boolean; failureThreshold?: number; openDurationSeconds?: number; customTransientPredicate?: boolean }
  middleware?: string[]
}

export interface MessagingContract {
  name: string
  contractVersion: string
  lanes: { lane: string; deliveryMode?: string; routingAffinity?: boolean; rawPayload?: boolean }[]
}

export interface MessagingCircuit {
  /** `{node}~{consumer}`. */
  id: string
  consumer: string
  state: 'Closed' | 'Open' | 'HalfOpen'
  consecutiveFailures: number
  failureThreshold: number
  openedAt?: string | null
  /** A .NET time span, `hh:mm:ss`, while the circuit is open. */
  estimatedRemainingOpenDuration?: string | null
}

/** The consumers the node registers, as its host manifest describes them; they change only with a deploy. */
export function useConsumers() {
  const { api } = useDashboard()
  const { node } = useNode()

  return useQuery({
    queryKey: ['messaging', 'consumers', node],
    queryFn: async () => (await api.request<{ items: MessagingConsumer[] }>({ method: 'GET', path: 'messaging/consumers', query: nodeQuery(node) })).items,
    staleTime: Infinity,
  })
}

/** The message contracts the node registers, with the lanes each travels on. */
export function useContracts() {
  const { api } = useDashboard()
  const { node } = useNode()

  return useQuery({
    queryKey: ['messaging', 'contracts', node],
    queryFn: async () => (await api.request<{ items: MessagingContract[] }>({ method: 'GET', path: 'messaging/contracts', query: nodeQuery(node) })).items,
    staleTime: Infinity,
  })
}

/** A link to a path of the module on the node the views read. */
export function NodeLink({ to, children }: { to: string; children: ReactNode }) {
  const { node } = useNode()
  const separator = to.includes('?') ? '&' : '?'

  return <Link to={`/messaging/${to}${node ? `${separator}node=${encodeURIComponent(node)}` : ''}`}>{children}</Link>
}
