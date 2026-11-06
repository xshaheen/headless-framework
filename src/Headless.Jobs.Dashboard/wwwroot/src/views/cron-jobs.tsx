import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { Button, Inspector, Mono, ObjectHeader, StatusMark, useDelayed } from '@headless/dashboard-sdk/components'
import { listKey, useDashboardList } from '@headless/dashboard-sdk/data'
import { useQueryClient } from '@headless/dashboard-sdk/query'
import { Link, useSearchParams } from '@headless/dashboard-sdk/router'
import { CronActions, CronFields } from '../cron-parts.tsx'
import { cronJobsTopic, cronJobTitle, cronStatusMark, fetchCronJobs, type CronJob } from '../cron.ts'
import { canvas, layout, LoadError, Skeleton, StaleNotice, Time } from '../parts.tsx'
import { viewOnlyReason } from '../runs.ts'
import { DataTable } from '@headless/dashboard-sdk/table'
import { CronJobForm } from './create.tsx'

/** Cron jobs: every definition with its expression, status, and next due time; the selection's inspector; Add. */
export default function CronJobsView() {
  const { api } = useDashboard()
  const module = useModule()
  const client = useQueryClient()
  const [params, setParams] = useSearchParams()
  const selectedId = params.get('selected') ?? undefined
  const creating = params.get('create') === 'cron-job'
  const list = useDashboardList<CronJob>({ topic: cronJobsTopic, fetch: () => fetchCronJobs(api) })
  const showLoading = useDelayed(list.isPending)
  const rows = list.data?.items ?? []
  const selected = rows.find((row) => row.id === selectedId)
  const addReason = module.canOperate ? null : viewOnlyReason

  function select(id: string | undefined) {
    setParams(id ? { selected: id } : {}, { replace: true })
  }

  let inspector
  if (creating) {
    inspector = (
      <CronJobForm
        onCancel={() => select(undefined)}
        onCreated={(id) => {
          void client.invalidateQueries({ queryKey: listKey(cronJobsTopic) })
          select(id)
        }}
      />
    )
  } else if (selected) {
    inspector = (
      <Inspector label={`Cron job ${selected.id}`} onClose={() => select(undefined)}>
        <ObjectHeader
          title={cronJobTitle(selected)}
          id={selected.id}
          status={cronStatusMark(selected)}
          actions={
            <>
              <Link to={`/jobs/cron-jobs/${encodeURIComponent(selected.id)}`}>
                Open cron job page
              </Link>
              <CronActions cronJob={selected} />
            </>
          }
        />
        <CronFields key={selected.id} cronJob={selected} />
      </Inspector>
    )
  } else {
    inspector = (
      <Inspector label="Cron jobs summary">
        <h2 className="hd-title">Cron jobs</h2>
        <p>{list.data ? `${rows.length} cron jobs, ${rows.filter((row) => row.isPaused).length} paused.` : 'Cron jobs appear here once they load.'}</p>
        <p className="hd-hint">Select a cron job to see its schedule and the actions allowed now.</p>
      </Inspector>
    )
  }

  return (
    <div style={layout}>
      <section aria-labelledby="jobs-cron-title" style={canvas}>
        <header className="hd-actions">
          <h1 id="jobs-cron-title" className="hd-title">
            Cron jobs
          </h1>
          <Button variant="primary" disabledReason={addReason} onPress={() => setParams({ create: 'cron-job' }, { replace: true })}>
            Add cron job
          </Button>
        </header>
        <StaleNotice what="these cron jobs" onRefresh={() => void list.refetch()} />
        {list.isPending ? (
          showLoading ? (
            <Skeleton label="Loading cron jobs" rows={4} />
          ) : null
        ) : list.isError ? (
          <LoadError message={list.error.message} onRetry={() => void list.refetch()} />
        ) : (
          <DataTable
            label="Cron jobs"
            viewId="jobs.cron-jobs"
            rows={rows}
            selectedIds={new Set(selectedId ? [selectedId] : [])}
            onSelectionChange={(ids) => select([...ids][0])}
            emptyState="No cron jobs. Declare one with a [Job] cron attribute, or add one here."
            columns={[
              { id: 'title', header: 'Cron job', cell: (row) => cronJobTitle(row), isRowHeader: true, width: '2fr' },
              { id: 'expression', header: 'Expression', cell: (row) => <Mono>{row.expression}</Mono>, priority: 2, width: '1fr' },
              { id: 'status', header: 'Status', cell: (row) => <StatusMark {...cronStatusMark(row)} />, width: 120 },
              { id: 'next', header: 'Next due', cell: (row) => (row.isPaused ? '' : <Time value={row.nextDueUtc} />), width: '1fr' },
            ]}
          />
        )}
      </section>
      {inspector}
    </div>
  )
}
