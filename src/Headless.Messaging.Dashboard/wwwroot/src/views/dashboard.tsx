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
import { AttentionIcon, PublishedIcon, ReceivedIcon } from '@headless/dashboard-sdk/icons'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQueryClient } from '@headless/dashboard-sdk/query'
import { Link } from '@headless/dashboard-sdk/router'
import {
  fetchMessages,
  failureGroupPath,
  parseFailureGroup,
  readFilter,
  rowMark,
  topicOf,
  type MessageKind,
  type MessageRow,
} from '../messages.ts'
import {
  AttentionGaps,
  MessagingStats,
  NodeSwitch,
  StaleNotice,
  useMessagingAttention,
  useNode,
  useNodeReset,
  type AttentionItem,
} from '../parts.tsx'

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
        <MessagingAttention />
        <RecentFailures kind="received" />
        <RecentFailures kind="published" />
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
