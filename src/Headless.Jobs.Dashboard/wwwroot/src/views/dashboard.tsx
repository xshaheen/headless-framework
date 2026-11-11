import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { CommandAction, useAllowedActions } from '@headless/dashboard-sdk/commands'
import {
  Button,
  Card,
  CardRow,
  Cards,
  LoadError,
  Mono,
  PageFrame,
  PageHeader,
  Skeleton,
  Text,
  usePageFrame,
  Timestamp,
} from '@headless/dashboard-sdk/components'
import { listKey, useDashboardKeysetList, useDashboardList, useNewRows } from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery, useQueryClient } from '@headless/dashboard-sdk/query'
import { AttentionIcon, PlayIcon, RecurringIcon } from '@headless/dashboard-sdk/icons'
import { Link, useNavigate } from '@headless/dashboard-sdk/router'
import { useStreamEvent } from '@headless/dashboard-sdk/stream'
import { useEffect, useRef } from 'react'
import { StatusDistribution, useRunsPerDay } from '../insight.tsx'
import { cronJobsTopic, fetchCronJobs, type CronJob } from '../cron.ts'
import {
  JobsStats,
  RunProgress,
  StaleNotice,
  useJobsAttention,
  useJobsCounts,
  useSettledRefetch,
  type AttentionItem,
} from '../parts.tsx'
import {
  fetchRuns,
  markDone,
  readFilter,
  readRunProjection,
  runsTopic,
  useJobsCommands,
  usePending,
  viewOnlyReason,
  type Run,
} from '../runs.ts'

const cardRows = 6

/** The in-progress runs, as the runs list filtered to In progress reads them, so both share one cached list. */
const processingFilter = { ...readFilter(new URLSearchParams()), status: 'InProgress' as const }

/** Every answer the dashboard reads: its lists, the strip's counts and figures, and the attention items. */
const dashboardKeys = [
  listKey(runsTopic),
  listKey(cronJobsTopic),
  ['jobs'],
  ['shell', 'counts', 'jobs'],
  ['shell', 'attention'],
]

export default function JobsDashboard() {
  const module = useModule()
  const navigate = useNavigate()
  const client = useQueryClient()
  const cronJobs = useNextRecurringRuns()
  const next = useNextExecution(cronJobs.next[0])
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
                Next execution <Mono>{next.function}</Mono>, <Timestamp value={next.at} scheduled />
              </>
            ) : undefined
          }
          actions={
            <Button
              variant="primary"
              disabledReason={module.canOperate ? null : viewOnlyReason}
              onPress={() => void navigate('/jobs/runs?create=time-job')}
            >
              Add time job
            </Button>
          }
        />
      }
    >
      <StaleNotice
        what="this dashboard"
        onRefresh={() => dashboardKeys.forEach((queryKey) => void client.invalidateQueries({ queryKey }))}
      />
      <Cards>
        <ProcessingNow />
        <NextRecurringRuns {...cronJobs} />
        <JobsAttention />
        <RunsByStatus />
        <RunsPerDay />
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
  const client = useQueryClient()
  const runs = useDashboardKeysetList<Run>({
    topic: runsTopic,
    params: processingFilter,
    fetchPage: (cursor) => fetchRuns(api, processingFilter, cursor),
  })
  const arrived = useNewRows<Run>(runsTopic, (run) => run.status === 'InProgress')
  const refetch = useRef<ReturnType<typeof setTimeout>>(undefined)

  // A run another cached list holds (All runs, Queued) is patched there rather than arriving as new, so it never
  // reaches this list: a change that moves a run into or out of In progress refetches the card, once per burst.
  // A progress tick of a listed run moves nothing, so it only patches.
  useStreamEvent('change', (change) => {
    const listed = runs.rows.some((run) => run.id === change.id)

    if (change.topic === runsTopic && change.projection && (change.projection.status === 'InProgress') !== listed) {
      clearTimeout(refetch.current)
      refetch.current = setTimeout(
        () => void client.invalidateQueries({ queryKey: listKey(runsTopic, processingFilter) }),
        250,
      )
    }
  })
  useEffect(() => () => clearTimeout(refetch.current), [])
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
      icon={PlayIcon}
      emptyIcon={PlayIcon}
      // The served count answers for the last fetch; while every running run is loaded, the shown rows are newer.
      caption={
        running.length > 0
          ? `${format.count(runs.hasMore && runs.count ? runs.count : { value: running.length, capped: false })} running`
          : undefined
      }
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
          title={run.node ? `${run.function} ${run.node}` : run.function}
          actions={
            cancel ? (
              <CommandAction
                module={module.id}
                command={cancel}
                ids={[run.id]}
                noun="run"
                commands={commands.all}
                disabledReason={refused.get(run.id) ?? null}
                onDone={markDone(pending, new Map([[run.id, run.version]]), 'cancel')}
              />
            ) : undefined
          }
        >
          {pending.labelOf(run) ??
            (run.cancelRequested ? (
              'Cancelling…'
            ) : run.progressPercent !== undefined ? (
              <RunProgress run={run} />
            ) : (
              'No progress reported'
            ))}
        </CardRow>
      ))}
    </Card>
  )
}

