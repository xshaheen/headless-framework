import { useDashboard, useModule, type DashboardStateRole } from '@headless/dashboard-sdk'
import { CommandAction, useAllowedActions, type CommandDefinition } from '@headless/dashboard-sdk/commands'
import {
  Button,
  DetailPanel,
  ExceptionBlock,
  FieldGrid,
  GridField,
  HistoryList,
  Mono,
  PayloadView,
  ProgressBar,
  StatCard,
  StatStrip,
  Timeline,
  useDelayed,
  useViewKeys,
  type HistoryItem,
  type TimelineEvent,
} from '@headless/dashboard-sdk/components'
import { DashboardApiError, useDashboardObject, type CappedCount } from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery } from '@headless/dashboard-sdk/query'
import { Link } from '@headless/dashboard-sdk/router'
import { useStreamEvent, useStreamStatus } from '@headless/dashboard-sdk/stream'
import { useState, type ReactNode } from 'react'
import { fetchFunctions } from './cron.ts'
import {
  attemptTones,
  fetchAttempts,
  fetchRun,
  formatDuration,
  isFinished,
  kindLabel,
  markDone,
  runStatusMark,
  runsTopic,
  statusLabel,
  tracePath,
  useJobsCommands,
  useModuleNavigate,
  type Attempt,
  type Pending,
  type Run,
  type RunStatus,
} from './runs.ts'

/**
 * A timestamp as relative time, with the exact time, zone, and offset on hover; in `zone` when one is named. `short`
 * reads `4 min. ago`, so a list column stays narrow enough for a phone.
 */
export function Time({ value, zone, short, missing = 'Not yet' }: { value: string | undefined; zone?: string; short?: boolean; missing?: string }) {
  const format = useFormat()

  if (!value) {
    return <span className="hd-hint">{missing}</span>
  }

  return (
    <time dateTime={value} title={zone ? format.zonedTime(value, zone) : format.exactTime(value)}>
      {format.relativeTime(value, short ? { style: 'short' } : undefined)}
    </time>
  )
}

/**
 * A list cell's content, with `title` giving the full text on hover. The table ends a cell's only child in an
 * ellipsis when the column is too narrow for it.
 */
export function CellText({ title, children }: { title?: string; children: ReactNode }) {
  return <span title={title}>{children}</span>
}

/** The skeleton of a list or page, shown only once its load has taken 150 ms. */
export function Skeleton({ label, rows = 3 }: { label: string; rows?: number }) {
  return (
    <div role="status" aria-busy="true" aria-label={label} className="hd-fields">
      {Array.from({ length: rows }, (_, index) => (
        <div key={index} className="hd-skeleton" />
      ))}
    </div>
  )
}

/** A failed load: the error and Retry, never an empty view. */
export function LoadError({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="hd-banner">
      <span role="alert" className="hd-error">
        {message}
      </span>
      <Button onPress={onRetry}>Retry</Button>
    </div>
  )
}

type LoadState<T> =
  | { isPending: true; isError: false }
  | { isPending: false; isError: true; error: Error; refetch: () => Promise<unknown> }
  | { isPending: false; isError: false; data: T }

/** A load's skeleton, once it has taken 150 ms, or its error and Retry, else what `children` draws from its data. */
export function QueryState<T>({
  query,
  label,
  rows,
  children,
}: {
  query: LoadState<T>
  label: string
  rows: number
  children: (data: T) => ReactNode
}) {
  const showLoading = useDelayed(query.isPending)

  if (query.isPending) {
    return showLoading ? <Skeleton label={label} rows={rows} /> : null
  }

  if (query.isError) {
    return <LoadError message={query.error.message} onRetry={() => void query.refetch()} />
  }

  return children(query.data)
}

/** The registered functions, one cached answer for the runs list, the add forms, and the Functions views. */
export function useFunctions() {
  const { api } = useDashboard()

  return useQuery({ queryKey: ['jobs', 'functions'], queryFn: () => fetchFunctions(api) })
}

