import { useDashboard } from '@headless/dashboard-sdk'
import {
  Button,
  Card,
  CardRow,
  Cards,
  FilterToolbar,
  LoadError,
  Mono,
  PageFrame,
  PageHeader,
  Skeleton,
  usePageFrame,
  Timestamp,
} from '@headless/dashboard-sdk/components'
import { listKey, useDashboardKeysetList } from '@headless/dashboard-sdk/data'
import {
  AttentionIcon,
  DispatchIcon,
  ListIcon,
  OpenCircuitIcon,
  PublishedIcon,
  ReceivedIcon,
} from '@headless/dashboard-sdk/icons'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQueryClient } from '@headless/dashboard-sdk/query'
import { Link } from '@headless/dashboard-sdk/router'
import {
  circuitLabels,
  circuitTones,
  fetchMessages,
  failureGroupPath,
  formatSeconds,
  parseFailureGroup,
  readFilter,
  rowMark,
  spanSeconds,
  topicOf,
  type MessageKind,
  type MessageRow,
} from '../messages.ts'
import {
  AttentionGaps,
  MessagingStats,
  NodeLink,
  NodeSwitch,
  StaleNotice,
  useBacklog,
  useCircuits,
  useMessagingAttention,
  useNode,
  useNodeReset,
  useProviders,
  type AttentionItem,
} from '../parts.tsx'
import { HourlyHistoryCard, LiveThroughput, StatusDistributionCard } from '../insight.tsx'

const cardRows = 6

/** The failed rows, as the sidebar's Failed list reads them, so the card and the list share one cached answer. */
const failedParams = new URLSearchParams({ status: 'Failed', terminal: 'true' })

export default function MessagingDashboard() {
  const client = useQueryClient()
  const { node } = useNode()
  const frame = usePageFrame({ viewId: 'messaging.dashboard', rows: [] })
  // Every answer the dashboard reads: its lists, the strip's counts and rates, and the attention items.
  const dashboardKeys = [
    listKey(topicOf('received', node)),
    listKey(topicOf('published', node)),
    ['messaging'],
    ['shell', 'counts', 'messaging'],
    ['shell', 'attention'],
  ]

  useNodeReset()

  return (
    <PageFrame
      frame={frame}
      stats={<MessagingStats />}
      header={
        <PageHeader
          title="Dashboard"
          toolbar={
            <FilterToolbar>
              <NodeSwitch />
            </FilterToolbar>
          }
        />
      }
    >
      <StaleNotice
        what="this dashboard"
        onRefresh={() => dashboardKeys.forEach((queryKey) => void client.invalidateQueries({ queryKey }))}
      />
      <Cards>
        {/* Narrow cards fill the first row; each wide chart then shares a row with one narrow card. */}
        <MessagingAttention />
        <RecentFailures kind="received" />
        <RecentFailures kind="published" />
        <LiveThroughput />
        <StatusDistributionCard />
        <HourlyHistoryCard />
        <BacklogCard />
        <CircuitsCard />
        <ProvidersCard />
      </Cards>
    </PageFrame>
  )
}

/** `3 messages received by sandbox.orders failed for good`, as the server titles a failure group, split into parts. */
const failedGroupTitle = /^(\d[\d,]*\+? messages?) received by (\S+) (failed .*)$/

/** Where an attention item leads: a failure group's failed rows, or the unknown lanes. */
function attentionPath(item: AttentionItem) {
  const group = parseFailureGroup(item.id)

  if (group) {
    return `/messaging/${failureGroupPath(group)}`
  }

  return item.id.includes('unknown-lane:') ? '/messaging/unknown-lanes' : undefined
}

function MessagingAttention() {
  const { answer, items, stale, unavailable, complete } = useMessagingAttention()
  const retry = () => void answer.refetch()
  // A source that did not answer is a row of its own, so the card never reads as the whole story without it.
  const gap =
    unavailable.length > 0 ? `Not answering: ${unavailable.join(', ')}. Items from these may be missing.` : undefined

  return (
    <Card
      title="Needs attention"
      icon={AttentionIcon}
      emptyIcon={AttentionIcon}
      caption={
        items.length > 0
          ? `${items.length}${complete ? '' : '+'} ${items.length === 1 && complete ? 'item' : 'items'}`
          : undefined
      }
      empty={
        answer.isError ? (
          <LoadError message={answer.error.message} onRetry={retry} />
        ) : answer.isPending ? (
          <Skeleton label="Loading what needs attention" rows={2} />
        ) : stale || !complete ? (
          <AttentionGaps stale={stale} unavailable={unavailable} onRetry={retry} />
        ) : (
          'Nothing in Messaging needs attention.'
        )
      }
    >
      {items.length > 0
        ? [
            ...items.map((item) => {
              const path = attentionPath(item)
              // The consumer is the identifier, so it leads the row in mono and the count follows as its fact; the
              // link still answers to the whole title. A title in another shape reads as the prose it is.
              const parts = failedGroupTitle.exec(item.title)
              const name = parts ? <Mono>{parts[2]}</Mono> : item.title

              return (
                <CardRow
                  key={item.id}
                  tone={item.severity === 'critical' ? 'fail' : 'proc'}
                  prose={!parts}
                  name={
                    path ? (
                      <Link to={path} aria-label={item.title}>
                        {name}
                      </Link>
                    ) : (
                      name
                    )
                  }
                  meta={parts ? `${parts[1]} ${parts[3]}` : undefined}
                  title={item.title}
                  value={item.lastSeenAt ? <Timestamp value={item.lastSeenAt} /> : undefined}
                />
              )
            }),
            ...(gap
              ? [
                  <CardRow
                    key="unavailable"
                    tone="off"
                    name={<span role="status">{gap}</span>}
                    actions={
                      <Button variant="ghost" onPress={retry}>
                        Retry
                      </Button>
                    }
                  />,
                ]
              : []),
          ]
        : null}
    </Card>
  )
}

