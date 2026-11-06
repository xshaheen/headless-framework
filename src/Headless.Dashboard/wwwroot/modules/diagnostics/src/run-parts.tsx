import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { useDashboardObject } from '@headless/dashboard-sdk/data'
import { CommandAction, useAllowedActions } from '@headless/dashboard-sdk/commands'
import type { DashboardStateRole } from '@headless/dashboard-sdk'
import { Button, DetailPanel, FieldGrid, GridField, Mono, Timeline } from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useStreamEvent, useStreamStatus } from '@headless/dashboard-sdk/stream'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { fetchRun, runStatusMark, runsTopic, useRunCommands, type Run, type RunEvent } from './runs.ts'

export function RunTime({ value }: { value: string }) {
  const format = useFormat()
  const at = new Date(value)

  if (Number.isNaN(at.getTime())) {
    return <span>{value}</span>
  }

  return (
    <time dateTime={value} title={format.exactTime(at)}>
      {format.relativeTime(at)}
    </time>
  )
}

function TraceLink({ traceId }: { traceId: string }) {
  const { traceLinkTemplate } = useDashboard()

  return traceLinkTemplate ? (
    <a href={traceLinkTemplate.replace('{traceId}', encodeURIComponent(traceId))} target="_blank" rel="noreferrer">
      Open trace
    </a>
  ) : null
}

function RunFields({ run, statusLabel }: { run: Run; statusLabel: string }) {
  return (
    <FieldGrid columns={3}>
      <GridField label="Status">{statusLabel}</GridField>
      <GridField label="Attempts">{run.attempts}</GridField>
      <GridField label="Updated">
        <RunTime value={run.updatedAt} />
      </GridField>
      <GridField label="Last error" span={2}>
        {run.lastError ?? 'None'}
      </GridField>
      <GridField label="Created">
        <RunTime value={run.createdAt} />
      </GridField>
      <GridField label="Trace id" span={2} action={<TraceLink traceId={run.traceId} />}>
        <Mono>{run.traceId}</Mono>
      </GridField>
    </FieldGrid>
  )
}

export function RunPanel({
  run,
  requeuing,
  onRequeued,
  page,
  children,
  selectedEvent,
  onSelectEvent,
  eventDetail,
}: {
  run: Run
  requeuing: boolean
  onRequeued: RunActionsProps['onRequeued']
  page?: boolean
  /** What the run page adds under the fields, such as the payload. */
  children?: ReactNode
  /** The selected timeline event's id, and how the timeline selects one; the run page keeps it in its URL. */
  selectedEvent?: string
  onSelectEvent?: (id: string) => void
  /** What the selected event recorded, under the timeline. */
  eventDetail?: ReactNode
}) {
  const { api } = useDashboard()
  const module = useModule()
  const detail = useDashboardObject({ topic: runsTopic(module.id), id: run.id, fetch: () => fetchRun(api, module.id, run.id) })
  // The list row fills the fields at once from the cache; the detail adds the timeline when it answers.
  const status = runStatusMark(run.status, requeuing)
  const seenVersion = useRef(run.version)

  // A live change patches the row's fields but carries no timeline, so the timeline is read again.
  useEffect(() => {
    if (seenVersion.current !== run.version) {
      seenVersion.current = run.version
      void detail.refetch()
    }
  }, [run.version, detail])

  return (
    <DetailPanel
      label={`Run ${run.id}`}
      name="Run"
      id={run.id}
      status={status}
      // Keyed by run, so a command still busy or failed for one run does not show on the next one J/K opens.
      actions={<RunActions key={run.id} runs={[run]} onRequeued={onRequeued} />}
      detachTo={page ? undefined : `runs/${encodeURIComponent(run.id)}`}
    >
      <section aria-label="Fields">
        <RunFields run={run} statusLabel={status.label} />
        {children}
      </section>
      <div>
        {detail.data ? (
          <RunTimeline events={detail.data.timeline} selected={selectedEvent} onSelect={onSelectEvent} />
        ) : detail.isError ? (
          <LoadError message={detail.error.message} onRetry={() => void detail.refetch()} />
        ) : (
          <p role="status">Loading the timeline…</p>
        )}
        {eventDetail}
      </div>
    </DetailPanel>
  )
}

const eventTones: Readonly<Record<string, DashboardStateRole>> = {
  Started: 'proc',
  Succeeded: 'ok',
  Failed: 'fail',
}

/** The run stores its events only in order, so an event's id is its position. */
function eventIdOf(index: number) {
  return String(index)
}

export function RunTimeline({ events, selected, onSelect }: { events: readonly RunEvent[]; selected?: string; onSelect?: (id: string) => void }) {
  return (
    <section aria-label="Timeline">
      <h3 className="hd-label">Timeline</h3>
      <Timeline
        label="Run timeline"
        events={events.map((event, index) => ({ id: eventIdOf(index), label: event.kind, at: event.at, text: event.text, tone: eventTones[event.kind] }))}
        selectedId={selected}
        onSelect={onSelect}
      />
    </section>
  )
}

export interface RunActionsProps {
  runs: readonly Run[]
  /** Called with the runs a requeue finished on and the versions they had when the operator confirmed it. */
  onRequeued?: (ids: readonly string[], versions: ReadonlyMap<string, string>) => void
}

/**
 * The actions the server allows now on `runs`: `Fail` and `Add note` on one run, `Requeue` on any number. Each
 * refused action renders disabled with the server's reason.
 */
export function RunActions({ runs, onRequeued }: RunActionsProps) {
  const module = useModule()
  const { all, fail, requeue, addNote } = useRunCommands()
  const refusal = useAllowedActions('run', runs)
  const ids = runs.map((run) => run.id)
  // Captured at render, so the versions are the ones the operator saw when they confirmed.
  const versions = new Map(runs.map((run) => [run.id, run.version]))
  const noun = ids.length === 1 ? 'run' : 'runs'

  if (ids.length === 0) {
    return null
  }

  return (
    <>
      {requeue ? (
        <CommandAction
          module={module.id}
          command={requeue}
          ids={ids}
          noun={noun}
          variant="primary"
          recovery
          commands={all}
          disabledReason={refusal(requeue.id)}
          onDone={(rows) =>
            onRequeued?.(
              rows.filter((row) => row.outcome === 'done').map((row) => row.id),
              versions,
            )
          }
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
        />
      ) : null}
    </>
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