/** `Stale` while the stream is down or the server reports the Jobs changes stopped; the data on screen is kept. */
export function StaleNotice({ what, onRefresh }: { what: string; onRefresh: () => void }) {
  const stale = useStale()

  if (!stale) {
    return null
  }

  return (
    <div className="hd-banner" role="status">
      <span>{`Stale. Live updates stopped, so ${what} may be out of date.`}</span>
      <Button variant="ghost" onPress={onRefresh}>
        Refresh
      </Button>
    </div>
  )
}

/** Whether what the view shows may be out of date: the stream is down, or this module's changes stopped. */
export function useStale() {
  const module = useModule()
  const { status } = useStreamStatus()
  const [moduleStale, setModuleStale] = useState(false)

  useStreamEvent('module.stale', (event) => {
    if (event.module === module.id) {
      setModuleStale(event.stale)
    }
  })

  return status === 'stale' || moduleStale
}

/**
 * A product's sidebar counts by count key, and the modules whose counts did not answer: the shell sidebar's cached
 * answer for `GET api/counts`, field for field. Whichever of the two fetches first fills the cache the other reads, so
 * a different shape here breaks the sidebar.
 */
interface ProductCounts {
  counts: Readonly<Record<string, CappedCount>>
  stale: readonly { id: string; displayName: string }[]
}

/**
 * The Jobs sidebar's counts. The query key and answer shape are the shell sidebar's own, so the strip and the sidebar
 * read one cached answer and the shell's debounced refetch on run changes updates both; no second request is made.
 */
export function useJobsCounts() {
  const { api } = useDashboard()

  return useQuery({
    queryKey: ['shell', 'counts', 'jobs'],
    queryFn: async (): Promise<ProductCounts> => {
      const answer = await api.request<{
        counts: Record<string, { count: number; capped: boolean }>
        stale: { id: string; displayName: string }[]
      }>({
        method: 'GET',
        path: 'counts',
        query: { product: 'jobs' },
      })

      return {
        counts: Object.fromEntries(
          Object.entries(answer.counts).map(([key, count]) => [key, { value: count.count, capped: count.capped }]),
        ),
        stale: answer.stale,
      }
    },
  })
}

export interface AttentionItem {
  id: string
  title: string
  severity: 'critical' | 'warning'
  count: number
  capped: boolean
  lastSeenAt?: string | null
  target?: { kind: string; id: string } | null
  /** The command that resolves a failure group, and the filter that scopes it to the group's runs. */
  recommended?: { filter: { functions?: string[]; fingerprint?: string } } | null
}

/**
 * The Jobs items of the shell's Attention aggregate. The query key and answer shape are the Attention pill's own, so
 * both read one cached answer and the pill's refetch on `attention.changed` updates these too.
 */
export function useJobsAttention() {
  const { api } = useDashboard()
  const answer = useQuery({
    queryKey: ['shell', 'attention'],
    queryFn: async () =>
      (await api.request<{ modules: { id: string; items: AttentionItem[] }[] }>({ method: 'GET', path: 'needs-you' })).modules,
  })

  return { answer, items: answer.data?.find((section) => section.id === 'jobs')?.items ?? [] }
}

interface Figure {
  id: string
  label: string
  value: number
  unit?: string | null
  comparison?: { label: string; value?: number | null } | null
}

const OVERVIEW_STALE_MS = 60_000

function useJobsFigures() {
  const { api } = useDashboard()

  return useQuery({
    queryKey: ['jobs', 'figures'],
    queryFn: async () =>
      (await api.request<{ modules: { id: string; items: Figure[] }[] }>({ method: 'GET', path: 'overview' })).modules.find(
        (section) => section.id === 'jobs',
      )?.items ?? [],
    staleTime: OVERVIEW_STALE_MS,
  })
}

