import { useDashboard } from '@headless/dashboard-sdk'
import { DetailPanel, FieldGrid, GridField, Mono, PageFrame, PageHeader, StatusMark, Tag, useDelayed, usePageFrame } from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery } from '@headless/dashboard-sdk/query'
import { Link } from '@headless/dashboard-sdk/router'
import { DataTable } from '@headless/dashboard-sdk/table'
import { fetchNodes, nodeStatusMark, type JobNode } from '../cron.ts'
import { CellText, JobsStats, LoadError, Skeleton, StaleNotice } from '../parts.tsx'

/**
 * Servers: each live node, its role and liveness, and the functions its run filter claims; the open one docks under
 * the list. Read-only: the server serves no scheduler state, workers, queues, or registration time per node, and
 * starting or stopping a node's scheduler is not offered yet, so the view shows none of them.
 */
export default function NodesView() {
  const { api } = useDashboard()
  const format = useFormat()
  const nodes = useQuery({ queryKey: ['jobs', 'nodes'], queryFn: () => fetchNodes(api) })
  const showLoading = useDelayed(nodes.isPending)
  const rows = (nodes.data ?? []).map((node) => ({ ...node, id: node.identity }))
  const frame = usePageFrame({ viewId: 'jobs.nodes', rows })
  const open = rows.find((row) => row.id === frame.openId)
  const alive = rows.filter((row) => row.state === 'Alive').length

  return (
    <PageFrame
      frame={frame}
      stats={<JobsStats />}
      header={
        <PageHeader
          title="Servers"
          count={nodes.data ? format.number(rows.length) : undefined}
          description={nodes.data ? `${format.number(alive)} alive. Each runs the functions its run filter claims.` : undefined}
        />
      }
      panel={open ? <NodePanel node={open} /> : undefined}
    >
      <StaleNotice what="these servers" onRefresh={() => void nodes.refetch()} />
      {nodes.isPending ? (
        showLoading ? (
          <Skeleton label="Loading servers" rows={3} />
        ) : null
      ) : nodes.isError ? (
        <LoadError message={nodes.error.message} onRetry={() => void nodes.refetch()} />
      ) : (
        <DataTable
          label="Servers"
          viewId="jobs.nodes"
          frame={frame}
          rows={rows}
          emptyState="No live servers."
          columns={[
            {
              id: 'identity',
              header: 'Server',
              cell: (row) => (
                <CellText title={row.identity}>
                  <Mono>{row.identity}</Mono>
                </CellText>
              ),
              isRowHeader: true,
              width: '1.5fr',
            },
            { id: 'role', header: 'Role', cell: (row) => <CellText title={row.role}>{row.role ?? ''}</CellText>, priority: 2, width: '1fr' },
            {
              id: 'state',
              header: 'State',
              cell: (row) => (
                <CellText>
                  <StatusMark {...nodeStatusMark(row)} />
                </CellText>
              ),
              width: 120,
            },
            {
              id: 'claims',
              header: 'Claimed functions',
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
    </PageFrame>
  )
}

/** The functions the node's run filter claims, each a tag; a node that published no filter says so. */
function Claims({ node, links }: { node: JobNode; links?: boolean }) {
  if (!node.claimedFunctions) {
    return <span className="hd-faint">No run filter published</span>
  }

  if (node.claimedFunctions.length === 0) {
    return <span className="hd-faint">None</span>
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

function NodePanel({ node }: { node: JobNode }) {
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
                <FieldGrid columns={2}>
                  <GridField label="State">{node.state}</GridField>
                  <GridField label="Role">{node.role ?? 'None'}</GridField>
                </FieldGrid>
              </div>
              <section aria-label="Functions this server runs">
                <h3 className="hd-label">Functions this server runs</h3>
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