/** The newest failed rows of one kind on the node the views read, each opening in its list. */
function RecentFailures({ kind }: { kind: MessageKind }) {
  const { api } = useDashboard()
  const format = useFormat()
  const { node } = useNode()
  const filter = readFilter(failedParams, kind)
  const failed = useDashboardKeysetList<MessageRow>({
    topic: topicOf(kind, node),
    params: { ...filter, node },
    fetchPage: (cursor) => fetchMessages(api, kind, node, filter, cursor),
  })
  const shown = failed.rows.filter((row) => rowMark(row).tone === 'fail').slice(0, cardRows)
  const nodeSuffix = node ? `&node=${encodeURIComponent(node)}` : ''
  const listPath = `/messaging/${kind}?status=Failed&terminal=true${nodeSuffix}`

  return (
    <Card
      title={kind === 'received' ? 'Recent received failures' : 'Recent published failures'}
      icon={kind === 'received' ? ReceivedIcon : PublishedIcon}
      emptyIcon={kind === 'received' ? ReceivedIcon : PublishedIcon}
      caption={shown.length > 0 && failed.count ? `${format.count(failed.count)} failed` : undefined}
      action={shown.length > 0 ? <Link to={listPath}>All</Link> : undefined}
      empty={
        failed.query.isError ? (
          <LoadError message={failed.query.error.message} onRetry={() => void failed.query.refetch()} />
        ) : failed.query.isPending ? (
          <Skeleton label={`Loading the failed ${kind} messages`} rows={2} />
        ) : (
          `No failed ${kind} messages.`
        )
      }
    >
      {shown.map((row) => (
        <CardRow
          key={row.id}
          tone="fail"
          name={<Link to={`${listPath}&open=${encodeURIComponent(row.id)}`}>{row.name ?? '(unnamed)'}</Link>}
          meta={kind === 'received' && row.consumer ? <Mono>{row.consumer}</Mono> : row.store}
          title={`${row.name ?? '(unnamed)'} ${(kind === 'received' ? row.consumer : row.store) ?? ''}`.trim()}
          value={<Timestamp value={row.changedAt ?? row.added} />}
        />
      ))}
    </Card>
  )
}

/**
 * The stored work still waiting in each store the node reads: rows not yet sent to the broker, and rows not yet
 * handled, each with how long its oldest has waited. A store that did not answer is named, never counted as empty.
 */
function BacklogCard() {
  const format = useFormat()
  const backlog = useBacklog()
  const stores = backlog.data?.stores ?? []
  const unavailable = backlog.data?.unavailable ?? []
  // One store needs no name on its rows; with several, each row says whose backlog it is.
  const named = stores.length + unavailable.length > 1
  const rows = stores.flatMap((store) => [
    {
      key: `${store.store}:published`,
      name: 'Waiting to be sent',
      store: store.store,
      pending: store.publishedPending,
      oldest: store.oldestPublishedSeconds,
    },
    ...(store.receivedPending === undefined
      ? []
      : [
          {
            key: `${store.store}:received`,
            name: 'Waiting to be handled',
            store: store.store,
            pending: store.receivedPending,
            oldest: store.oldestReceivedSeconds,
          },
        ]),
  ])
  const total = rows.reduce((sum, row) => sum + row.pending, 0)
  const retry = () => void backlog.refetch()

  return (
    <Card
      title="Backlog"
      icon={ListIcon}
      emptyIcon={ListIcon}
      // A store that did not answer may hold more, so the total is said only when every store answered.
      caption={rows.length > 0 && unavailable.length === 0 ? `${format.number(total)} waiting` : undefined}
      empty={
        backlog.isError ? (
          <LoadError message={backlog.error.message} onRetry={retry} />
        ) : backlog.isPending ? (
          <Skeleton label="Loading the backlog" rows={2} />
        ) : (
          'This node reads no store.'
        )
      }
    >
      {rows.length > 0 || unavailable.length > 0
        ? [
            ...rows.map((row) => (
              <CardRow
                key={row.key}
                tone={row.pending > 0 ? 'enq' : 'off'}
                prose
                name={row.name}
                meta={
                  <>
                    {named ? <Mono>{row.store}</Mono> : null}
                    {named && row.oldest !== undefined ? ' · ' : null}
                    {row.oldest !== undefined ? `oldest for ${format.duration(row.oldest * 1000)}` : null}
                  </>
                }
                value={format.number(row.pending)}
              />
            )),
            ...(unavailable.length > 0
              ? [
                  <CardRow
                    key="unavailable"
                    tone="off"
                    name={
                      <span role="status">{`Not answering: ${unavailable.join(', ')}. Their backlog is unknown.`}</span>
                    }
                    actions={
                      <Button variant="ghost" onPress={retry}>
                        Retry
                      </Button>
                    }
                  />,
                ]
              : []),
          ]
        : null}
    </Card>
  )
}

