import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { CommandAction, useAllowedActions } from '@headless/dashboard-sdk/commands'
import { Button, Card, CardRow, Cards, Mono, PageFrame, PageHeader, usePageFrame } from '@headless/dashboard-sdk/components'
import { useDashboardKeysetList, useDashboardList, useNewRows } from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { Link, useNavigate } from '@headless/dashboard-sdk/router'
import { useEffect } from 'react'
import { cronJobsTopic, fetchCronJobs, type CronJob } from '../cron.ts'
import { JobsStats, LoadError, RunProgress, Skeleton, StaleNotice, Time, useJobsAttention, type AttentionItem } from '../parts.tsx'
import { fetchRuns, pendingLabels, readFilter, runsTopic, useJobsCommands, usePending, viewOnlyReason, type Run } from '../runs.ts'

/** The rows each card shows before its `All` link takes over. */
const cardRows = 6

/** The in-progress runs, as the runs list filtered to In progress reads them, so both share one cached list. */
const processingFilter = { ...readFilter(new URLSearchParams()), status: 'InProgress' }

/**
 * The Jobs Dashboard: the state counts and served figures, the runs in progress with their progress, the next fires
 * of the recurring jobs, and the Jobs attention items. Throughput, history, workers, and queue lag wait until the
 * server serves them, so the page draws none of them.
 */
export default function JobsDashboard() {
  const module = useModule()
  const navigate = useNavigate()
  const cronJobs = useNextRecurringRuns()
  const next = cronJobs.next[0]
  const frame = usePageFrame({ viewId: 'jobs.dashboard', rows: [] })

  return (
    <PageFrame
      frame={frame}
      stats={<JobsStats />}
      header={
        <PageHeader
          title="Dashboard"
          description={
            next ? (
              <>
                Next execution <Time value={next.nextDueUtc} /> · <Mono>{next.function}</Mono>
              </>
            ) : undefined
          }
          actions={
            <Button variant="primary" disabledReason={module.canOperate ? null : viewOnlyReason} onPress={() => void navigate('/jobs/runs?create=time-job')}>
              Add time job
            </Button>
          }
        />
      }
    >
      <StaleNotice what="this dashboard" onRefresh={() => void cronJobs.list.refetch()} />
      <Cards>
        <ProcessingNow />
        <NextRecurringRuns {...cronJobs} />
        <JobsAttention />
      </Cards>
    </PageFrame>
  )
}

/**
 * The runs in progress, newest first, with their progress and Cancel. A live change patches a listed run in place
 * and drops one that finished; a run that starts while the page is open arrives as a new row, which this card shows
 * at once, because it has no reading place to hold.
 */
function ProcessingNow() {
  const { api } = useDashboard()
  const module = useModule()
  const format = useFormat()
  const runs = useDashboardKeysetList<Run>({ topic: runsTopic, params: processingFilter, fetchPage: (cursor) => fetchRuns(api, processingFilter, cursor) })
  const arrived = useNewRows<Run>(runsTopic, (run) => run.status === 'InProgress')
  const commands = useJobsCommands()
  const pending = usePending()
  const running = runs.rows.filter((run) => run.status === 'InProgress')
  const shown = running.slice(0, cardRows)
  const allowed = useAllowedActions('run', shown)
  const cancel = commands.cancel
  const refused = allowed.refused(cancel?.id)
  const show = arrived.show

  useEffect(() => {
    if (arrived.count > 0) {
      void show()
    }
  }, [arrived.count, show])

  return (
    <Card
      title="Processing now"
      caption={runs.count ? `${format.count(runs.count)} running` : undefined}
      action={running.length > 0 ? <Link to="/jobs/runs?status=InProgress">All</Link> : undefined}
      empty={
        runs.query.isError ? (
          <LoadError message={runs.query.error.message} onRetry={() => void runs.query.refetch()} />
        ) : runs.query.isPending ? (
          <Skeleton label="Loading the runs in progress" rows={2} />
        ) : (
          'Nothing is running. A due run starts once a node picks it up.'
        )
      }
    >
      {shown.map((run) => (
        <CardRow
          key={run.id}
          tone="proc"
          name={<Link to={`/jobs/runs?status=InProgress&open=${encodeURIComponent(run.id)}`}>{run.function}</Link>}
          meta={run.node ? <Mono>{run.node}</Mono> : undefined}
          actions={
            cancel ? (
              <CommandAction
                module={module.id}
                command={cancel}
                ids={[run.id]}
                noun="run"
                commands={commands.all}
                disabledReason={refused.get(run.id) ?? null}
                onDone={(rows) =>
                  pending.mark(
                    rows.filter((row) => row.outcome === 'done').map((row) => row.id),
                    new Map([[run.id, run.version]]),
                    pendingLabels.cancel!,
                  )
                }
              />
            ) : undefined
          }
        >
          {pending.labelOf(run) ?? (run.cancelRequested ? 'Cancelling…' : run.progressPercent !== undefined ? <RunProgress run={run} /> : 'No progress reported')}
        </CardRow>
      ))}
    </Card>
  )
}

