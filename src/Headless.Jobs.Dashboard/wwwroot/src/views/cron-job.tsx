import { useDashboard } from '@headless/dashboard-sdk'
import { Inspector, Mono, ObjectHeader, PrimaryAction, ScheduleStrip, StatusMark, useDelayed, type ScheduleTick } from '@headless/dashboard-sdk/components'
import { DashboardApiError, useDashboardKeysetList, useDashboardObject } from '@headless/dashboard-sdk/data'
import { Link, useParams, useSearchParams } from '@headless/dashboard-sdk/router'
import { useStreamEvent } from '@headless/dashboard-sdk/stream'
import type { ReactNode } from 'react'
import { DataTable } from '@headless/dashboard-sdk/table'
import { CronActions, CronFields, NextFires, useCronZone } from '../cron-parts.tsx'
import { cronJobsTopic, cronJobTitle, cronStatusMark, fetchCronJob, type CronJob } from '../cron.ts'
import { AttemptTimeline, canvas, layout, LoadError, RunFields, Skeleton, StaleNotice, Time } from '../parts.tsx'
import { fetchRuns, runStatusMark, runsTopic, statusLabel, type Run } from '../runs.ts'

const hour = 3_600_000

/** A cron job's page: its schedule strip, next fires in its zone, editable schedule, actions, and occurrences. */
export default function CronJobPage() {
  const { api } = useDashboard()
  const cronJobId = useParams().cronJobId ?? ''
  const cronJob = useDashboardObject({ topic: cronJobsTopic, id: cronJobId, fetch: () => fetchCronJob(api, cronJobId) })
  const showLoading = useDelayed(cronJob.isPending)

  // A live change carries no next fires, so the page reads the cron job again for them.
  useStreamEvent('change', (change) => {
    if (change.topic === cronJobsTopic && change.id === cronJobId) {
      void cronJob.refetch()
    }
  })

  const back = <Link to="/jobs/cron-jobs">Back to cron jobs</Link>

  if (cronJob.isPending) {
    return showLoading ? <Skeleton label="Loading the cron job" rows={3} /> : null
  }

  if (cronJob.isError) {
    if (cronJob.error instanceof DashboardApiError && cronJob.error.status === 404) {
      return (
        <section aria-label="Cron job not found" className="hd-fields">
          <p role="alert">This cron job no longer exists.</p>
          {back}
        </section>
      )
    }

    return <LoadError message={cronJob.error.message} onRetry={() => void cronJob.refetch()} />
  }

  return <CronJobDetail cronJob={cronJob.data} back={back} onRefresh={() => void cronJob.refetch()} />
}

function CronJobDetail({ cronJob, back, onRefresh }: { cronJob: CronJob; back: ReactNode; onRefresh: () => void }) {
  const { api } = useDashboard()
  const zone = useCronZone(cronJob)
  const [params, setParams] = useSearchParams()
  const span = Number(params.get('span')) || hour
  const filter = { cronJobId: cronJob.id }
  const occurrences = useDashboardKeysetList<Run>({
    topic: runsTopic,
    params: filter,
    fetchPage: (cursor) => fetchRuns(api, filter, cursor),
  })
  const selectedRun = occurrences.rows.find((run) => run.id === params.get('selected'))
  const ticks: ScheduleTick[] = [
    ...occurrences.rows.map((run) => ({
      id: run.id,
      at: run.executionTime ?? run.createdAt,
      label: statusLabel(run.status).toLowerCase(),
      tone: runStatusMark(run).tone,
    })),
    ...(cronJob.isPaused ? [] : cronJob.nextFires).map((fire) => ({ id: `fire:${fire}`, at: fire, label: 'next fire' })),
  ]

  return (
    <div style={layout}>
      <article aria-label={`Cron job ${cronJob.id}`} style={canvas}>
        {back}
        <ObjectHeader title={cronJobTitle(cronJob)} id={cronJob.id} status={cronStatusMark(cronJob)} />
        <PrimaryAction>
          <CronActions cronJob={cronJob} />
        </PrimaryAction>
        <StaleNotice what="this cron job" onRefresh={onRefresh} />
        <CronFields cronJob={cronJob} />
        <ScheduleStrip
          label={`Schedule of ${cronJobTitle(cronJob)}`}
          tracks={[{ id: cronJob.id, label: cronJob.function, ticks }]}
          span={span}
          onSpanChange={(next) => setParams({ span: String(next) }, { replace: true })}
          timeZone={zone}
          onSelect={(id) => (id.startsWith('fire:') ? undefined : setParams({ span: String(span), selected: id }, { replace: true }))}
        />
        <section aria-label="Next fires" className="hd-fields">
          <h3 className="hd-title">{`Next fires, ${zone ?? 'scheduler zone'}`}</h3>
          <NextFires fires={cronJob.nextFires} zone={zone} paused={cronJob.isPaused} />
        </section>
        <section aria-label="Occurrences" className="hd-fields">
          <h3 className="hd-title">Occurrences</h3>
          {occurrences.query.isError ? (
            <LoadError message={occurrences.query.error.message} onRetry={() => void occurrences.query.refetch()} />
          ) : (
            <DataTable
              label="Occurrences"
              viewId="jobs.occurrences"
              rows={occurrences.rows}
              hasMore={occurrences.hasMore}
              isLoadingMore={occurrences.isLoadingMore}
              onLoadMore={occurrences.loadMore}
              selectedIds={new Set(params.getAll('selected'))}
              onSelectionChange={(ids) => setParams({ span: String(span), ...(ids.size ? { selected: [...ids][0]! } : {}) }, { replace: true })}
              emptyState="No occurrences yet. One appears each time the cron job fires."
              columns={[
                { id: 'due', header: 'Due', cell: (run) => <Time value={run.executionTime} zone={zone} />, isRowHeader: true, width: '1fr' },
                { id: 'status', header: 'Status', cell: (run) => <StatusMark {...runStatusMark(run)} />, width: 150 },
                { id: 'node', header: 'Node', cell: (run) => (run.node ? <Mono>{run.node}</Mono> : ''), priority: 2, width: '1fr' },
                { id: 'open', header: 'Run', cell: (run) => <Link to={`/jobs/runs/${encodeURIComponent(run.id)}`}>Open run</Link>, width: 100 },
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
          <RunFields run={selectedRun} />
          <AttemptTimeline run={selectedRun} />
        </Inspector>
      ) : (
        <Inspector label="Cron job summary">
          <h2 className="hd-title">Occurrences</h2>
          <p className="hd-hint">Select an occurrence in the strip or the list to see its fields and attempts.</p>
        </Inspector>
      )}
    </div>
  )
}
