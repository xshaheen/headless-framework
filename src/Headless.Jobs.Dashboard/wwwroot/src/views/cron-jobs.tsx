import { useDashboard, useModule } from '@headless/dashboard-sdk'
import {
  Button,
  DetailPanel,
  EmptyState,
  LoadError,
  Mono,
  PageFrame,
  PageHeader,
  Skeleton,
  StatusMark,
  Tag,
  Text,
  usePageFrame,
  Timestamp,
  QueryState,
} from '@headless/dashboard-sdk/components'
import { DashboardApiError, listKey, useDashboardList } from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQueryClient } from '@headless/dashboard-sdk/query'
import { Link, useSearchParams } from '@headless/dashboard-sdk/router'
import { useStreamEvent, useTopics } from '@headless/dashboard-sdk/stream'
import { DataTable } from '@headless/dashboard-sdk/table'
import { RecurringIcon } from '@headless/dashboard-sdk/icons'
import { CronPanel, useCronJob } from '../cron-parts.tsx'
import { cronJobsTopic, fetchCronJobs, type CronJob, type JobFunction } from '../cron.ts'
import { CellText, JobsStats, StaleNotice, useFunctions, useSettledRefetch } from '../parts.tsx'
import { readRunProjection, runStatusMark, runsTopic, viewOnlyReason, type RunProjectionFacts } from '../runs.ts'
import { CronJobForm } from './create.tsx'

/**
 * Whether a run change can move the last run a row shows. A change with no projection names no occurrence, and may be
 * a removed one, so it can. The occurrence a row already shows moves it only by changing status, which leaves out the
 * progress it reports while it runs; any other occurrence moves it once it has started.
 */
function movesLastRun(rows: readonly CronJob[], id: string, run: RunProjectionFacts | undefined) {
  if (!run) {
    return true
  }

  if (run.kind !== 'CronJobOccurrence') {
    return false
  }

  const shown = rows.find((row) => row.id === run.cronJobId)?.lastRun

  return shown?.id === id ? shown.status !== run.status : run.status !== 'Idle' && run.status !== 'Queued'
}

export default function CronJobsView() {
  const { api } = useDashboard()
  const module = useModule()
  const format = useFormat()
  const client = useQueryClient()
  const [params, setParams] = useSearchParams()
  const creating = params.get('create') === 'cron-job'
  const list = useDashboardList<CronJob>({ topic: cronJobsTopic, fetch: () => fetchCronJobs(api) })
  const rows = list.data?.items ?? []
  const frame = usePageFrame({ viewId: 'jobs.cron-jobs', rows })
  const openRow = rows.find((row) => row.id === frame.openId)
  // The functions' counts are an extra fact beside the schedules, so only a read that answered fills the column; a
  // pending or failed one leaves it blank rather than putting an error in this list.
  const functions = useFunctions()
  const functionsByName = new Map((functions.isSuccess ? functions.data : []).map((item) => [item.name, item]))
  const reread = useSettledRefetch(listKey(cronJobsTopic))

  // Only the list read serves a cron job's last run, so the list is read again once a burst of occurrence changes that
  // can move one settles.
  useTopics([runsTopic])
  useStreamEvent('change', (change) => {
    if (change.topic === runsTopic && movesLastRun(rows, change.id, readRunProjection(change.projection))) {
      reread()
    }
  })

  /**
   * `Succeeded · 12 min. ago`: how the latest occurrence stands, opening its run, then when it ended. One in progress
   * has no end yet, so it shows its status alone.
   */
  function lastRun(row: CronJob) {
    const run = row.lastRun

    if (!run) {
      return undefined
    }

    const mark = runStatusMark(run)

    return (
      <CellText>
        <Link to={`/jobs/runs/${encodeURIComponent(run.id)}`} aria-label={`Last run of ${row.function}: ${mark.label}`}>
          <StatusMark {...mark} />
        </Link>
        {run.executedAt ? (
          <>
            {' · '}
            <Timestamp value={run.executedAt} />
          </>
        ) : null}
      </CellText>
    )
  }

  /** `10 succeeded · 2 failed`, a zero part left out, the failed part opening the function's failed runs. */
  function functionRuns(item: JobFunction | undefined) {
    if (!item || (item.succeeded === 0 && item.failed === 0)) {
      return ''
    }

    const succeeded = item.succeeded > 0 ? `${format.number(item.succeeded)} succeeded` : undefined
    const failed = item.failed > 0 ? `${format.number(item.failed)} failed` : undefined

    return (
      <CellText title={`${item.name}, last 24 hours: ${[succeeded, failed].filter(Boolean).join(' · ')}`}>
        {succeeded}
        {succeeded && failed ? ' · ' : null}
        {failed ? (
          <Link
            to={`/jobs/runs?status=Failed&function=${encodeURIComponent(item.name)}`}
            aria-label={`${failed} runs of ${item.name}`}
          >
            {failed}
          </Link>
        ) : null}
      </CellText>
    )
  }

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
            <Button
              variant="primary"
              disabledReason={module.canOperate ? null : viewOnlyReason}
              onPress={() => setCreate(true)}
            >
              Add recurring job
            </Button>
          }
        />
      }
      panel={panel}
    >
      <StaleNotice what="these recurring jobs" onRefresh={() => void list.refetch()} failed={list.isError} />
      <QueryState query={list} label="Loading recurring jobs" rows={4} shape="table">
        {() => (
          <DataTable
            label="Recurring jobs"
            frame={frame}
            rows={rows}
            emptyIcon={RecurringIcon}
            emptyTitle="No recurring jobs"
            emptyState="Declare one with a [Job] cron attribute, or add one here."
            emptyAction={
              <Button disabledReason={module.canOperate ? null : viewOnlyReason} onPress={() => setCreate(true)}>
                Add recurring job
              </Button>
            }
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
                kind: 'id',
                width: '1.5fr',
              },
              {
                id: 'description',
                header: 'Description',
                cell: (row) => <CellText title={row.description}>{row.description}</CellText>,
                priority: 2,
                width: '1.5fr',
              },
              {
                id: 'schedule',
                header: 'Schedule',
                cell: (row) => (
                  <CellText title={row.reading ?? row.validationMessage}>
                    <Mono>{row.expression}</Mono> <Text tone="faint">{row.reading ?? row.validationMessage}</Text>
                  </CellText>
                ),
                width: '2fr',
              },
              {
                id: 'last-run',
                header: 'Last run',
                cell: lastRun,
                kind: 'status',
                priority: 2,
                // Wide enough for a status and the age of its end, `Succeeded · 12 min. ago`.
                width: 210,
              },
              {
                id: 'function-runs',
                // Every run of the row's function, a time job or another schedule's included, so the header names
                // the function and never this recurring job.
                header: 'Function runs, 24 h',
                cell: (row) => functionRuns(functionsByName.get(row.function)),
                priority: 2,
                width: 200,
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
                      <Timestamp value={row.nextDueUtc} scheduled />
                    </CellText>
                  ),
                kind: 'time',
                align: 'end',
                // Wide enough for a fire that is overdue, `due 42 sec. ago`, the longest text the cell shows.
                width: 150,
              },
            ]}
          />
        )}
      </QueryState>
    </PageFrame>
  )
}

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
          <EmptyState role="alert" title="This recurring job no longer exists." />
        ) : (
          <LoadError message={detail.error.message} onRetry={() => void detail.refetch()} />
        )
      ) : (
        <Skeleton label="Loading the recurring job" shape="fields" rows={3} />
      )}
    </DetailPanel>
  )
}
