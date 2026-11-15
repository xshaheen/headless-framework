import { useDashboard, useModule, type DashboardStateRole } from '@headless/dashboard-sdk'
import { CommandAction, useAllowedActions, type CommandDefinition } from '@headless/dashboard-sdk/commands'
import {
  Banner,
  Button,
  DetailPanel,
  EmptyState,
  ExceptionBlock,
  FieldGrid,
  KeyValueTable,
  GridField,
  HistoryList,
  LoadError,
  Mono,
  NodeChip,
  PayloadView,
  FilterSelect,
  Skeleton,
  StaleNotice as SdkStaleNotice,
  StatCard,
  StatStrip,
  Text,
  type HistoryItem,
  Timestamp,
} from '@headless/dashboard-sdk/components'
import {
  DashboardApiError,
  listKey,
  refetchTopics,
  useDashboardList,
  type CappedCount,
} from '@headless/dashboard-sdk/data'
import {
  AttentionIcon,
  CancelIcon,
  DeleteIcon,
  DispatchIcon,
  HoldIcon,
  PublishedIcon,
  ReceivedIcon,
  RefreshIcon,
  ReleaseIcon,
  RetryIcon,
  type IconComponent,
} from '@headless/dashboard-sdk/icons'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery, useQueryClient } from '@headless/dashboard-sdk/query'
import { Link, useSearchParams } from '@headless/dashboard-sdk/router'
import { useModuleStale, useNodeStale, useStreamEvent, useStreamStatus } from '@headless/dashboard-sdk/stream'
import { useEffect, type ReactNode } from 'react'
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
  type GenerationRow,
  type MessageKind,
  type MessageRow,
  type Pending,
} from './messages.ts'
import { envelopeBody } from './envelope.ts'

/**
 * A list cell's content, with `title` giving the full text on hover. The table ends a cell's only child in an
 * ellipsis when the column is too narrow for it.
 */
export function CellText({ title, children }: { title?: string; children: ReactNode }) {
  return <span title={title}>{children}</span>
}

/** `Stale` while the stream is down, or the server reports this node's Messaging changes stopped; the data is kept. */
export function StaleNotice({ what, onRefresh, failed }: { what: string; onRefresh: () => void; failed?: boolean }) {
  return <SdkStaleNotice stale={useStale()} what={what} onRefresh={onRefresh} failed={failed} />
}

/**
 * Whether what the view shows may be out of date: the stream is down, this module's changes stopped, or the node the
 * view reads stopped sending its changes.
 */
export function useStale() {
  const module = useModule()
  const { node } = useNode()
  const { status } = useStreamStatus()
  const { isStale } = useModuleStale(module.id)
  const isNodeStale = useNodeStale(module.id, node)

  return status === 'stale' || isStale || (node !== '' && isNodeStale)
}

/** One instance discovery lists for a node: its address, port (`0` when the address carries it), and tags. */
export interface MessagingNodeInstance {
  address: string
  port: number
  tags: string[]
}

