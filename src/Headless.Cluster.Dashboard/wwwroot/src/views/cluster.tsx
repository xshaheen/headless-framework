import {
  Banner,
  DetailPanel,
  EmptyState,
  FieldGrid,
  GridField,
  HistoryList,
  KeyValueTable,
  Mono,
  PageFrame,
  PageHeader,
  QueryState,
  Section,
  StatCard,
  StatStrip,
  StatusMark,
  Tag,
  Text,
  Timestamp,
  usePageFrame,
  type HistoryItem,
} from '@headless/dashboard-sdk/components'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { NodeIcon } from '@headless/dashboard-sdk/icons'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { Link, useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable } from '@headless/dashboard-sdk/table'
import {
  nodeMark,
  nodeStates,
  parseFilter,
  queryKeys,
  useMetadataAgreement,
  useNodeDetail,
  useNodes,
  useSummary,
  type ClusterNode,
  type MembershipEvent,
  type NodeAgreement,
  type ValueGroup,
} from '../api.ts'
import { CellText, FilterNote, StaleNotice, useClusterCounts, useKeptOpenRow, useLiveRefetch } from '../parts.tsx'

// A heartbeat ages on screen between change events, which leave it out; a shorter interval adds reads, not facts.
const MIN_REFRESH_MS = 5_000

export default function ClusterView() {
  const format = useFormat()
  const summary = useSummary()
  const [params] = useSearchParams()
  const state = parseFilter(nodeStates, params.get('state'))
  const coordination = summary.data?.coordination
  const hasCoordination = coordination != null
  const nodes = useNodes(
    state,
    hasCoordination,
    coordination ? Math.max(MIN_REFRESH_MS, coordination.heartbeatIntervalMilliseconds) : false,
  )
  const agreement = useMetadataAgreement(hasCoordination)
  const kept = useKeptOpenRow(nodes.data ?? [], params.get('open'))
  const frame = usePageFrame({ viewId: 'cluster.nodes', rows: kept.rows })
  // Only the unfiltered list holds every incarnation, so only it can tell this node is missing.
  const localMissing =
    coordination?.localNode != null &&
    state === undefined &&
    nodes.data !== undefined &&
    !nodes.data.some((node) => node.isLocal)

  useLiveRefetch(['node'], [queryKeys.nodes, queryKeys.summary])

  if (summary.data && !hasCoordination) {
    return (
      <PageFrame frame={frame} header={<PageHeader title="Nodes" />}>
        <EmptyState
          icon={NodeIcon}
          title="Coordination is not registered"
          placement="page"
          action={<Link to="/cluster/health">Open health checks</Link>}
        >
          This host serves no cluster membership. Call services.AddHeadlessCoordination(...) with a provider to show its
          nodes here.
        </EmptyState>
      </PageFrame>
    )
  }

  return (
    <PageFrame
      frame={frame}
      stats={
        coordination ? (
          <NodeStats
            heartbeat={coordination.heartbeatIntervalMilliseconds}
            suspected={coordination.suspicionThresholdMilliseconds}
            dead={coordination.deadThresholdMilliseconds}
          />
        ) : undefined
      }
      header={
        <PageHeader
          title="Nodes"
          count={nodes.data ? `${format.number(nodes.data.length)} incarnations` : undefined}
          description="Read-only. Coordination reports liveness; times come from the membership store's clock."
        />
      }
      panel={frame.openId ? <NodePanel id={frame.openId} /> : undefined}
    >
      <StaleNotice
        what="these nodes"
        onRefresh={() => void nodes.refetch()}
        failed={nodes.isError || summary.isError}
      />
      {localMissing ? (
        <Banner tone="warn">This node is not in the membership store, so no row is marked as this node.</Banner>
      ) : null}
      <QueryState query={summary} label="Loading nodes" rows={4} shape="table">
        {() => (
          <QueryState query={nodes} label="Loading nodes" rows={4} shape="table">
            {() => (
              <DataTable
                label="Nodes"
                frame={frame}
                rows={kept.rows}
                emptyIcon={NodeIcon}
                emptyTitle={state ? `No ${state.toLowerCase()} nodes` : 'No nodes'}
                emptyState={
                  state
                    ? 'No incarnation in the membership store has this state now.'
                    : 'A node appears here once a host that runs Coordination registers.'
                }
                columns={[
                  {
                    id: 'node',
                    header: 'node',
                    cell: (row) => (
                      <CellText title={row.id}>
                        <Mono>{row.id}</Mono>
                        {row.isLocal ? <Tag>This node</Tag> : null}
                        {kept.isKept(row) ? <FilterNote /> : null}
                      </CellText>
                    ),
                    isRowHeader: true,
                    kind: 'id',
                    width: '2fr',
                  },
                  {
                    id: 'role',
                    header: 'role',
                    cell: (row) => <CellText title={row.role ?? undefined}>{row.role ?? ''}</CellText>,
                    priority: 2,
                    width: '1fr',
                  },
                  {
                    id: 'state',
                    header: 'state',
                    cell: (row) => (
                      <CellText>
                        <StatusMark {...nodeMark(row.state)} />
                      </CellText>
                    ),
                    kind: 'status',
                    width: 130,
                  },
                  {
                    id: 'lastHeartbeat',
                    header: 'last heartbeat',
                    cell: (row) => (
                      <CellText>
                        <Timestamp value={row.lastHeartbeatAt} />
                      </CellText>
                    ),
                    kind: 'time',
                    width: 140,
                  },
                ]}
              />
            )}
          </QueryState>
        )}
      </QueryState>
      {agreement.data && agreement.data.some((key) => !key.isUnanimous) ? (
        <Section label="Metadata agreement" description="Values that differ across the alive and suspected nodes.">
          <KeyValueTable
            label="Metadata that differs"
            rows={agreement.data
              .filter((key) => !key.isUnanimous)
              .map((key) => ({ name: key.key, value: groupsText(key.groups) }))}
          />
        </Section>
      ) : null}
    </PageFrame>
  )
}

