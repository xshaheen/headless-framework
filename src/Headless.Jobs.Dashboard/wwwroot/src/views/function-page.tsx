import { useDashboard } from '@headless/dashboard-sdk'
import { Inspector, Mono, ObjectHeader, ScheduleStrip, StatusMark, useDelayed, type ScheduleTick } from '@headless/dashboard-sdk/components'
import { useDashboardKeysetList, useDashboardList } from '@headless/dashboard-sdk/data'
import { useQuery } from '@headless/dashboard-sdk/query'
import { Link, useParams, useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable } from '@headless/dashboard-sdk/table'
import { cronJobsTopic, cronJobTitle, cronStatusMark, fetchCronJobs, fetchFunctions, type CronJob } from '../cron.ts'
import { canvas, layout, LoadError, Skeleton, StaleNotice, Time } from '../parts.tsx'
import { fetchRuns, runStatusMark, runsTopic, statusLabel, type Run } from '../runs.ts'
import { claimMark, FunctionFields } from './functions.tsx'

const hour = 3_600_000

/** A function's page: its contract, what claims it, its strip, its cron jobs, and its runs. */
export default function FunctionPage() {
  const { api } = useDashboard()
  const name = useParams().name ?? ''
  const [params, setParams] = useSearchParams()
  const span = Number(params.get('span')) || hour
  const functions = useQuery({ queryKey: ['jobs', 'functions'], queryFn: () => fetchFunctions(api) })
  const showLoading = useDelayed(functions.isPending)
  const filter = { function: name }
  const runs = useDashboardKeysetList<Run>({ topic: runsTopic, params: filter, fetchPage: (cursor) => fetchRuns(api, filter, cursor) })
  const cronJobs = useDashboardList<CronJob>({ topic: cronJobsTopic, params: filter, fetch: () => fetchCronJobs(api, name) })
  const item = functions.data?.find((candidate) => candidate.name === name)
  const back = <Link to="/jobs/functions">Back to functions</Link>

  if (functions.isPending) {
    return showLoading ? <Skeleton label="Loading the function" rows={4} /> : null
  }

  if (functions.isError) {
    return <LoadError message={functions.error.message} onRetry={() => void functions.refetch()} />
  }

  if (!item) {
    return (
      <section aria-label="Function not found" className="hd-fields">
        <p role="alert">{`No live node registers the function ${name}.`}</p>
        {back}
      </section>
    )
  }

  const ticks: ScheduleTick[] = [
    ...runs.rows.map((run) => ({
      id: run.id,
      at: run.executionTime ?? run.createdAt,
      label: statusLabel(run.status).toLowerCase(),
      tone: runStatusMark(run).tone,
    })),
    // Every next fire the server computed, so a frequent cron job fills the window to the right of now.
    ...(cronJobs.data?.items ?? []).flatMap((cronJob) =>
      cronJob.nextFires.map((fire) => ({ id: `fire:${cronJob.id}:${fire}`, at: fire, label: 'next fire' })),
    ),
  ]
  const selectedRun = runs.rows.find((run) => run.id === params.get('selected'))

  return (
    <div style={layout}>
      <article aria-label={`Function ${name}`} style={canvas}>
        {back}
        <ObjectHeader title={name} id={name} status={claimMark(item)} />
        <StaleNotice what="this function" onRefresh={() => void functions.refetch()} />
        <FunctionFields item={item} />
        <ScheduleStrip
          label={`Schedule of ${name}`}
          tracks={[{ id: name, label: name, ticks }]}
          span={span}
          onSpanChange={(next) => setParams({ span: String(next) }, { replace: true })}
          onSelect={(id) => (id.startsWith('fire:') ? undefined : setParams({ span: String(span), selected: id }, { replace: true }))}
        />
        <section aria-label="Cron jobs" className="hd-fields">
          <h3 className="hd-title">Cron jobs</h3>
          {cronJobs.isError ? (
            <LoadError message={cronJobs.error.message} onRetry={() => void cronJobs.refetch()} />
          ) : (cronJobs.data?.items ?? []).length === 0 ? (
            <p className="hd-hint">No cron jobs run this function.</p>
          ) : (
            <ul>
              {cronJobs.data!.items.map((cronJob) => (
                <li key={cronJob.id}>
                  <Link to={`/jobs/cron-jobs/${encodeURIComponent(cronJob.id)}`}>{cronJobTitle(cronJob)}</Link>{' '}
                  <StatusMark {...cronStatusMark(cronJob)} />
                </li>
              ))}
            </ul>
          )}
        </section>
        <section aria-label="Runs" className="hd-fields">
          <h3 className="hd-title">Runs</h3>
          <Link to={`/jobs/?function=${encodeURIComponent(name)}`}>Open in Runs</Link>
          {runs.query.isError ? (
            <LoadError message={runs.query.error.message} onRetry={() => void runs.query.refetch()} />
          ) : (
            <DataTable
              label={`Runs of ${name}`}
              viewId="jobs.function-runs"
              rows={runs.rows}
              hasMore={runs.hasMore}
              isLoadingMore={runs.isLoadingMore}
              onLoadMore={runs.loadMore}
              selectedIds={new Set(selectedRun ? [selectedRun.id] : [])}
              onSelectionChange={(ids) => setParams({ span: String(span), ...(ids.size ? { selected: [...ids][0]! } : {}) }, { replace: true })}
              emptyState="No runs of this function yet."
              columns={[
                { id: 'due', header: 'Due', cell: (run) => <Time value={run.executionTime ?? run.createdAt} />, isRowHeader: true, width: '1fr' },
                { id: 'status', header: 'Status', cell: (run) => <StatusMark {...runStatusMark(run)} />, width: 150 },
                { id: 'node', header: 'Node', cell: (run) => (run.node ? <Mono>{run.node}</Mono> : ''), priority: 2, width: '1fr' },
              ]}
            />
          )}
        </section>
      </article>
      {selectedRun ? (
        <Inspector label={`Run ${selectedRun.id}`} onClose={() => setParams({ span: String(span) }, { replace: true })}>
          <ObjectHeader
            title={selectedRun.function}
            id={selectedRun.id}
            status={runStatusMark(selectedRun)}
            actions={<Link to={`/jobs/runs/${encodeURIComponent(selectedRun.id)}`}>Open run page</Link>}
          />
        </Inspector>
      ) : (
        <Inspector label="Function summary">
          <h2 className="hd-title">
            <Mono>{name}</Mono>
          </h2>
          <p className="hd-hint">Select a run in the strip or the list to see it here.</p>
        </Inspector>
      )}
    </div>
  )
}