/** Short card labels for the served figures: the strip has no room for the server's full sentence. */
const figureMeta: Readonly<Record<string, { label: string; tone?: DashboardStateRole }>> = {
  'jobs.overview.succeeded-share': { label: 'Succeeded, 24 h', tone: 'ok' },
  'jobs.overview.failed': { label: 'Failed, 24 h', tone: 'fail' },
  'jobs.overview.late': { label: 'Started late, 24 h' },
}

const stripStates: readonly { status: RunStatus; tone: 'sched' | 'enq' | 'proc' }[] = [
  { status: 'Idle', tone: 'sched' },
  { status: 'Queued', tone: 'enq' },
  { status: 'InProgress', tone: 'proc' },
]

/** A count still loading or failed reads as an en dash, never 0. */
export function JobsStats() {
  const format = useFormat()
  const counts = useJobsCounts()
  const figures = useJobsFigures()

  function figureValue(figure: Figure) {
    const value = format.number(figure.value, { maximumFractionDigits: 1 })

    return figure.unit ? `${value} ${figure.unit}` : value
  }

  function caption(figure: Figure) {
    const compared = figure.comparison

    if (!compared) {
      return undefined
    }

    if (compared.value == null) {
      return compared.label
    }

    const delta = figure.value - compared.value
    const sign = delta > 0 ? '+' : delta < 0 ? '−' : '±'
    // A share moves in percentage points; a count moves in runs.
    const unit = figure.unit === '%' ? ' pt' : ''

    return `${sign}${format.number(Math.abs(delta), { maximumFractionDigits: 1 })}${unit} vs ${compared.label}`
  }

  return (
    <StatStrip label="Jobs at a glance">
      {stripStates.map(({ status, tone }) => {
        const count = counts.data?.counts[status]

        return (
          <StatCard
            key={status}
            label={statusLabel(status)}
            tone={tone}
            value={count ? format.count(count) : '–'}
            to={`/jobs/runs?status=${status}`}
          />
        )
      })}
      {(figures.data ?? []).map((figure) => (
        <StatCard
          key={figure.id}
          label={figureMeta[figure.id]?.label ?? figure.label}
          tone={figureMeta[figure.id]?.tone}
          value={figureValue(figure)}
          caption={caption(figure)}
        />
      ))}
    </StatStrip>
  )
}

/** The message's first line, and the rest of it with the stored stack under it. */
function exceptionParts(message: string, stack?: string) {
  const [first = '', ...rest] = message.split('\n')

  return { message: first, stack: [...rest, ...(stack ? [stack] : [])].join('\n') }
}

/**
 * A stored exception as its block: the outer exception's type and message, then its frames and every inner exception.
 * A reason the store wrote itself has no type and no stack.
 */
export function RunException({ type, message, stack }: { type?: string; message: string; stack?: string }) {
  return <ExceptionBlock type={type} {...exceptionParts(message, stack)} />
}

function TraceLink({ traceId }: { traceId: string }) {
  const { traceLinkTemplate } = useDashboard()

  return traceLinkTemplate ? (
    <a href={traceLinkTemplate.replace('{traceId}', encodeURIComponent(traceId))} target="_blank" rel="noreferrer">
      Open trace
    </a>
  ) : null
}

function Optional({ label, value, span }: { label: string; value: ReactNode; span?: 2 }) {
  return value === undefined || value === null || value === '' ? null : (
    <GridField label={label} span={span}>
      {value}
    </GridField>
  )
}

/** `T` opens the runs that share the run's correlation id, while the run is open. */
export function useTraceKey(run: Pick<Run, 'correlationId'> | undefined) {
  const open = useModuleNavigate()
  const id = run?.correlationId

  useViewKeys({ trace: () => (id ? open(tracePath(id)) : undefined) }, id !== undefined)
}

