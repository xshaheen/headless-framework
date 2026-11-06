import { useDashboard } from '@headless/dashboard-sdk'
import { Inspector, InspectorField, InspectorFields, Mono, StatusMark, useDelayed } from '@headless/dashboard-sdk/components'
import { useQuery } from '@headless/dashboard-sdk/query'
import { Link, useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable } from '@headless/dashboard-sdk/table'
import { fetchNodes, nodeStatusMark } from '../cron.ts'
import { canvas, layout, LoadError, Skeleton, StaleNotice } from '../parts.tsx'

/**
 * Nodes: each live node, its liveness, and the functions its run filter claims. Read-only: the scheduler's state
 * and its start and stop are not offered here yet.
 */
export default function NodesView() {
  const { api } = useDashboard()
  const [params, setParams] = useSearchParams()
  const nodes = useQuery({ queryKey: ['jobs', 'nodes'], queryFn: () => fetchNodes(api) })
  const showLoading = useDelayed(nodes.isPending)
  const rows = (nodes.data ?? []).map((node) => ({ ...node, id: node.identity }))
  const selectedId = params.get('selected') ?? undefined
  const selected = rows.find((row) => row.id === selectedId)

  return (
    <div style={layout}>
      <section aria-labelledby="jobs-nodes-title" style={canvas}>
        <h1 id="jobs-nodes-title" className="hd-title">
          Nodes
        </h1>
        <StaleNotice what="these nodes" onRefresh={() => void nodes.refetch()} />
        {nodes.isPending ? (
          showLoading ? (
            <Skeleton label="Loading nodes" rows={3} />
          ) : null
        ) : nodes.isError ? (
          <LoadError message={nodes.error.message} onRetry={() => void nodes.refetch()} />
        ) : (
          <DataTable
            label="Nodes"
            viewId="jobs.nodes"
            rows={rows}
            selectedIds={new Set(selectedId ? [selectedId] : [])}
            onSelectionChange={(ids) => setParams(ids.size ? { selected: [...ids][0]! } : {}, { replace: true })}
            emptyState="No live nodes."
            columns={[
              { id: 'identity', header: 'Node', cell: (row) => <Mono>{row.identity}</Mono>, isRowHeader: true, width: '2fr' },
              { id: 'state', header: 'Liveness', cell: (row) => <StatusMark {...nodeStatusMark(row)} />, width: 130 },
              { id: 'role', header: 'Role', cell: (row) => row.role ?? '', priority: 2, width: '1fr' },
              {
                id: 'claims',
                header: 'Claims',
                cell: (row) => (row.claimedFunctions ? `${row.claimedFunctions.length} functions` : 'Not published'),
                width: '1fr',
              },
            ]}
          />
        )}
      </section>
      {selected ? (
        <Inspector label={`Node ${selected.identity}`} onClose={() => setParams({}, { replace: true })}>
          <h2 className="hd-title">
            <Mono>{selected.identity}</Mono>
          </h2>
          <InspectorFields>
            <InspectorField label="Liveness">
              <StatusMark {...nodeStatusMark(selected)} />
            </InspectorField>
            <InspectorField label="Role">{selected.role ?? 'None'}</InspectorField>
            <InspectorField label="Claims">
              {selected.claimedFunctions ? (
                <ul>
                  {selected.claimedFunctions.map((name) => (
                    <li key={name}>
                      <Link to={`/jobs/functions/${encodeURIComponent(name)}`}>
                        <Mono>{name}</Mono>
                      </Link>
                    </li>
                  ))}
                </ul>
              ) : (
                'This node published no run filter.'
              )}
            </InspectorField>
          </InspectorFields>
        </Inspector>
      ) : (
        <Inspector label="Nodes summary">
          <h2 className="hd-title">Nodes</h2>
          <p>{nodes.data ? `${rows.length} live ${rows.length === 1 ? 'node' : 'nodes'}.` : 'Nodes appear here once they load.'}</p>
          <p className="hd-hint">Select a node to see the functions its run filter claims.</p>
        </Inspector>
      )}
    </div>
  )
}