/**
 * The circuit breakers that are not closed on the node the views read, each opening its consumer. A closed circuit
 * is the normal state, so the card lists only the ones holding deliveries back.
 */
function CircuitsCard() {
  const format = useFormat()
  const circuits = useCircuits()
  const all = circuits.data ?? []
  const tripped = all.filter((circuit) => circuit.state !== 'Closed')
  const open = tripped.filter((circuit) => circuit.state === 'Open').length
  const halfOpen = tripped.length - open

  return (
    <Card
      title="Circuits"
      icon={OpenCircuitIcon}
      emptyIcon={OpenCircuitIcon}
      caption={
        all.length === 0
          ? undefined
          : tripped.length === 0
            ? `${format.number(all.length)} closed`
            : [
                open > 0 ? `${format.number(open)} open` : '',
                halfOpen > 0 ? `${format.number(halfOpen)} half open` : '',
              ]
                .filter(Boolean)
                .join(' · ')
      }
      action={all.length > 0 ? <NodeLink to="consumers">Consumers</NodeLink> : undefined}
      empty={
        circuits.isError ? (
          <LoadError message={circuits.error.message} onRetry={() => void circuits.refetch()} />
        ) : circuits.isPending ? (
          <Skeleton label="Loading the circuit breakers" rows={2} />
        ) : all.length === 0 ? (
          'No consumer on this node has a circuit breaker.'
        ) : (
          'No circuit is open.'
        )
      }
    >
      {tripped.map((circuit) => {
        const seconds = circuit.estimatedRemainingOpenDuration
          ? spanSeconds(circuit.estimatedRemainingOpenDuration)
          : undefined
        const failures = `${format.number(circuit.consecutiveFailures)} of ${format.number(circuit.failureThreshold)} failures`

        return (
          <CardRow
            key={circuit.id}
            tone={circuitTones[circuit.state]}
            name={
              <NodeLink to={`consumers?open=${encodeURIComponent(circuit.id)}`}>
                <Mono>{circuit.consumer}</Mono>
              </NodeLink>
            }
            meta={`${circuitLabels[circuit.state]} · ${failures}`}
            title={`${circuit.consumer} ${circuitLabels[circuit.state]}, ${failures}`}
            value={seconds === undefined ? undefined : `retries in ${formatSeconds(Math.ceil(seconds), format.number)}`}
          />
        )
      })}
    </Card>
  )
}

/** What the node publishes through: the broker of each lane, and the storage provider of each outbox. */
function ProvidersCard() {
  const providers = useProviders()
  const data = providers.data
  const rows = data
    ? [
        ...(data.transports.bus
          ? [{ key: 'bus', name: 'Bus lane', store: undefined, value: data.transports.bus }]
          : []),
        ...(data.transports.queue
          ? [{ key: 'queue', name: 'Queue lane', store: undefined, value: data.transports.queue }]
          : []),
        ...data.outboxes.map((outbox) => ({
          key: `outbox:${outbox.name}`,
          name: 'Outbox',
          store: outbox.name,
          value: outbox.storage,
        })),
      ]
    : []

  return (
    <Card
      title="Providers"
      icon={DispatchIcon}
      emptyIcon={DispatchIcon}
      empty={
        providers.isError ? (
          <LoadError message={providers.error.message} onRetry={() => void providers.refetch()} />
        ) : providers.isPending ? (
          <Skeleton label="Loading the providers" rows={2} />
        ) : (
          'This node has no transport and no storage.'
        )
      }
    >
      {rows.map((row) => (
        <CardRow
          key={row.key}
          prose
          name={row.name}
          meta={row.store ? <Mono>{row.store}</Mono> : undefined}
          value={<Mono>{row.value}</Mono>}
        />
      ))}
    </Card>
  )
}