export function RunFields({ run }: { run: Run }) {
  const format = useFormat()
  const open = useModuleNavigate()
  // The store stamps `executedAt` when a run ends, so a running run has none yet; it reads as running, not as unrun.
  const running = run.status === 'InProgress' ? 'Running' : undefined
  const correlationId = run.correlationId

  return (
    <>
      {run.exceptionMessage ? (
        <RunException type={run.exceptionType} message={run.exceptionMessage} stack={run.exceptionStack} />
      ) : null}
      <FieldGrid>
        <GridField label="Kind">{kindLabel(run.kind)}</GridField>
        <GridField label="Retries">{format.number(run.retryCount)}</GridField>
        <GridField label="Node">{run.node ? <Mono>{run.node}</Mono> : 'Not recorded'}</GridField>
        <Optional label="Tenant" value={run.tenantId ? <Mono>{run.tenantId}</Mono> : undefined} />
        <GridField label="Status" span={2}>
          {run.cancelRequested && !isFinished(run.status) ? `${statusLabel(run.status)}, cancel requested` : statusLabel(run.status)}
        </GridField>
        {/* A handler that does not observe its cancellation token finishes on its own terms; the run keeps the status
            it really ended with, and this says the cancel asked for was not honoured. */}
        {run.cancelRequested && isFinished(run.status) && run.status !== 'Cancelled' ? (
          <GridField label="Cancel request" span={2}>
            Not honoured: the handler kept running
          </GridField>
        ) : null}
        {run.status === 'DueDone' ? <GridField label="Note">Started late</GridField> : null}
        <GridField label={run.status === 'Failed' ? 'Failed at' : 'Executed'} span={2}>
          <Time value={run.executedAt} missing={running} />
        </GridField>
        <GridField label="Duration">{run.executedAt ? formatDuration(run.elapsedMilliseconds, format.number) : (running ?? 'Not run yet')}</GridField>
        <Optional
          label="Correlation"
          span={2}
          value={
            correlationId ? (
              <>
                <Mono>{correlationId}</Mono>
                <Button variant="ghost" onPress={() => open(tracePath(correlationId))}>
                  Trace
                </Button>
                {run.traceId ? <TraceLink traceId={run.traceId} /> : null}
              </>
            ) : undefined
          }
        />
        {run.progressPercent !== undefined ? (
          <GridField label="Progress" span={2}>
            <RunProgress run={run} />
          </GridField>
        ) : null}
        <GridField label="Due" span={2}>
          {/* A chain step has no due time of its own: it runs once its parent ends. */}
          <Time value={run.executionTime} missing={run.parentId ? 'After its chain parent' : undefined} />
        </GridField>
        <GridField label="Created" span={2}>
          <Time value={run.createdAt} />
        </GridField>
        <GridField label="Function" span={2}>
          <Link to={`/jobs/functions/${encodeURIComponent(run.function)}`}>
            <Mono>{run.function}</Mono>
          </Link>
        </GridField>
        <Optional label="Skipped because" span={2} value={run.skippedReason} />
        <Optional label="Trace id" span={2} value={run.traceId ? <Mono>{run.traceId}</Mono> : undefined} />
        <Optional label="Causation" span={2} value={run.causationId ? <Mono>{run.causationId}</Mono> : undefined} />
        <Optional label="Contract version" value={run.contractVersion ? <Mono>{run.contractVersion}</Mono> : undefined} />
        <Optional label="Business key" span={2} value={run.businessKey ? <Mono>{run.businessKey}</Mono> : undefined} />
        <Optional label="Description" span={2} value={run.description} />
        <Optional
          label="Cron job"
          span={2}
          value={run.cronJobId ? <Link to={`/jobs/cron-jobs/${encodeURIComponent(run.cronJobId)}`}><Mono>{run.cronJobId}</Mono></Link> : undefined}
        />
      </FieldGrid>
    </>
  )
}