export interface MessagingNode {
  name: string
  isLocal: boolean
  stores: string[]
  /** What the last forwarded call to the node found, such as `not-answering`; none before the first call. */
  state?: string | null
  /** Each instance discovery lists for the node, in discovery order; absent for this node. */
  instances?: MessagingNodeInstance[] | null
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
      <Text tone="error" role="alert">
        No Messaging node reachable
      </Text>
    )
  }

  if (!forwarding) {
    return local ? <NodeChip node={local.name} /> : null
  }

  const options = nodes.data.items.map((item) => ({
    value: item.name,
    label: item.isLocal ? `${item.name} (this node)` : item.name,
  }))

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

  return (
    <FilterSelect label="Node" isRequired value={node || (local?.name ?? '')} onChange={choose} options={options} />
  )
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
      void refetchTopics(client, [
        topicOf('published', event.node),
        topicOf('received', event.node),
        topicOf('circuit', event.node),
      ])
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
            const page = await fetchMessages(
              api,
              kind,
              node,
              readFilter(new URLSearchParams(params), kind),
              undefined,
              {},
              1,
            )

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
          Object.entries(answer.counts).map(([key, { count, capped, unavailable }]) => [
            key,
            { value: count, capped: capped || !!unavailable, unavailable },
          ]),
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
    queryFn: async () =>
      (await api.request<{ modules: AttentionSection[] }>({ method: 'GET', path: 'needs-you' })).modules,
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
export function AttentionGaps({
  stale,
  unavailable,
  onRetry,
}: {
  stale: boolean
  unavailable: string[]
  onRetry: () => void
}) {
  if (stale) {
    return <LoadError message="Messaging did not answer, so what needs attention is unknown." onRetry={onRetry} />
  }

  if (unavailable.length > 0) {
    return (
      <Banner role="status">
        <span>{`Not answering: ${unavailable.join(', ')}. Items from these may be missing.`}</span>
        <Button variant="ghost" onPress={onRetry}>
          Retry
        </Button>
      </Banner>
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

/**
 * The node's throughput, second by second for five minutes. It is polled: the node measures a new second whether or
 * not anything changed, so the stream has no change to send for it.
 */
export function useRealtime() {
  const { api } = useDashboard()
  const { node } = useNode()

  return useQuery({
    queryKey: ['messaging', 'realtime', node],
    queryFn: () =>
      api.request<RealtimeMetrics>({ method: 'GET', path: 'messaging/metrics/realtime', query: nodeQuery(node) }),
    refetchInterval: 5_000,
  })
}

// What each card is about; its state stays the dot and the label, as the Jobs strip draws them.
const stripCounts: readonly {
  key: string
  label: string
  tone: DashboardStateRole
  to: string
  icon: IconComponent
}[] = [
  {
    key: 'received.failed',
    label: 'Failed received',
    tone: 'fail',
    to: 'received?status=Failed&terminal=true',
    icon: AttentionIcon,
  },
  {
    key: 'received.retrying',
    label: 'Retrying received',
    tone: 'proc',
    to: 'received?retrying=true',
    icon: RefreshIcon,
  },
  {
    key: 'published.failed',
    label: 'Failed published',
    tone: 'fail',
    to: 'published?status=Failed&terminal=true',
    icon: AttentionIcon,
  },
  {
    key: 'published.retrying',
    label: 'Retrying published',
    tone: 'proc',
    to: 'published?retrying=true',
    icon: RefreshIcon,
  },
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
      {stripCounts.map(({ key, label, tone, to, icon }) => {
        const count = counts.data?.counts[key]

        return (
          <StatCard
            key={key}
            label={label}
            tone={tone}
            value={count ? format.count(count) : '–'}
            to={`/messaging/${to}${nodeSuffix}`}
            icon={icon}
          />
        )
      })}
      <StatCard
        label="Published/s"
        value={rate(realtime.data?.publishedPerSecond)}
        caption="Last minute"
        icon={PublishedIcon}
      />
      <StatCard
        label="Received/s"
        value={rate(realtime.data?.consumedPerSecond)}
        caption="Last minute"
        icon={ReceivedIcon}
      />
    </StatStrip>
  )
}

/** The stored exception's first line, and the rest of it (frames and inner exceptions) as the stack under it. */
function exceptionParts(info: string) {
  const [first = '', ...rest] = info.split('\n')

  return { message: first, stack: rest.join('\n') }
}

function Optional({ label, value, span }: { label: string; value: ReactNode; span?: 2 }) {
  return value === undefined || value === null || value === '' ? null : (
    <GridField label={label} span={span}>
      {value}
    </GridField>
  )
}

/** The headers that tie a message to the work around it, in the order the Correlation group shows them. */
const correlationHeaders: readonly { header: string; label: string }[] = [
  { header: 'headless-corr-id', label: 'Correlation id' },
  { header: 'headless-corr-seq', label: 'Sequence' },
  { header: 'headless-causation-id', label: 'Causation id' },
  { header: 'traceparent', label: 'Trace parent' },
  { header: 'headless-request-id', label: 'Request id' },
  { header: 'headless-in-reply-to', label: 'In reply to' },
  { header: 'headless-reply-to', label: 'Reply to' },
]

/**
 * A row's fields in three groups: who it is, how and when it travels, and what it belongs to. The row carries the first
 * two; the correlation comes from the stored headers, so it shows once the detail is read.
 */
export function MessageFields({
  kind,
  row,
  headers,
}: {
  kind: MessageKind
  row: MessageRow
  headers?: Readonly<Record<string, string | null>>
}) {
  const format = useFormat()
  const correlation = correlationHeaders.flatMap(({ header, label }) => {
    const value = headers?.[header]

    return value ? [{ label, value }] : []
  })

  return (
    <>
      <FieldGrid label="Identity">
        <Optional label="Message id" span={2} value={row.messageId ? <Mono>{row.messageId}</Mono> : undefined} />
        <Optional label="Tenant" value={row.tenantId ? <Mono>{row.tenantId}</Mono> : undefined} />
        <GridField label="Storage id" span={2}>
          <Mono>{row.storageId}</Mono>
        </GridField>
        <Optional
          label="Fingerprint"
          span={2}
          value={row.failureFingerprint ? <Mono>{row.failureFingerprint}</Mono> : undefined}
        />
      </FieldGrid>
      <FieldGrid label="Delivery">
        <GridField label="Status">{rowMark(row).label}</GridField>
        <GridField label="Lane">{row.lane}</GridField>
        {kind === 'published' ? <GridField label="Store">{row.store}</GridField> : null}
        <GridField label="Retries">{format.number(row.retries)}</GridField>
        <Optional label="Consumer" span={2} value={row.consumer ? <Mono>{row.consumer}</Mono> : undefined} />
        <GridField label="Added" span={2}>
          <Mono>{format.exactTime(row.added)}</Mono>
        </GridField>
        <Optional
          label="Changed"
          span={2}
          value={row.changedAt ? <Mono>{format.exactTime(row.changedAt)}</Mono> : undefined}
        />
        <Optional
          label="Next retry"
          span={2}
          value={row.nextRetryAt ? <Timestamp value={row.nextRetryAt} scheduled /> : undefined}
        />
        <Optional
          label="Expires"
          span={2}
          value={row.expiresAt ? <Timestamp value={row.expiresAt} scheduled /> : undefined}
        />
      </FieldGrid>
      {correlation.length > 0 ? (
        <FieldGrid label="Correlation">
          {correlation.map(({ label, value }) => (
            <GridField key={label} label={label} span={2}>
              <Mono>{value}</Mono>
            </GridField>
          ))}
        </FieldGrid>
      ) : null}
    </>
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

type MessageDetail = NonNullable<ReturnType<typeof useMessageDetail>['data']>

/** The row's own fields, under what the detail read says when it could not be read. */
function MessageOverview({
  kind,
  row,
  headers,
  notice,
}: {
  kind: MessageKind
  row: MessageRow
  headers?: Readonly<Record<string, string | null>>
  notice?: ReactNode
}) {
  return (
    <div>
      {notice}
      <MessageFields kind={kind} row={row} headers={headers} />
    </div>
  )
}

/** The stored failure on its own tab, the whole stack open. */
function MessageException({ info }: { info: string }) {
  return (
    <div>
      <ExceptionBlock {...exceptionParts(info)} expanded />
    </div>
  )
}

/** The headers as a name and value table; the tab shows only once the detail says the message has some. */
function MessageHeaders({ detail }: { detail: MessageDetail }) {
  return (
    <section aria-label="Headers">
      <KeyValueTable label="Headers" rows={Object.entries(detail.headers).map(([name, value]) => ({ name, value }))} />
    </section>
  )
}

function MessageBody({ detail }: { detail: MessageDetail }) {
  // The store keeps the whole envelope; its headers already have their own tab, so the body shows the value alone.
  const body = envelopeBody(detail.content)

  return (
    <section aria-label="Body">
      {/* The server stores the serialized message, so its own content type names the format. */}
      <PayloadView
        content={body ?? detail.content}
        contentType={detail.headers['headless-content-type'] ?? detail.headers['content-type'] ?? 'application/json'}
      />
    </section>
  )
}

/**
 * The rows of one received message across its consumers and re-executions, newest first. The store keeps each
 * generation's row, not each move between states, so only those show: with its number, whether it is the current one
 * or a replay of another, when retention may delete it, and whether a host can still run it.
 */
function MessageGenerations({ node, row }: { node: string; row: MessageRow }) {
  const { api } = useDashboard()
  const format = useFormat()
  const messageId = row.messageId
  const generations = useQuery({
    queryKey: ['messaging', 'generations', node, messageId, row.version ?? ''],
    queryFn: () => fetchGenerations(api, node, messageId!),
    enabled: messageId !== undefined,
  })

  if (!messageId) {
    return <EmptyState>This row has no message id, so its generations cannot be read.</EmptyState>
  }

  if (!generations.data) {
    return (
      <section aria-label="Generations">
        {generations.isError ? (
          <LoadError message={generations.error.message} onRetry={() => void generations.refetch()} />
        ) : (
          <Skeleton label="Loading the generations" shape="lines" rows={4} />
        )}
      </section>
    )
  }

  const items: HistoryItem[] = [...generations.data].reverse().map((generation) => {
    const mark = rowMark(generation)

    return {
      key: generation.id,
      time: <Timestamp value={generation.changedAt ?? generation.added} />,
      tone: mark.tone,
      state: generation.id === row.id ? `${mark.label} (this row)` : mark.label,
      note: [
        generationNote(generation, format.exactTime),
        generation.consumer,
        `${generation.retries} retries`,
        generation.failureFingerprint && `fingerprint ${generation.failureFingerprint}`,
      ]
        .filter(Boolean)
        .join(' · '),
    }
  })

  return <HistoryList label="Generations" items={items} />
}

/** `Generation 1, current, replay of generation 0, orphaned, expires …`: what the store records about a generation. */
export function generationNote(row: GenerationRow, exactTime: (at: string) => string) {
  const generation = row.generation

  if (!generation) {
    return 'No generation recorded'
  }

  return [
    `Generation ${generation.number}`,
    generation.isCurrent ? 'current' : 'retired',
    generation.origin === 'replay' ? 'replay' : 'original',
    generation.recovery === 'orphaned' ? 'orphaned: no host registers its consumer' : undefined,
    generation.expiresAt ? `expires ${exactTime(generation.expiresAt)}` : undefined,
  ]
    .filter(Boolean)
    .join(', ')
}

/** The commands each kind's rows offer, in order; the first is the recovery action. */
export const rowCommands: Readonly<
  Record<MessageKind, readonly { name: MessageCommandName; variant?: 'primary' | 'danger'; icon: IconComponent }[]>
> = {
  received: [
    { name: 're-execute', variant: 'primary', icon: RetryIcon },
    { name: 'hold', icon: HoldIcon },
    { name: 'release', icon: ReleaseIcon },
    { name: 'purge', variant: 'danger', icon: DeleteIcon },
  ],
  published: [
    { name: 're-send', variant: 'primary', icon: RetryIcon },
    { name: 'dispatch-now', icon: DispatchIcon },
    { name: 'revoke', icon: CancelIcon },
    { name: 'delete', variant: 'danger', icon: DeleteIcon },
  ],
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
        .map(({ name, variant, icon }, index) => {
          const command: CommandDefinition | undefined = commands.find(name)

          return command ? (
            <CommandAction
              key={name}
              module={module.id}
              command={command}
              ids={[row.id]}
              noun="message"
              variant={variant}
              icon={icon}
              // The recovery action keeps its label as the row's one primary button; the rest are icons with tooltips.
              appearance={index === 0 ? 'button' : 'icon'}
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

export function MessagePanel({
  kind,
  node,
  row,
  pending,
}: {
  kind: MessageKind
  node: string
  row: MessageRow
  pending: Pending
}) {
  const name = row.name ?? '(unnamed)'
  // The row carries no headers, body, or failure: the detail is read once the panel opens, and each tab shows only
  // when the message has that part. A missing message keeps the Overview, which says so.
  const detail = useMessageDetail(kind, node, row)
  const data = detail.data
  const gone = detail.isError && detail.error instanceof DashboardApiError && detail.error.status === 404
  const info = data?.exceptionInfo

  return (
    <DetailPanel
      label={`Message ${row.id}`}
      name={name}
      id={row.messageId ?? row.storageId}
      status={rowMark(row, pending.labelOf(row))}
      // Keyed by row, so a command still busy or failed for one row does not show on the next one J/K opens.
      actions={<MessageActions key={row.id} kind={kind} row={row} pending={pending} />}
      // A failed message opens on its exception: that is what the operator came to read.
      defaultTab={info && rowMark(row).tone === 'fail' ? 'exception' : 'overview'}
      tabs={[
        {
          id: 'overview',
          label: 'Overview',
          content: (
            <MessageOverview
              kind={kind}
              row={row}
              headers={data?.headers}
              notice={
                gone ? (
                  <EmptyState role="alert" title="This message no longer exists." />
                ) : detail.isError ? (
                  <LoadError message={detail.error.message} onRetry={() => void detail.refetch()} />
                ) : null
              }
            />
          ),
        },
        { id: 'exception', label: 'Exception', content: info ? <MessageException info={info} /> : null },
        ...(kind === 'received'
          ? [{ id: 'generations', label: 'Generations', content: <MessageGenerations node={node} row={row} /> }]
          : []),
        {
          id: 'headers',
          label: 'Headers',
          content: data && Object.keys(data.headers).length > 0 ? <MessageHeaders detail={data} /> : null,
        },
        { id: 'body', label: 'Body', content: data?.content ? <MessageBody detail={data} /> : null },
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
  /** Each message it consumes, with the type that implements it and the method that handles it, when the host says. */
  messages: { name: string; contractVersion: string; consumerType?: string | null; handlerMethod?: string | null }[]
  concurrency: number
  /** Whether it handles the messages that share a routing affinity key one at a time; missing when it does not. */
  sequential?: boolean
  inboxRetentionSeconds?: number
  rateLimit?:
    | { algorithm: 'token-bucket'; tokenLimit: number; tokensPerPeriod: number; replenishmentPeriodSeconds: number }
    | { algorithm: 'fixed-window'; permitLimit: number; windowSeconds: number }
  circuitBreaker?: {
    enabled: boolean
    failureThreshold?: number
    openDurationSeconds?: number
    customTransientPredicate?: boolean
  }
  middleware?: string[]
}

export interface MessagingContract {
  name: string
  contractVersion: string
  lanes: { lane: string; deliveryMode?: string; routingAffinity?: boolean; rawPayload?: boolean }[]
}

export interface MessagingCircuit {
  /** `{node}~{circuit key}`, which the circuit's commands act on. */
  id: string
  /** The consumer identity, as the consumers read names it. */
  consumer: string
  /** The lane the circuit guards: an identity may consume on both, each with a circuit of its own. */
  lane?: 'Bus' | 'Queue'
  state: 'Closed' | 'Open' | 'HalfOpen'
  consecutiveFailures: number
  failureThreshold: number
  openedAt?: string | null
  /** A .NET time span, `hh:mm:ss`, while the circuit is open. */
  estimatedRemainingOpenDuration?: string | null
  /** The version of the circuit's latest change: the list serves it, and a stream change that patches the row replaces it. */
  version?: string
}

/** The consumers the node registers, as its host manifest describes them; they change only with a deploy. */
export function useConsumers() {
  const { api } = useDashboard()
  const { node } = useNode()

  return useQuery({
    queryKey: ['messaging', 'consumers', node],
    queryFn: async () =>
      (
        await api.request<{ items: MessagingConsumer[] }>({
          method: 'GET',
          path: 'messaging/consumers',
          query: nodeQuery(node),
        })
      ).items,
    staleTime: Infinity,
  })
}

/** How often the time left on an open circuit is read again; no change carries the seconds as they pass. */
const openCircuitRefreshMs = 5_000

/**
 * A consumer's circuits on the node the views read, as a live list: the node that holds a circuit sends each change
 * on the stream, which patches its row. Only the time left on an open circuit moves with no change behind it, so the
 * list is read again on a timer while one is open, and never otherwise.
 */
export function useCircuits() {
  const { api } = useDashboard()
  const { node } = useNode()
  const client = useQueryClient()
  const topic = topicOf('circuit', node)
  const circuits = useDashboardList<MessagingCircuit>({
    topic,
    fetch: () =>
      api.request<{ items: MessagingCircuit[] }>({
        method: 'GET',
        path: 'messaging/circuits',
        query: nodeQuery(node),
      }),
  })
  const rows = circuits.data?.items
  const hasOpen = rows?.some((circuit) => circuit.state === 'Open') ?? false

  // A change patches a row the list holds; a circuit it does not hold yet has no row to patch, so the list is read.
  useStreamEvent('change', (change) => {
    if (change.topic === topic && rows && !rows.some((circuit) => circuit.id === change.id)) {
      void client.invalidateQueries({ queryKey: listKey(topic) })
    }
  })

  useEffect(() => {
    if (!hasOpen) {
      return undefined
    }

    const timer = setInterval(() => void client.invalidateQueries({ queryKey: listKey(topic) }), openCircuitRefreshMs)

    return () => clearInterval(timer)
  }, [client, hasOpen, topic])

  return circuits
}

/** One store's stored work still waiting. A figure the store does not keep is missing, never zero. */
export interface StoreBacklog {
  store: string
  publishedPending: number
  /** How long the oldest pending published row has waited; missing when none is pending. */
  oldestPublishedSeconds?: number
  /** Missing for an additional outbox, which holds no received rows. */
  receivedPending?: number
  oldestReceivedSeconds?: number
}

export interface MessagingBacklog {
  asOf: string
  stores: StoreBacklog[]
  /** The stores that did not answer; their backlog is unknown, not empty. */
  unavailable: string[]
}

/** The backlog of every store the node reads. Rows enter and leave it all the time, so it is polled. */
export function useBacklog() {
  const { api } = useDashboard()
  const { node } = useNode()

  return useQuery({
    queryKey: ['messaging', 'backlog', node],
    queryFn: () => api.request<MessagingBacklog>({ method: 'GET', path: 'messaging/backlog', query: nodeQuery(node) }),
    refetchInterval: 15_000,
  })
}

/** What the node publishes through: the broker of each lane and the storage provider of each outbox, by name. */
export interface MessagingProviders {
  transports: { bus?: string; queue?: string }
  outboxes: { name: string; storage: string }[]
}

/** The node's providers, as its host manifest names them; they change only with a deploy. */
export function useProviders() {
  const { api } = useDashboard()
  const { node } = useNode()

  return useQuery({
    queryKey: ['messaging', 'providers', node],
    queryFn: () =>
      api.request<MessagingProviders>({ method: 'GET', path: 'messaging/providers', query: nodeQuery(node) }),
    staleTime: Infinity,
  })
}

/** The message contracts the node registers, with the lanes each travels on. */
export function useContracts() {
  const { api } = useDashboard()
  const { node } = useNode()

  return useQuery({
    queryKey: ['messaging', 'contracts', node],
    queryFn: async () =>
      (
        await api.request<{ items: MessagingContract[] }>({
          method: 'GET',
          path: 'messaging/contracts',
          query: nodeQuery(node),
        })
      ).items,
    staleTime: Infinity,
  })
}

/** A link to a path of the module on the node the views read. */
export function NodeLink({ to, children }: { to: string; children: ReactNode }) {
  const { node } = useNode()
  const separator = to.includes('?') ? '&' : '?'

  return <Link to={`/messaging/${to}${node ? `${separator}node=${encodeURIComponent(node)}` : ''}`}>{children}</Link>
}
