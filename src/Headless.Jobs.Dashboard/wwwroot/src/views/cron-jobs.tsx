import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { Button, DetailPanel, Mono, PageFrame, PageHeader, Tag, useDelayed, usePageFrame } from '@headless/dashboard-sdk/components'
import { DashboardApiError, listKey, useDashboardList } from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQueryClient } from '@headless/dashboard-sdk/query'
import { useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable } from '@headless/dashboard-sdk/table'
import { CronPanel, useCronJob } from '../cron-parts.tsx'
import { cronJobsTopic, fetchCronJobs, type CronJob } from '../cron.ts'
import { CellText, JobsStats, LoadError, Skeleton, StaleNotice, Time } from '../parts.tsx'
import { viewOnlyReason } from '../runs.ts'
import { CronJobForm } from './create.tsx'

/**
 * Recurring jobs: every cron job with its function, schedule and plain reading, and next run, live; the open one docks
 * under the list, and `Add recurring job` opens the add form there. The last run of each is not served, so the list
 * shows none.
 */
export default function CronJobsView() {
  const { api } = useDashboard()
  const module = useModule()
  const format = useFormat()
  const client = useQueryClient()
  const [params, setParams] = useSearchParams()
  const creating = params.get('create') === 'cron-job'
  const list = useDashboardList<CronJob>({ topic: cronJobsTopic, fetch: () => fetchCronJobs(api) })
  const showLoading = useDelayed(list.isPending)
  const rows = list.data?.items ?? []
  const frame = usePageFrame({ viewId: 'jobs.cron-jobs', rows })
  const openRow = rows.find((row) => row.id === frame.openId)

  function setCreate(on: boolean) {
    setParams(
      (current) => {
        const next = new URLSearchParams(current)
        next.delete('open')

        if (on) {
          next.set('create', 'cron-job')
        } else {
          next.delete('create')
        }

        return next
      },
      { replace: true },
    )
  }

  let panel
  if (frame.openId) {
    panel = <OpenCronJob key={frame.openId} id={frame.openId} row={openRow} />
  } else if (creating) {
    panel = (
      <CronJobForm
        onCancel={() => setCreate(false)}
        onCreated={(id) => {
          void client.invalidateQueries({ queryKey: listKey(cronJobsTopic) })
          setParams({ open: id }, { replace: true })
        }}
      />
    )
  }

  return (
    <PageFrame
      // The form is not a row, so the panel's Close ends the form rather than closing an open row.
      frame={creating && !frame.openId ? { ...frame, close: () => setCreate(false) } : frame}
      form={creating && !frame.openId}
      stats={<JobsStats />}
      header={
        <PageHeader
          title="Recurring jobs"
          count={list.data ? format.number(rows.length) : undefined}
          description="Each runs its function on its cron schedule, in its zone."
          actions={
            <Button variant="primary" disabledReason={module.canOperate ? null : viewOnlyReason} onPress={() => setCreate(true)}>
              Add recurring job
            </Button>
          }
        />
      }
      panel={panel}
    >
      <StaleNotice what="these recurring jobs" onRefresh={() => void list.refetch()} />
      {list.isPending ? (
        showLoading ? (
          <Skeleton label="Loading recurring jobs" rows={4} />
        ) : null
      ) : list.isError ? (
        <LoadError message={list.error.message} onRetry={() => void list.refetch()} />
      ) : (
        <DataTable
          label="Recurring jobs"
          viewId="jobs.cron-jobs"
          frame={frame}
          rows={rows}
          emptyState="No recurring jobs. Declare one with a [Job] cron attribute, or add one here."
          columns={[
            {
              id: 'function',
              header: 'Function',
              cell: (row) => (
                <CellText title={row.function}>
                  <Mono>{row.function}</Mono>
                </CellText>
              ),
              isRowHeader: true,
              width: '1.5fr',
            },
            { id: 'description', header: 'Description', cell: (row) => <CellText title={row.description}>{row.description}</CellText>, priority: 2, width: '1.5fr' },
            {
              id: 'schedule',
              header: 'Schedule',
              cell: (row) => (
                <CellText title={row.reading ?? row.validationMessage}>
                  <Mono>{row.expression}</Mono> <span className="hd-faint">{row.reading ?? row.validationMessage}</span>
                </CellText>
              ),
              width: '2fr',
            },
            {
              id: 'next',
              header: 'Next run',
              cell: (row) =>
                row.isPaused ? (
                  <Tag variant="pill" tone="off">
                    Paused
                  </Tag>
                ) : (
                  <CellText>
                    <Time value={row.nextDueUtc} short />
                  </CellText>
                ),
              align: 'end',
              width: 120,
            },
          ]}
        />
      )}
    </PageFrame>
  )
}

/**
 * The open cron job, read by its id for its next fires; the list row stands in while that read is under way, and a
 * cron job no longer listed shows only once the read answers.
 */
function OpenCronJob({ id, row }: { id: string; row: CronJob | undefined }) {
  const detail = useCronJob(id)
  const cronJob = detail.data ?? row

  if (cronJob) {
    return <CronPanel cronJob={cronJob} fires={detail.data?.nextFires} />
  }

  return (
    <DetailPanel label={`Cron job ${id}`} name="Recurring job" id={id}>
      {detail.isError ? (
        detail.error instanceof DashboardApiError && detail.error.status === 404 ? (
          <p role="alert">This recurring job no longer exists.</p>
        ) : (
          <LoadError message={detail.error.message} onRetry={() => void detail.refetch()} />
        )
      ) : (
        <Skeleton label="Loading the recurring job" />
      )}
    </DetailPanel>
  )
}