/**
 * The time job due soonest: the list read ascending from now, one row, among the runs still waiting to start. The
 * list endpoint reads its newest first unless told otherwise.
 */
function useNextTimeJob() {
  const { api } = useDashboard()
  const reread = useSettledRefetch(['jobs', 'next-time-job'])
  const next = useQuery({
    queryKey: ['jobs', 'next-time-job'],
    queryFn: async () => {
      const answer = await api.request<{ items: Run[] }>({
        method: 'GET',
        path: 'jobs/runs',
        query: {
          kind: 'TimeJob',
          status: ['Idle', 'Queued'],
          from: new Date().toISOString(),
          sort: 'executionTime',
          direction: 'ascending',
          limit: '1',
        },
      })

      return answer.items[0] ?? null
    },
  })

  // Read again when the shown job moves on, or a waiting time job is due sooner; a progress tick of another run is not.
  // A change with no projection says neither, so it is read again in case it is a time job due sooner. A burst of such
  // changes, such as a bulk requeue, reads once.
  useStreamEvent('change', (change) => {
    const shown = next.data
    const run = readRunProjection(change.projection)
    const sooner =
      !run ||
      (run.kind === 'TimeJob' &&
        (run.status === 'Idle' || run.status === 'Queued') &&
        run.executionTime !== undefined &&
        (!shown?.executionTime || Date.parse(run.executionTime) < Date.parse(shown.executionTime)))

    if (change.topic === runsTopic && (change.id === shown?.id || sooner)) {
      reread()
    }
  })

  return next
}

/** The next execution of either kind: the next cron fire or the next scheduled time job, whichever is sooner. */
function useNextExecution(cronJob: CronJob | undefined) {
  const timeJob = useNextTimeJob().data
  const fire = cronJob ? { at: cronJob.nextDueUtc, function: cronJob.function } : undefined
  const scheduled = timeJob?.executionTime ? { at: timeJob.executionTime, function: timeJob.function } : undefined

  if (!fire || !scheduled) {
    return fire ?? scheduled
  }

  return Date.parse(scheduled.at) < Date.parse(fire.at) ? scheduled : fire
}

/** The stored runs by status, from the counts the strip and the sidebar share. */
function RunsByStatus() {
  const counts = useJobsCounts()

  return (
    <Card
      title="Runs by status"
      empty={
        counts.isError ? (
          <LoadError message={counts.error.message} onRetry={() => void counts.refetch()} />
        ) : (
          <Skeleton label="Loading the runs by status" shape="chart" />
        )
      }
    >
      {counts.data ? (
        <CardRow>
          <Text tone="hint">All stored runs</Text>
          <StatusDistribution
            counts={counts.data.counts}
            stale={counts.data.stale.length > 0}
            fetchedAt={counts.dataUpdatedAt}
          />
        </CardRow>
      ) : null}
    </Card>
  )
}

