import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { CommandAction, type RowResult } from '@headless/dashboard-sdk/commands'
import { Button, InspectorField, InspectorFields, Mono } from '@headless/dashboard-sdk/components'
import { useStreamEvent, useStreamStatus } from '@headless/dashboard-sdk/stream'
import { useState } from 'react'
import { exactTime, relativeTime, useRefusals, useRunCommands, type Run, type RunEvent } from './runs.ts'

/** A timestamp as relative time, with the exact time and zone on hover. */
export function RunTime({ value }: { value: string }) {
  const at = new Date(value)

  if (Number.isNaN(at.getTime())) {
    return <span>{value}</span>
  }

  return (
    <time dateTime={value} title={exactTime(at)}>
      {relativeTime(at, Date.now())}
    </time>
  )
}

/** The trace id, mono, with `Open trace` when the host configured a link to its tracing tool. */
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

/** A run's read-only fields, in the order the inspector and the run page show them. */
export function RunFields({ run, statusLabel }: { run: Run; statusLabel: string }) {
  return (
    <InspectorFields>
      <InspectorField label="Id">
        <Mono>{run.id}</Mono>
      </InspectorField>
      <InspectorField label="Status">{statusLabel}</InspectorField>
      <InspectorField label="Attempts">{run.attempts}</InspectorField>
      <InspectorField label="Last error">{run.lastError ? <Mono>{run.lastError}</Mono> : 'None'}</InspectorField>
      <InspectorField label="Trace id">
        <TraceId traceId={run.traceId} />
      </InspectorField>
      <InspectorField label="Created">
        <RunTime value={run.createdAt} />
      </InspectorField>
      <InspectorField label="Updated">
        <RunTime value={run.updatedAt} />
      </InspectorField>
    </InspectorFields>
  )
}

const eventLabels: Readonly<Record<string, string>> = {
  Created: 'Created',
  Started: 'Started',
  Succeeded: 'Succeeded',
  Failed: 'Failed',
  Requeued: 'Requeued',
  Note: 'Note',
}

/** The run's stored events, oldest first: created, each start, failure, requeue, and note. */
export function RunTimeline({ events }: { events: readonly RunEvent[] }) {
  return (
    <section aria-label="Timeline">
      <h3 className="hd-title">Timeline</h3>
      <ol className="hd-fields">
        {events.map((event, index) => (
          <li key={`${event.at}-${index}`}>
            <strong>{eventLabels[event.kind] ?? event.kind}</strong> <RunTime value={event.at} />
            {event.text ? (
              <>
                {' '}
                <span className="hd-hint">{event.text}</span>
              </>
            ) : null}
          </li>
        ))}
      </ol>
    </section>
  )
}

export interface RunActionsProps {
  runs: readonly Run[]
  /** Called with the runs a requeue finished on and the versions they had when the operator confirmed it. */
  onRequeued?: (ids: readonly string[], versions: ReadonlyMap<string, string>) => void
  onChanged?: (rows: RowResult[]) => void
}

/**
 * The actions the server allows now on `runs`: `Fail` and `Add note` on one run, `Requeue` on any number. Each
 * refused action renders disabled with the server's reason.
 */
export function RunActions({ runs, onRequeued, onChanged }: RunActionsProps) {
  const module = useModule()
  const { all, fail, requeue, addNote } = useRunCommands()
  const refusal = useRefusals(runs)
  const ids = runs.map((run) => run.id)
  // Captured at render, so the versions are the ones the operator saw when they confirmed.
  const versions = new Map(runs.map((run) => [run.id, run.version]))
  const noun = ids.length === 1 ? 'run' : 'runs'

  if (ids.length === 0) {
    return null
  }

  return (
    <div className="hd-actions">
      {requeue ? (
        <CommandAction
          module={module.id}
          command={requeue}
          ids={ids}
          noun={noun}
          variant="primary"
          commands={all}
          disabledReason={refusal(requeue.id)}
          onDone={(rows) => {
            onRequeued?.(
              rows.filter((row) => row.outcome === 'done').map((row) => row.id),
              versions,
            )
            onChanged?.(rows)
          }}
        />
      ) : null}
      {fail && ids.length === 1 ? (
        <CommandAction
          module={module.id}
          command={fail}
          ids={ids}
          noun={noun}
          commands={all}
          disabledReason={refusal(fail.id)}
          onDone={onChanged}
        />
      ) : null}
      {addNote && ids.length === 1 ? (
        <CommandAction
          module={module.id}
          command={addNote}
          ids={ids}
          noun={noun}
          commands={all}
          disabledReason={refusal(addNote.id)}
          onDone={onChanged}
        />
      ) : null}
    </div>
  )
}

/**
 * `Stale` while the stream is down or the server reports this module's changes stopped: what the view shows may be
 * out of date, and `Refresh` reads it again.
 */
export function StaleNotice({ onRefresh }: { onRefresh: () => void }) {
  const module = useModule()
  const { status } = useStreamStatus()
  const [moduleStale, setModuleStale] = useState(false)

  useStreamEvent('module.stale', (event) => {
    if (event.module === module.id) {
      setModuleStale(event.stale)
    }
  })

  if (status !== 'stale' && !moduleStale) {
    return null
  }

  return (
    <div className="hd-banner" role="status">
      <span>Stale. Live updates stopped, so these runs may be out of date.</span>
      <Button variant="ghost" onPress={onRefresh}>
        Refresh
      </Button>
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
