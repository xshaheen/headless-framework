import { useDashboard } from '@headless/dashboard-sdk'
import { Inspector, InspectorField, InspectorFields, Mono, StatusMark, useDelayed } from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery } from '@headless/dashboard-sdk/query'
import { Link, useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable } from '@headless/dashboard-sdk/table'
import { fetchFunctions, type JobFunction } from '../cron.ts'
import { canvas, layout, LoadError, Skeleton, StaleNotice, Time } from '../parts.tsx'

/** Whether a live node's run filter claims the function: a function no node claims never runs. */
export function claimMark(item: Pick<JobFunction, 'claimedBy'>) {
  return item.claimedBy.length > 0
    ? ({ tone: 'healthy', label: `Claimed by ${item.claimedBy.length}` } as const)
    : ({ tone: 'needs', label: 'No live node' } as const)
}

/** A function's read-only fields, as the list's inspector and the function page show them. */
export function FunctionFields({ item }: { item: JobFunction }) {
  const format = useFormat()

  return (
    <InspectorFields>
      <InspectorField label="Contract version">
        <Mono>{item.contractVersion}</Mono>
      </InspectorField>
      <InspectorField label="Priority">{item.priority}</InspectorField>
      <InspectorField label="Concurrency">{item.maxConcurrency > 0 ? format.number(item.maxConcurrency) : 'Unlimited'}</InspectorField>
      {item.requestType ? (
        <InspectorField label="Argument type">
          <Mono>{item.requestType}</Mono>
        </InspectorField>
      ) : null}
      {item.cronExpression ? (
        <InspectorField label="Declared cron">
          <Mono>{item.cronExpression}</Mono>
        </InspectorField>
      ) : null}
      <InspectorField label="Run by">
        {item.claimedBy.length > 0 ? (
          item.claimedBy.map((node) => (
            <span key={node}>
              <Mono>{node}</Mono>{' '}
            </span>
          ))
        ) : (
          <span className="hd-error">No live node's run filter claims it, so its runs wait.</span>
        )}
      </InspectorField>
      <InspectorField label="Last 24 h">
        {`${format.number(item.succeeded)} succeeded, ${format.number(item.failed)} failed, ${format.number(item.running)} running, ${format.number(item.waiting)} waiting`}
      </InspectorField>
      <InspectorField label="Last run">
        <Time value={item.lastExecutedAt} />
      </InspectorField>
    </InspectorFields>
  )
}

/** Functions: every registered function, which live nodes claim it, and how its runs went in the last 24 hours. */
export default function FunctionsView() {
  const { api } = useDashboard()
  const format = useFormat()
  const [params, setParams] = useSearchParams()
  const functions = useQuery({ queryKey: ['jobs', 'functions'], queryFn: () => fetchFunctions(api) })
  const showLoading = useDelayed(functions.isPending)
  const rows = (functions.data ?? []).map((item) => ({ ...item, id: item.name }))
  const selectedId = params.get('selected') ?? undefined
  const selected = rows.find((row) => row.id === selectedId)

  return (
    <div style={layout}>
      <section aria-labelledby="jobs-functions-title" style={canvas}>
        <h1 id="jobs-functions-title" className="hd-title">
          Functions
        </h1>
        <StaleNotice what="these functions" onRefresh={() => void functions.refetch()} />
        {functions.isPending ? (
          showLoading ? (
            <Skeleton label="Loading functions" rows={4} />
          ) : null
        ) : functions.isError ? (
          <LoadError message={functions.error.message} onRetry={() => void functions.refetch()} />
        ) : (
          <DataTable
            label="Functions"
            viewId="jobs.functions"
            rows={rows}
            selectedIds={new Set(selectedId ? [selectedId] : [])}
            onSelectionChange={(ids) => setParams(ids.size ? { selected: [...ids][0]! } : {}, { replace: true })}
            emptyState="No functions registered on any live node."
            columns={[
              { id: 'name', header: 'Function', cell: (row) => <Mono>{row.name}</Mono>, isRowHeader: true, width: '2fr' },
              { id: 'claim', header: 'Run by', cell: (row) => <StatusMark {...claimMark(row)} />, width: 150 },
              { id: 'failed', header: 'Failed, 24 h', cell: (row) => format.number(row.failed), align: 'end', width: 110 },
              { id: 'succeeded', header: 'Succeeded, 24 h', cell: (row) => format.number(row.succeeded), align: 'end', priority: 2, width: 130 },
              { id: 'last', header: 'Last run', cell: (row) => <Time value={row.lastExecutedAt} />, priority: 2, width: '1fr' },
            ]}
          />
        )}
      </section>
      {selected ? (
        <Inspector label={`Function ${selected.name}`} onClose={() => setParams({}, { replace: true })}>
          <h2 className="hd-title">
            <Mono>{selected.name}</Mono>
          </h2>
          <div className="hd-actions">
            <Link to={`/jobs/functions/${encodeURIComponent(selected.name)}`}>Open function page</Link>
            <Link to={`/jobs/?function=${encodeURIComponent(selected.name)}`}>Its runs</Link>
          </div>
          <FunctionFields item={selected} />
        </Inspector>
      ) : (
        <Inspector label="Functions summary">
          <h2 className="hd-title">Functions</h2>
          <p>{functions.data ? `${rows.length} functions, ${rows.filter((row) => row.claimedBy.length === 0).length} not claimed by any live node.` : 'Functions appear here once they load.'}</p>
          <p className="hd-hint">Select a function to see what runs it and how its runs went.</p>
        </Inspector>
      )}
    </div>
  )
}
