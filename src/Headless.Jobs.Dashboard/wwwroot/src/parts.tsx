import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { CommandAction, useAllowedActions, type CommandDefinition } from '@headless/dashboard-sdk/commands'
import {
  Button,
  InspectorField,
  InspectorFields,
  Mono,
  PayloadView,
  ProgressBar,
  Timeline,
  useViewKeys,
  type TimelineEvent,
} from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery } from '@headless/dashboard-sdk/query'
import { Link } from '@headless/dashboard-sdk/router'
import { useStreamEvent, useStreamStatus } from '@headless/dashboard-sdk/stream'
import { useState, type ReactNode } from 'react'
import {
  attemptTones,
  fetchAttempts,
  formatDuration,
  isFinished,
  kindLabel,
  pendingLabels,
  statusLabel,
  tracePath,
  useJobsCommands,
  useModuleNavigate,
  type Attempt,
  type Pending,
  type Run,
} from './runs.ts'

/** The canvas sits beside the inspector, and the two stack once the screen is too narrow for both. */
export const layout = { display: 'flex', flexWrap: 'wrap', gap: '1rem', alignItems: 'start' } as const
export const canvas = { flex: '1 1 32rem', minInlineSize: 0, display: 'flex', flexDirection: 'column', gap: '0.75rem' } as const

/**
 * A timestamp as relative time, with the exact time, zone, and offset on hover; in `zone` when one is named. `short`
 * reads `4 min. ago`, so a list column stays narrow enough for a phone.
 */
export function Time({ value, zone, short }: { value: string | undefined; zone?: string; short?: boolean }) {
  const format = useFormat()

  if (!value) {
    return <span className="hd-hint">Not yet</span>
  }

  return (
    <time dateTime={value} title={zone ? format.zonedTime(value, zone) : format.exactTime(value)}>
      {format.relativeTime(value, short ? { style: 'short' } : undefined)}
    </time>
  )
}