/** Runs per day by status, the range control in the card's heading. */
function RunsPerDay() {
  const format = useFormat()
  const { control, body } = useRunsPerDay({ unit: 'runs', unitOne: 'run' })

  return (
    <Card title="Runs per day" action={control} wide>
      <CardRow>
        <Text tone="hint">{`By execution day, ${format.timeZone}`}</Text>
        {body}
      </CardRow>
    </Card>
  )
}

function useNextRecurringRuns() {
  const { api } = useDashboard()
  const list = useDashboardList<CronJob>({ topic: cronJobsTopic, fetch: () => fetchCronJobs(api) })
  const all = list.data?.items ?? []
  const next = all
    .filter((cronJob) => !cronJob.isPaused)
    .sort((left, right) => Date.parse(left.nextDueUtc) - Date.parse(right.nextDueUtc))

  return { list, total: all.length, next }
}

function NextRecurringRuns({ list, total, next }: ReturnType<typeof useNextRecurringRuns>) {
  const module = useModule()
  const navigate = useNavigate()

  return (
    <Card
      title="Next recurring runs"
      icon={RecurringIcon}
      emptyIcon={RecurringIcon}
      caption={next.length > 0 ? `${next.length} scheduled` : undefined}
      action={total > 0 ? <Link to="/jobs/cron-jobs">All</Link> : undefined}
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
            <Button
              disabledReason={module.canOperate ? null : viewOnlyReason}
              onPress={() => void navigate('/jobs/cron-jobs?create=cron-job')}
            >
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
          title={`${cronJob.function} ${cronJob.expression}`}
          value={<Timestamp value={cronJob.nextDueUtc} scheduled />}
        />
      ))}
    </Card>
  )
}

/** `37 failed runs of billing.charge`, as the server titles a failure group, split into its function and its count. */
const failedRunsTitle = /^(\d[\d,]*\+? failed runs?) of (\S+)$/

function attentionPath(item: AttentionItem) {
  const scope = item.recommended?.filter
  const fn = scope?.functions?.[0]

  if (fn && scope.fingerprint) {
    return `/jobs/runs?status=Failed&function=${encodeURIComponent(fn)}&fingerprint=${encodeURIComponent(scope.fingerprint)}`
  }

  return item.target?.kind === 'function'
    ? `/jobs/functions/${encodeURIComponent(item.target.id)}`
    : '/jobs/runs?status=Failed'
}

function JobsAttention() {
  const { answer, items } = useJobsAttention()

  return (
    <Card
      title="Needs attention"
      icon={AttentionIcon}
      emptyIcon={AttentionIcon}
      caption={items.length > 0 ? `${items.length} ${items.length === 1 ? 'item' : 'items'}` : undefined}
      action={items.length > 0 ? <Link to="/jobs/runs?status=Failed">All</Link> : undefined}
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
      {items.map((item) => {
        // The function is the identifier, so it leads the row in mono and the count follows as its fact; the link
        // still answers to the whole title. A title in another shape reads as the prose it is.
        const parts = failedRunsTitle.exec(item.title)

        return (
          <CardRow
            key={item.id}
            tone={item.severity === 'critical' ? 'fail' : 'proc'}
            prose={!parts}
            name={
              <Link to={attentionPath(item)} aria-label={item.title}>
                {parts ? <Mono>{parts[2]}</Mono> : item.title}
              </Link>
            }
            meta={parts ? parts[1] : undefined}
            title={item.title}
            value={item.lastSeenAt ? <Timestamp value={item.lastSeenAt} /> : undefined}
          />
        )
      })}
    </Card>
  )
}
