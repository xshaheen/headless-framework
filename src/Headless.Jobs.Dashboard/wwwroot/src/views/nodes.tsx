import {
  Banner,
  DetailPanel,
  ExceptionBlock,
  FieldGrid,
  GridField,
  Meter,
  Mono,
  PageFrame,
  PageHeader,
  StatusMark,
  Tag,
  Text,
  usePageFrame,
  Timestamp,
} from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { Link } from '@headless/dashboard-sdk/router'
import { DataTable } from '@headless/dashboard-sdk/table'
import { NodeIcon } from '@headless/dashboard-sdk/icons'
import { nodeStatusMark, type JobNode } from '../cron.ts'
import { CellText, firstLine, JobsStats, QueryState, StaleNotice, useNodes } from '../parts.tsx'

export default function NodesView() {
  const format = useFormat()
  const nodes = useNodes()
  const rows = (nodes.data ?? []).map((node) => ({ ...node, id: node.identity }))
  const frame = usePageFrame({ viewId: 'jobs.nodes', rows })
  const open = rows.find((row) => row.id === frame.openId)
  const alive = rows.filter((row) => row.state === 'Alive').length
  const serving = rows.find((row) => row.isLocal && row.lastHostException)

  return (
    <PageFrame
      frame={frame}
      stats={<JobsStats />}
      header={
        <PageHeader
          title="Servers"
          count={nodes.data ? format.number(rows.length) : undefined}
          description={
            nodes.data ? `${format.number(alive)} alive. Each runs the functions its run filter claims.` : undefined
          }
        />
      }
      panel={open ? <NodePanel node={open} /> : undefined}
    >
      <StaleNotice what="these servers" onRefresh={() => void nodes.refetch()} failed={nodes.isError} />
      {serving ? <HostExceptionAlert node={serving} /> : null}
      <QueryState query={nodes} label="Loading servers" rows={3} shape="table">
        {() => (
          <DataTable
            label="Servers"
            frame={frame}
            rows={rows}
            emptyIcon={NodeIcon}
            emptyTitle="No live servers"
            emptyState="A server appears here once a host that runs Jobs starts."
            columns={[
              {
                id: 'identity',
                header: 'Server',
                cell: (row) => (
                  <CellText title={row.identity}>
                    <Mono>{row.identity}</Mono>
                    {row.isLocal ? <Tag>This node</Tag> : null}
                  </CellText>
                ),
                isRowHeader: true,
                kind: 'id',
                width: '1.5fr',
              },
              {
                id: 'role',
                header: 'Role',
                cell: (row) => <CellText title={row.role}>{row.role ?? ''}</CellText>,
                priority: 2,
                width: '1fr',
              },
              {
                id: 'state',
                header: 'State',
                cell: (row) => (
                  <CellText>
                    <StatusMark {...nodeStatusMark(row)} />
                  </CellText>
                ),
                kind: 'status',
                width: 120,
              },
              {
                id: 'workers',
                header: 'Workers',
                cell: (row) => <Workers node={row} />,
                width: 150,
              },
              {
                id: 'lastBeat',
                header: 'Last beat',
                cell: (row) => (
                  <CellText>
                    <Timestamp value={row.lastHeartbeatAt ?? undefined} missing="Not tracked" />
                  </CellText>
                ),
                kind: 'time',
                priority: 2,
                width: 110,
              },
              {
                id: 'claims',
                header: 'Claimed functions',
                // The list keeps the server and its state at narrow widths; the panel shows what it claims.
                priority: 2,
                cell: (row) => (
                  <CellText title={row.claimedFunctions?.join(', ')}>
                    <Claims node={row} />
                  </CellText>
                ),
                width: '2.5fr',
              },
            ]}
          />
        )}
      </QueryState>
    </PageFrame>
  )
}

/**
 * The node's busy workers against its max concurrency. A node that published none gives the meter no scale, so its
 * running count reads as text.
 */
function Workers({ node }: { node: JobNode }) {
  const format = useFormat()
  const running = format.count({ value: node.running, capped: node.runningCapped })

  if (node.maxConcurrency == null) {
    return (
      <CellText title="This node published no max concurrency.">
        {`${running} busy`} <Text tone="faint">· Not published</Text>
      </CellText>
    )
  }

  return (
    <Meter
      label={`Workers of ${node.identity}`}
      value={node.running}
      max={node.maxConcurrency}
      valueLabel={`${running} of ${format.number(node.maxConcurrency)} busy`}
    />
  )
}

/** The functions the node's run filter claims, each a tag; a node that published no filter says so. */
function Claims({ node, links }: { node: JobNode; links?: boolean }) {
  if (!node.claimedFunctions) {
    return <Text tone="faint">No run filter published</Text>
  }

  if (node.claimedFunctions.length === 0) {
    return <Text tone="faint">None</Text>
  }

  return (
    <>
      {node.claimedFunctions.map((name) => (
        <Tag key={name} variant="square">
          {links ? <Link to={`/jobs/functions/${encodeURIComponent(name)}`}>{name}</Link> : name}
        </Tag>
      ))}
    </>
  )
}

/** The serving node's last scheduler exception, the only one this page can know: a peer keeps its own. */
function HostExceptionAlert({ node }: { node: JobNode }) {
  return (
    <Banner>
      <Text role="alert" tone="error">
        {`This node's scheduler last failed: ${firstLine(node.lastHostException ?? undefined) ?? ''}`}
      </Text>
      <Timestamp value={node.lastHostExceptionAt ?? undefined} />
    </Banner>
  )
}

/** A peer's host exception lives in its own process, so only the serving node's can show here. */
function HostException({ node }: { node: JobNode }) {
  if (!node.isLocal) {
    return <Text tone="faint">Known only on that node</Text>
  }

  if (!node.lastHostException) {
    return <>None</>
  }

  const [message = '', ...stack] = node.lastHostException.split('\n')

  return (
    <>
      <Timestamp value={node.lastHostExceptionAt ?? undefined} />
      <ExceptionBlock message={message} stack={stack.length > 0 ? stack.join('\n') : undefined} />
    </>
  )
}

function NodePanel({ node }: { node: JobNode }) {
  const format = useFormat()

  return (
    <DetailPanel
      label={`Server ${node.identity}`}
      name={node.identity}
      status={nodeStatusMark(node)}
      tabs={[
        {
          id: 'overview',
          label: 'Overview',
          content: (
            <>
              <div>
                <FieldGrid label="Server">
                  <GridField label="State">{node.state}</GridField>
                  <GridField label="Role">{node.role ?? 'None'}</GridField>
                  <GridField label="Serves this page">{node.isLocal ? 'This node' : 'No'}</GridField>
                  <GridField label="Last beat">
                    <Timestamp value={node.lastHeartbeatAt ?? undefined} missing="Not tracked" />
                  </GridField>
                </FieldGrid>
                <FieldGrid label="Load">
                  <GridField label="Running">
                    {format.count({ value: node.running, capped: node.runningCapped })}
                  </GridField>
                  <GridField label="Max concurrency">
                    {node.maxConcurrency == null ? 'Not published' : format.number(node.maxConcurrency)}
                  </GridField>
                  <GridField label="Last host exception" span={2}>
                    <HostException node={node} />
                  </GridField>
                </FieldGrid>
              </div>
              <section aria-label="Functions this server runs">
                <Text as="h3" tone="label">
                  Functions this server runs
                </Text>
                <div>
                  <Claims node={node} links />
                </div>
              </section>
            </>
          ),
        },
      ]}
    />
  )
}
