import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { CommandAction, createCommandsApi, useAllowedActions } from '@headless/dashboard-sdk/commands'
import { InspectorField, InspectorFields, Mono } from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery } from '@headless/dashboard-sdk/query'
import { Link } from '@headless/dashboard-sdk/router'
import { useEffect, useState } from 'react'
import { fetchPreview, type CronJob } from './cron.ts'
import { Time } from './parts.tsx'
import { useJobsCommands, viewOnlyReason } from './runs.ts'

/** The zone a cron job fires in: its own, or the scheduler's as the server names it. */
export function useCronZone(cronJob: Pick<CronJob, 'expression' | 'timeZoneId'>) {
  const { api } = useDashboard()
  const preview = useQuery({
    queryKey: ['jobs', 'cron-preview', cronJob.expression, cronJob.timeZoneId],
    queryFn: () => fetchPreview(api, cronJob.expression, cronJob.timeZoneId),
    enabled: cronJob.timeZoneId === '',
  })

  return cronJob.timeZoneId || preview.data?.timeZoneId
}

/** `value` once it has stayed the same for `delayMs`, so a preview asks the server once per pause in typing. */
function useSettled(value: string, delayMs = 250) {
  const [settled, setSettled] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delayMs)

    return () => clearTimeout(timer)
  }, [value, delayMs])

  return settled
}

/** The next fires the server computed, in the zone the cron job fires in. */
export function NextFires({ fires, zone, paused }: { fires: readonly string[]; zone: string | undefined; paused: boolean }) {
  const format = useFormat()

  if (paused || fires.length === 0) {
    return (
      <p className="hd-hint" role="status">
        {paused ? 'No fires scheduled. Resume the cron job to schedule its next fires.' : 'No fires scheduled.'}
      </p>
    )
  }

  return (
    <ol aria-label={`Next fires, ${zone ?? 'scheduler zone'}`}>
      {fires.map((fire) => (
        <li key={fire}>
          <time dateTime={fire}>{zone ? format.zonedTime(fire, zone) : format.exactTime(fire)}</time>
        </li>
      ))}
    </ol>
  )
}

/**
 * A cron job's fields. The expression and zone are editable in place: while the operator types, the server's plain
 * reading and next five fires show below; `Enter` or blur commits through `Edit schedule` only when the server reads
 * the expression as valid, and its message shows inline when it does not. A cron job defined in code keeps both read-only.
 */
export function CronFields({ cronJob }: { cronJob: CronJob }) {
  const { api } = useDashboard()
  const module = useModule()
  const format = useFormat()
  const { editCron } = useJobsCommands()
  const zone = useCronZone(cronJob)
  const [draft, setDraft] = useState<{ expression: string; timeZoneId: string } | null>(null)
  const typed = useSettled(draft ? `${draft.expression}\n${draft.timeZoneId}` : '')
  const [typedExpression = '', typedZone = ''] = typed.split('\n')
  const preview = useQuery({
    queryKey: ['jobs', 'cron-preview', typedExpression, typedZone],
    queryFn: () => fetchPreview(api, typedExpression, typedZone),
    enabled: typedExpression.trim() !== '',
  })
  const locked = cronJob.definedInCode ? 'Defined in code' : !module.canOperate || (editCron && !editCron.canRun) ? (editCron?.disabledReason ?? viewOnlyReason) : null

  async function commit(expression: string, timeZoneId: string) {
    const checked = await fetchPreview(api, expression, timeZoneId)

    if (checked.validationMessage) {
      throw new Error(checked.validationMessage)
    }

    const [row] = await createCommandsApi(api, module.id).run(`${module.id}.edit-cron`, {
      ids: [cronJob.id],
      arguments: { expression, ...(timeZoneId ? { timeZoneId } : {}) },
    })

    if (row && row.outcome !== 'done') {
      throw new Error(row.reason ?? 'The server did not change the schedule.')
    }

    setDraft(null)
  }

  const shown = preview.data
  const draftPreview =
    draft && shown ? (
      shown.validationMessage ? (
        <span>{shown.validationMessage}</span>
      ) : (
        <span>
          {shown.reading}
          <ol aria-label={`Next fires, ${shown.timeZoneId}`}>
            {shown.nextFires.map((fire) => (
              <li key={fire}>{format.zonedTime(fire, shown.timeZoneId)}</li>
            ))}
          </ol>
        </span>
      )
    ) : undefined

  return (
    <InspectorFields>
      <InspectorField label="Function">
        <Link to={`/jobs/functions/${encodeURIComponent(cronJob.function)}`}>
          <Mono>{cronJob.function}</Mono>
        </Link>
      </InspectorField>
      {locked ? (
        <>
          <InspectorField label="Expression">
            <Mono>{cronJob.expression}</Mono> <span className="hd-hint">{locked}</span>
          </InspectorField>
          <InspectorField label="Time zone">{cronJob.timeZoneId || `${zone ?? 'Scheduler zone'} (scheduler zone)`}</InspectorField>
        </>
      ) : (
        <>
          <InspectorField
            label="Expression"
            edit={{
              value: cronJob.expression,
              onDraftChange: (expression) => setDraft({ expression, timeZoneId: draft?.timeZoneId ?? cronJob.timeZoneId }),
              preview: draftPreview,
              onCommit: (expression) => commit(expression, draft?.timeZoneId ?? cronJob.timeZoneId),
            }}
          />
          <InspectorField
            label="Time zone"
            edit={{
              value: cronJob.timeZoneId,
              onDraftChange: (timeZoneId) => setDraft({ expression: draft?.expression ?? cronJob.expression, timeZoneId }),
              onCommit: (timeZoneId) => commit(draft?.expression ?? cronJob.expression, timeZoneId),
            }}
          />
        </>
      )}
      <InspectorField label="Reads">{cronJob.reading ?? cronJob.validationMessage ?? ''}</InspectorField>
      <InspectorField label="Overlap">{cronJob.onOverlap === 'Skip' ? 'Skip while running' : 'Allow'}</InspectorField>
      <InspectorField label="Retries">{format.number(cronJob.retries)}</InspectorField>
      <InspectorField label="Next due">{cronJob.isPaused ? 'Paused' : <Time value={cronJob.nextDueUtc} zone={zone} />}</InspectorField>
    </InspectorFields>
  )
}

/**
 * The actions the server allows now on a cron job: `Run now`, which returns the new occurrence, and `Pause` or
 * `Resume`, each the other's Undo. A refused one renders disabled with the server's reason.
 */
export function CronActions({ cronJob }: { cronJob: CronJob }) {
  const module = useModule()
  const commands = useJobsCommands()
  const refusal = useAllowedActions('cron-job', [cronJob])
  const [newRun, setNewRun] = useState<string>()
  const toggle = cronJob.isPaused ? commands.resume : commands.pause

  return (
    <div className="hd-actions">
      {commands.runNow ? (
        <CommandAction
          module={module.id}
          command={commands.runNow}
          ids={[cronJob.id]}
          noun="cron job"
          variant="primary"
          commands={commands.all}
          disabledReason={refusal(commands.runNow.id)}
          onDone={(rows) => setNewRun(rows.find((row) => row.outcome === 'done')?.reason)}
        />
      ) : null}
      {toggle ? (
        <CommandAction
          module={module.id}
          command={toggle}
          ids={[cronJob.id]}
          noun="cron job"
          commands={commands.all}
          disabledReason={refusal(toggle.id)}
        />
      ) : null}
      {newRun ? <Link to={`/jobs/runs/${encodeURIComponent(newRun)}`}>Open the new run</Link> : null}
    </div>
  )
}