export function RunProgress({ run }: { run: Pick<Run, 'function' | 'progressPercent' | 'progressMessage'> }) {
  const format = useFormat()
  const percent = run.progressPercent ?? 0

  return (
    <ProgressBar
      label={`Progress of ${run.function}`}
      value={percent}
      max={100}
      valueText={`${format.number(percent, { maximumFractionDigits: 1 })} %${run.progressMessage ? ` · ${run.progressMessage}` : ''}`}
    />
  )
}

/** A run's attempts, read again whenever the run's version moves, since a live change carries no attempts. */
export function useAttempts(run: Pick<Run, 'id' | 'version'>) {
  const { api } = useDashboard()

  return useQuery({ queryKey: ['jobs', 'attempts', run.id, run.version], queryFn: () => fetchAttempts(api, run.id) })
}

export function firstLine(text: string | undefined) {
  return text?.split('\n')[0]
}

export function attemptEvents(run: Pick<Run, 'createdAt'>, attempts: readonly Attempt[]): TimelineEvent[] {
  return [
    { id: 'created', label: 'Created', at: run.createdAt },
    ...attempts.map((attempt) => ({
      id: String(attempt.number),
      label: `Attempt ${attempt.number}: ${attempt.outcome ?? 'Running'}`,
      at: attempt.endedAt ?? attempt.startedAt ?? run.createdAt,
      actor: attempt.node,
      // The fingerprint is the failure's cause, the key Attention groups by, so each failed attempt names it.
      text:
        [firstLine(attempt.exceptionMessage), attempt.failureFingerprint && `fingerprint ${attempt.failureFingerprint}`]
          .filter(Boolean)
          .join(' · ') || undefined,
      tone: attempt.outcome ? attemptTones[attempt.outcome] : 'proc',
    })),
  ]
}

export function AttemptTimeline({ run }: { run: Pick<Run, 'id' | 'version' | 'createdAt'> }) {
  const attempts = useAttempts(run)

  return (
    <section aria-label="Timeline" className="hd-fields">
      <h3 className="hd-title">Timeline</h3>
      {attempts.data ? (
        <Timeline label="Run timeline" events={attemptEvents(run, attempts.data)} />
      ) : attempts.isError ? (
        <LoadError message={attempts.error.message} onRetry={() => void attempts.refetch()} />
      ) : (
        <p className="hd-hint" role="status">
          Loading the attempts…
        </p>
      )}
    </section>
  )
}

function attemptNote(attempt: Attempt) {
  return [
    `try ${attempt.number}`,
    attempt.node,
    firstLine(attempt.exceptionMessage),
    attempt.failureFingerprint && `fingerprint ${attempt.failureFingerprint}`,
  ]
    .filter(Boolean)
    .join(' · ')
}

/** The store keeps attempts, not each move between queued and idle, so only those show. */
export function RunHistory({ run }: { run: Pick<Run, 'id' | 'version' | 'createdAt'> }) {
  const attempts = useAttempts(run)

  if (!attempts.data) {
    return (
      <section aria-label="History">
        {attempts.isError ? (
          <LoadError message={attempts.error.message} onRetry={() => void attempts.refetch()} />
        ) : (
          <Skeleton label="Loading the history" />
        )}
      </section>
    )
  }

  const items: HistoryItem[] = [...attempts.data].reverse().map((attempt) => ({
    key: String(attempt.number),
    time: <Time value={attempt.endedAt ?? attempt.startedAt} short />,
    tone: attempt.outcome ? attemptTones[attempt.outcome] : 'proc',
    state: attempt.outcome ?? 'Running',
    note: attemptNote(attempt),
  }))

  return (
    <HistoryList
      label="History"
      items={[...items, { key: 'created', time: <Time value={run.createdAt} short />, tone: 'sched', state: 'Created' }]}
    />
  )
}

/**
 * The actions the server allows now on one run: `Requeue` (the recovery action `R` runs), `Cancel`, and `Delete`. A
 * refused action renders disabled with the server's reason, and a finished one marks the run pending until its change
 * arrives.
 */