/** The served node counts by state, and the thresholds that decide those states. */
function NodeStats({ heartbeat, suspected, dead }: { heartbeat: number; suspected: number; dead: number }) {
  const format = useFormat()
  const counts = useClusterCounts()
  const value = (key: string) => {
    const count = counts.data?.counts[key]

    return count ? format.count(count) : '–'
  }

  return (
    <StatStrip label="Cluster at a glance">
      <StatCard label="Alive" tone="ok" value={value('Alive')} to="?state=Alive" />
      <StatCard label="Suspected" tone="proc" value={value('Suspected')} to="?state=Suspected" />
      <StatCard label="Dead" tone="fail" value={value('Dead')} to="?state=Dead" />
      <StatCard label="Heartbeat every" value={format.duration(heartbeat)} />
      <StatCard label="Suspected after" value={format.duration(suspected)} />
      <StatCard label="Dead after" value={format.duration(dead)} />
    </StatStrip>
  )
}

/** `4.2.1 on 2 nodes · 4.2.0 on 1 node · missing on 1 node`. */
function groupsText(groups: readonly ValueGroup[]) {
  return groups
    .map(
      (group) => `${group.value ?? 'missing'} on ${group.nodes.length} ${group.nodes.length === 1 ? 'node' : 'nodes'}`,
    )
    .join(' · ')
}

/**
 * A key that holds a different value on every node by design, such as a pod name, reads neutrally: three or more live
 * nodes compared, each alone in its group, and none missing the key.
 */
function differsOnEveryNode(key: NodeAgreement) {
  return (
    key.compared >= 3 &&
    key.matches === 1 &&
    key.groups.every((group) => group.nodes.length === 1 && group.value !== null)
  )
}

function eventItems(events: readonly MembershipEvent[]): HistoryItem[] {
  return events.map((event) => ({
    key: String(event.sequence),
    time: <Timestamp value={event.observedAt} />,
    tone: event.state ? nodeMark(event.state).tone : 'off',
    state: event.isFirstObservation ? `${event.state ?? 'Seen'} (starting state)` : eventLabel(event.kind),
    note: event.state && !event.isFirstObservation ? event.state : undefined,
  }))
}

function eventLabel(kind: string) {
  switch (kind) {
    case 'NodeJoined':
      return 'Joined'
    case 'NodeSuspected':
      return 'Suspected'
    case 'NodeRecovered':
      return 'Recovered'
    case 'NodeLeft':
      return 'Left the snapshot'
    case 'LocalMembershipLost':
      return 'Membership lost'
    default:
      return kind
  }
}

function NodePanel({ id }: { id: string }) {
  const detail = useNodeDetail(id)
  const summary = useSummary()
  const gone = detail.error instanceof DashboardApiError && detail.error.status === 404

  if (gone) {
    return (
      <DetailPanel label={`Node ${id}`} name={id}>
        <Banner tone="warn">This node is no longer in the membership store.</Banner>
      </DetailPanel>
    )
  }

  const node = detail.data?.node

  return (
    <DetailPanel
      label={`Node ${id}`}
      name={id}
      status={node ? nodeMark(node.state) : undefined}
      tabs={[
        {
          id: 'overview',
          label: 'Overview',
          content: (
            <QueryState query={detail} label="Loading the node" rows={4} shape="fields">
              {(data) => <NodeOverview node={data.node} />}
            </QueryState>
          ),
        },
        {
          id: 'metadata',
          label: 'Metadata',
          content: detail.data && detail.data.agreement.length > 0 ? <Agreement keys={detail.data.agreement} /> : null,
        },
        {
          id: 'events',
          label: 'Events',
          content:
            detail.data && detail.data.events.length > 0 ? (
              <>
                <Text as="p" tone="hint">
                  {`Seen by ${summary.data?.host ?? 'this node'}, local clock`}
                </Text>
                <HistoryList label="Membership events" items={eventItems(detail.data.events)} />
              </>
            ) : null,
        },
      ]}
    />
  )
}

function NodeOverview({ node }: { node: ClusterNode }) {
  const format = useFormat()

  return (
    <FieldGrid label="Node">
      <GridField label="State">
        <StatusMark {...nodeMark(node.state)} />
      </GridField>
      <GridField label="Role">{node.role ?? 'None'}</GridField>
      <GridField label="Last heartbeat">
        <Timestamp value={node.lastHeartbeatAt} />
      </GridField>
      <GridField label="Incarnation">{format.number(node.incarnation)}</GridField>
      <GridField label="Owner tag" span={2}>
        <Mono>{node.ownerTag}</Mono>
      </GridField>
    </FieldGrid>
  )
}

function Agreement({ keys }: { keys: readonly NodeAgreement[] }) {
  return (
    <>
      <KeyValueTable label="Metadata" rows={keys.map((key) => ({ name: key.key, value: key.value }))} />
      {keys.map((key) =>
        key.isUnanimous ? null : differsOnEveryNode(key) ? (
          <Text key={key.key} as="p" tone="hint">
            {`${key.key}: Differs on every node`}
          </Text>
        ) : (
          <Text key={key.key} as="p" tone="hint">
            <Tag tone="proc">differs</Tag>
            {` ${key.key}: Matches on ${key.matches} of ${key.compared} live nodes. Others: ${groupsText(key.groups)}`}
          </Text>
        ),
      )}
    </>
  )
}