const cellText = { minInlineSize: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const

/**
 * A list cell's content on one line. A column too narrow for it ends the text in an ellipsis, never cuts a word, and
 * `title` gives the full text on hover.
 */
export function CellText({ title, children }: { title?: string; children: ReactNode }) {
  return (
    <span title={title} style={cellText}>
      {children}
    </span>
  )
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

const stackText = { margin: 0, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' } as const

/**
 * An exception: its message's first line as text with `Copy`, and the rest of the message with the top frame one
 * disclosure away, as a left-to-right isolated mono block so a source path reads the same in any locale. `Copy` takes
 * the whole of both.
 */
export function ExceptionText({ message, frame }: { message: string; frame?: string }) {
  const [first, ...rest] = message.split('\n')
  const stack = [...rest, ...(frame ? [frame] : [])].join('\n')

  return (
    <span className="hd-fields">
      <span>
        {first}{' '}
        <Button
          variant="ghost"
          aria-label="Copy exception"
          onPress={() => void navigator.clipboard?.writeText(frame ? `${message}\n${frame}` : message)}
        >
          Copy
        </Button>
      </span>
      {stack ? (
        <details>
          <summary>Stack</summary>
          <pre className="hd-mono" dir="ltr" style={stackText}>
            {stack}
          </pre>
        </details>
      ) : null}
    </span>
  )
}

/** The correlation id with `Trace`, which opens the runs that share it. */
function Correlation({ id }: { id: string }) {
  const open = useModuleNavigate()

  return (
    <span>
      <Mono>{id}</Mono>{' '}
      <Button variant="ghost" onPress={() => open(tracePath(id))}>
        Trace
      </Button>
    </span>
  )
}

/** The W3C trace id with `Open trace`, when the host configured a link to its tracing tool. */
function TraceId({ traceId }: { traceId: string }) {
  const { traceLinkTemplate } = useDashboard()

  return (
    <span>
      <Mono>{traceId}</Mono>
      {traceLinkTemplate ? (
        <>
          {' '}
          <a href={traceLinkTemplate.replace('{traceId}', encodeURIComponent(traceId))} target="_blank" rel="noreferrer">
            Open trace
          </a>
        </>
      ) : null}
    </span>
  )
}

function Optional({ label, value }: { label: string; value: ReactNode }) {
  return value === undefined || value === null || value === '' ? null : <InspectorField label={label}>{value}</InspectorField>
}

/** `T` opens the runs that share the run's correlation id, while the run is the selection. */
export function useTraceKey(run: Pick<Run, 'correlationId'> | undefined) {
  const open = useModuleNavigate()
  const id = run?.correlationId

  useViewKeys({ trace: () => (id ? open(tracePath(id)) : undefined) }, id !== undefined)
}

/** A run's fields, in the order the inspector and the run page show them. */
export function RunFields({ run }: { run: Run }) {
  const format = useFormat()

  return (
    <InspectorFields>
      <InspectorField label="Kind">{kindLabel(run.kind)}</InspectorField>
      <InspectorField label="Function">
        <Link to={`/jobs/functions/${encodeURIComponent(run.function)}`}>
          <Mono>{run.function}</Mono>
        </Link>
      </InspectorField>
      <InspectorField label="Status">
        {run.cancelRequested && !isFinished(run.status) ? `${statusLabel(run.status)}, cancel requested` : statusLabel(run.status)}
      </InspectorField>
      {run.status === 'DueDone' ? <InspectorField label="Note">Started late</InspectorField> : null}
      <InspectorField label="Retries">{format.number(run.retryCount)}</InspectorField>
      <InspectorField label="Duration">{run.executedAt ? formatDuration(run.elapsedMilliseconds, format.number) : 'Not run yet'}</InspectorField>
      {run.progressPercent !== undefined ? (
        <InspectorField label="Progress">
          <ProgressBar
            label="Progress"
            value={run.progressPercent}
            max={100}
            valueText={`${format.number(run.progressPercent, { maximumFractionDigits: 1 })} %${run.progressMessage ? ` · ${run.progressMessage}` : ''}`}
          />
        </InspectorField>
      ) : null}
      <InspectorField label="Due">
        <Time value={run.executionTime} />
      </InspectorField>
      <InspectorField label="Created">
        <Time value={run.createdAt} />
      </InspectorField>
      <InspectorField label="Executed">
        <Time value={run.executedAt} />
      </InspectorField>
      <Optional label="Node" value={run.node ? <Mono>{run.node}</Mono> : undefined} />
      <Optional
        label="Exception"
        value={run.exceptionMessage ? <ExceptionText message={run.exceptionMessage} frame={run.exceptionFrame} /> : undefined}
      />
      <Optional label="Skipped because" value={run.skippedReason} />
      <Optional label="Contract version" value={run.contractVersion ? <Mono>{run.contractVersion}</Mono> : undefined} />
      <Optional label="Correlation" value={run.correlationId ? <Correlation id={run.correlationId} /> : undefined} />
      <Optional label="Causation" value={run.causationId ? <Mono>{run.causationId}</Mono> : undefined} />
      <Optional label="Trace id" value={run.traceId ? <TraceId traceId={run.traceId} /> : undefined} />
      <Optional label="Tenant" value={run.tenantId ? <Mono>{run.tenantId}</Mono> : undefined} />
      <Optional label="Business key" value={run.businessKey ? <Mono>{run.businessKey}</Mono> : undefined} />
      <Optional label="Description" value={run.description} />
      <Optional
        label="Cron job"
        value={run.cronJobId ? <Link to={`/jobs/cron-jobs/${encodeURIComponent(run.cronJobId)}`}><Mono>{run.cronJobId}</Mono></Link> : undefined}
      />
      <Optional
        label="Chain parent"
        value={run.parentId ? <Link to={`/jobs/runs/${encodeURIComponent(run.parentId)}`}><Mono>{run.parentId}</Mono></Link> : undefined}
      />
    </InspectorFields>
  )
}

/** A run's attempts, read again whenever the run's version moves, since a live change carries no attempts. */
export function useAttempts(run: Pick<Run, 'id' | 'version'>) {
  const { api } = useDashboard()

  return useQuery({ queryKey: ['jobs', 'attempts', run.id, run.version], queryFn: () => fetchAttempts(api, run.id) })
}

function firstLine(text: string | undefined) {
  return text?.split('\n')[0]
}

/** The run's timeline: created, then each attempt with its node, outcome, and exception, oldest first. */
export function attemptEvents(run: Pick<Run, 'createdAt'>, attempts: readonly Attempt[]): TimelineEvent[] {
  return [
    { id: 'created', label: 'Created', at: run.createdAt },
    ...attempts.map((attempt) => ({
      id: String(attempt.number),
      label: `Attempt ${attempt.number}: ${attempt.outcome ?? 'Running'}`,
      at: attempt.endedAt ?? attempt.startedAt ?? run.createdAt,
      actor: attempt.node,
      text: firstLine(attempt.exceptionMessage),
      tone: attempt.outcome ? attemptTones[attempt.outcome] : 'active',
    })),
  ]
}

export function AttemptTimeline({
  run,
  selected,
  onSelect,
}: {
  run: Pick<Run, 'id' | 'version' | 'createdAt'>
  selected?: string
  onSelect?: (id: string) => void
}) {
  const attempts = useAttempts(run)

  return (
    <section aria-label="Timeline" className="hd-fields">
      <h3 className="hd-title">Timeline</h3>
      {attempts.data ? (
        <Timeline label="Run timeline" events={attemptEvents(run, attempts.data)} selectedId={selected} onSelect={onSelect} />
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

/** One attempt's fields, as the run page's inspector shows the selected timeline event. */
export function AttemptFields({ attempt }: { attempt: Attempt }) {
  return (
    <InspectorFields>
      <InspectorField label="Outcome">{attempt.outcome ?? 'Running'}</InspectorField>
      <InspectorField label="Node">{attempt.node ? <Mono>{attempt.node}</Mono> : 'Not recorded'}</InspectorField>
      <InspectorField label="Started">
        <Time value={attempt.startedAt} />
      </InspectorField>
      <InspectorField label="Ended">
        <Time value={attempt.endedAt} />
      </InspectorField>
      <Optional
        label="Exception"
        value={attempt.exceptionMessage ? <ExceptionText message={attempt.exceptionMessage} frame={attempt.exceptionFrame} /> : undefined}
      />
      <Optional label="Fingerprint" value={attempt.failureFingerprint ? <Mono>{attempt.failureFingerprint}</Mono> : undefined} />
    </InspectorFields>
  )
}

/**
 * The actions the server allows now on `runs`: `Requeue` (the recovery action `R` runs), `Cancel`, and `Delete`. A
 * refused action renders disabled with the server's reason, and a finished one marks its rows pending until their
 * change arrives.
 */
export function RunActions({ runs, pending }: { runs: readonly Run[]; pending: Pending }) {
  const module = useModule()
  const commands = useJobsCommands()
  const refusal = useAllowedActions('run', runs)
  const ids = runs.map((run) => run.id)
  // Captured at render, so the versions are the ones the operator saw when they ran the command.
  const versions = new Map(runs.map((run) => [run.id, run.version]))
  const noun = ids.length === 1 ? 'run' : 'runs'

  if (ids.length === 0) {
    return null
  }

  const action = (name: 'requeue' | 'cancel' | 'delete', command: CommandDefinition | undefined, variant?: 'primary' | 'danger') =>
    command ? (
      <CommandAction
        key={name}
        module={module.id}
        command={command}
        ids={ids}
        noun={noun}
        variant={variant}
        recovery={name === 'requeue'}
        commands={commands.all}
        disabledReason={refusal(command.id)}
        onDone={(rows) =>
          pending.mark(
            rows.filter((row) => row.outcome === 'done').map((row) => row.id),
            versions,
            pendingLabels[name]!,
          )
        }
      />
    ) : null

  return (
    <div className="hd-actions">
      {action('requeue', commands.requeue, 'primary')}
      {action('cancel', commands.cancel)}
      {action('delete', commands.delete, 'danger')}
    </div>
  )
}

interface PayloadAnswer {
  content: string
  contentType: string
  headers?: Readonly<Record<string, string>>
}

/** The run's arguments, fetched only on request and already redacted by the server's formatter. */
export function RunPayload({ runId }: { runId: string }) {
  const { api } = useDashboard()
  const module = useModule()
  const [shown, setShown] = useState(false)
  const payload = useQuery({
    queryKey: ['payload', module.id, 'run', runId],
    queryFn: () => api.request<PayloadAnswer>({ method: 'GET', path: 'payload', query: { module: module.id, kind: 'run', id: runId } }),
    enabled: shown,
  })

  return (
    <section aria-label="Payload" className="hd-fields">
      <h3 className="hd-title">Payload</h3>
      {!shown ? (
        <Button onPress={() => setShown(true)}>Show payload</Button>
      ) : payload.isPending ? (
        <p role="status" className="hd-hint">
          Loading the payload…
        </p>
      ) : payload.isError ? (
        <LoadError message={payload.error.message} onRetry={() => void payload.refetch()} />
      ) : (
        <PayloadView content={payload.data.content} contentType={payload.data.contentType} />
      )}
    </section>
  )
}