export function RunActions({ run, pending }: { run: Run; pending: Pending }) {
  const module = useModule()
  const commands = useJobsCommands()
  const refusal = useAllowedActions('run', [run])
  // Captured at render, so the version is the one the operator saw when they ran the command.
  const versions = new Map([[run.id, run.version]])

  const action = (name: 'requeue' | 'cancel' | 'delete', command: CommandDefinition | undefined, variant?: 'primary' | 'danger') =>
    command ? (
      <CommandAction
        key={name}
        module={module.id}
        command={command}
        ids={[run.id]}
        noun="run"
        variant={variant}
        recovery={name === 'requeue'}
        commands={commands.all}
        disabledReason={refusal(command.id)}
        onDone={markDone(pending, versions, name)}
      />
    ) : null

  return (
    <>
      {action('requeue', commands.requeue, 'primary')}
      {action('cancel', commands.cancel)}
      {action('delete', commands.delete, 'danger')}
    </>
  )
}

interface PayloadAnswer {
  content: string
  contentType: string
  headers?: Readonly<Record<string, string>>
}

/** The run's arguments, read when their tab opens and already redacted by the server's formatter. */
export function RunPayload({ runId }: { runId: string }) {
  const { api } = useDashboard()
  const module = useModule()
  const payload = useQuery({
    queryKey: ['payload', module.id, 'run', runId],
    queryFn: () => api.request<PayloadAnswer>({ method: 'GET', path: 'payload', query: { module: module.id, kind: 'run', id: runId } }),
  })

  return (
    <section aria-label="Arguments">
      {payload.isPending ? (
        <Skeleton label="Loading the arguments" />
      ) : payload.isError ? (
        // The server answers 404 for a run that stored no arguments: that is the run's payload, not a failed load.
        payload.error instanceof DashboardApiError && payload.error.status === 404 ? (
          <p>This run has no payload.</p>
        ) : (
          <LoadError message={payload.error.message} onRetry={() => void payload.refetch()} />
        )
      ) : (
        <PayloadView content={payload.data.content} contentType={payload.data.contentType} />
      )}
    </section>
  )
}

function RunChain({ run }: { run: Pick<Run, 'id' | 'parentId'> }) {
  const { api } = useDashboard()
  const detail = useDashboardObject({ topic: runsTopic, id: run.id, fetch: () => fetchRun(api, run.id) })
  const step = (id: string) => (
    <Link key={id} to={`/jobs/runs/${encodeURIComponent(id)}`}>
      <Mono>{id}</Mono>
    </Link>
  )
  const children = detail.data?.children

  return (
    <FieldGrid columns={2}>
      <GridField label="Runs after">{run.parentId ? step(run.parentId) : 'Nothing: this run starts its chain'}</GridField>
      <GridField label="Steps after this run">
        {children ? (children.length > 0 ? children.map(step) : 'None') : detail.isError ? 'The chain did not load.' : 'Loading…'}
      </GridField>
    </FieldGrid>
  )
}

export function RunPanel({ run, pending, page }: { run: Run; pending: Pending; page?: boolean }) {
  useTraceKey(run)

  return (
    <DetailPanel
      label={`Run ${run.id}`}
      name={run.function}
      id={run.id}
      status={runStatusMark(run, pending.labelOf(run))}
      detachTo={page ? undefined : `/jobs/runs/${encodeURIComponent(run.id)}`}
      // Keyed by run, so a command still busy or failed for one run does not show on the next one J/K opens.
      actions={<RunActions key={run.id} run={run} pending={pending} />}
      tabs={[
        {
          id: 'overview',
          label: 'Overview',
          content: (
            <>
              <div>
                <RunFields run={run} />
              </div>
              <RunHistory run={run} />
            </>
          ),
        },
        { id: 'arguments', label: 'Arguments', content: <RunPayload runId={run.id} /> },
        { id: 'chain', label: 'Chain', content: <RunChain run={run} /> },
      ]}
    />
  )
}