/** The active cron jobs ordered by their next due time. */
function useNextRecurringRuns() {
  const { api } = useDashboard()
  const list = useDashboardList<CronJob>({ topic: cronJobsTopic, fetch: () => fetchCronJobs(api) })
  const all = list.data?.items ?? []
  const next = all.filter((cronJob) => !cronJob.isPaused).sort((left, right) => Date.parse(left.nextDueUtc) - Date.parse(right.nextDueUtc))

  return { list, total: all.length, next }
}

function NextRecurringRuns({ list, total, next }: ReturnType<typeof useNextRecurringRuns>) {
  const module = useModule()
  const navigate = useNavigate()

  return (
    <Card
      title="Next recurring runs"
      action={total > 0 ? <Link to="/jobs/cron-jobs">{`All ${total}`}</Link> : undefined}
      empty={
        list.isError ? (
          <LoadError message={list.error.message} onRetry={() => void list.refetch()} />
        ) : list.isPending ? (
          <Skeleton label="Loading the recurring jobs" rows={2} />
        ) : total > 0 ? (
          'Every recurring job is paused.'
        ) : (
          <>
            No recurring jobs yet. A recurring job runs a function on a cron schedule.{' '}
            <Button disabledReason={module.canOperate ? null : viewOnlyReason} onPress={() => void navigate('/jobs/cron-jobs?create=cron-job')}>
              Add recurring job
            </Button>
          </>
        )
      }
    >
      {next.slice(0, cardRows).map((cronJob) => (
        <CardRow
          key={cronJob.id}
          tone="sched"
          name={<Link to={`/jobs/cron-jobs/${encodeURIComponent(cronJob.id)}`}>{cronJob.function}</Link>}
          meta={cronJob.expression}
          value={<Time value={cronJob.nextDueUtc} short />}
        />
      ))}
    </Card>
  )
}

/** Where an attention item leads: a failure group to its failed runs, anything else to the object it names. */
function attentionPath(item: AttentionItem) {
  const scope = item.recommended?.filter
  const fn = scope?.functions?.[0]

  if (fn && scope.fingerprint) {
    return `/jobs/runs?status=Failed&function=${encodeURIComponent(fn)}&fingerprint=${encodeURIComponent(scope.fingerprint)}`
  }

  return item.target?.kind === 'function' ? `/jobs/functions/${encodeURIComponent(item.target.id)}` : '/jobs/runs?status=Failed'
}

function JobsAttention() {
  const { answer, items } = useJobsAttention()

  return (
    <Card
      title="Needs attention"
      caption={items.length > 0 ? String(items.length) : undefined}
      empty={
        answer.isError ? (
          <LoadError message={answer.error.message} onRetry={() => void answer.refetch()} />
        ) : answer.isPending ? (
          <Skeleton label="Loading what needs attention" rows={2} />
        ) : (
          'Nothing in Jobs needs attention.'
        )
      }
    >
      {items.map((item) => (
        <CardRow
          key={item.id}
          tone={item.severity === 'critical' ? 'fail' : 'proc'}
          name={<Link to={attentionPath(item)}>{item.title}</Link>}
          value={item.lastSeenAt ? <Time value={item.lastSeenAt} short /> : undefined}
        />
      ))}
    </Card>
  )
}
