import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { CommandAction, createCommandsApi, useAllowedActions } from '@headless/dashboard-sdk/commands'
import { BarChart, Button, DetailPanel, FieldGrid, GridField, HistoryList, Mono, type BarColumn } from '@headless/dashboard-sdk/components'
import { useDashboardKeysetList, useDashboardObject } from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery } from '@headless/dashboard-sdk/query'
import { Link } from '@headless/dashboard-sdk/router'
import { useStreamEvent } from '@headless/dashboard-sdk/stream'
import { useEffect, useState } from 'react'
import { cronJobsTopic, cronStatusMark, fetchCronJob, fetchPreview, type CronJob } from './cron.ts'
import { firstLine, LoadError, Skeleton, Time } from './parts.tsx'
import { fetchRuns, runStatusMark, runsTopic, statusLabel, useJobsCommands, viewOnlyReason, type Run, type RunStatus } from './runs.ts'

const hour = 3_600_000

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

/**
 * One cron job with its next fires. The list row carries no fires, and a live change carries none either, so the
 * cron job is read again whenever its change arrives.
 */
export function useCronJob(id: string) {
  const { api } = useDashboard()
  const cronJob = useDashboardObject({ topic: cronJobsTopic, id, fetch: () => fetchCronJob(api, id) })

  useStreamEvent('change', (change) => {
    if (change.topic === cronJobsTopic && change.id === id) {
      void cronJob.refetch()
    }
  })

  return cronJob
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

/** The next fires the server computed, in the zone the cron job fires in; `fires` is unset while they load. */
export function NextFires({ fires, zone, paused }: { fires: readonly string[] | undefined; zone: string | undefined; paused: boolean }) {
  const format = useFormat()
  const label = `Next fires, ${zone ?? 'scheduler zone'}`

  if (!fires && !paused) {
    return <Skeleton label="Loading the next fires" rows={2} />
  }

  return (
    <section aria-label="Next fires" className="hd-fields">
      <h3 className="hd-label">{label}</h3>
      {paused || !fires?.length ? (
        <p className="hd-hint" role="status">
          {paused ? 'No fires scheduled. Resume the cron job to schedule its next fires.' : 'No fires scheduled.'}
        </p>
      ) : (
        <ol aria-label={label}>
          {fires.map((fire) => (
            <li key={fire}>
              <time dateTime={fire}>{zone ? format.zonedTime(fire, zone) : format.exactTime(fire)}</time>
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}

/**
 * A cron job's fields. The expression and zone are editable in place: while the operator types, the server's plain
 * reading and next five fires show below; `Enter` or blur commits through `Edit schedule` only when the server reads
 * the expression as valid, and its message shows inline when it does not. A cron job defined in code keeps both
 * read-only, and so does every field the server offers no command to change.
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
    <FieldGrid columns={2}>
      <GridField label="Function">
        <Link to={`/jobs/functions/${encodeURIComponent(cronJob.function)}`}>
          <Mono>{cronJob.function}</Mono>
        </Link>
      </GridField>
      <GridField label="Description">{cronJob.description || 'None'}</GridField>
      {locked ? (
        <>
          <GridField label="Expression" action={<span className="hd-hint">{locked}</span>}>
            <Mono>{cronJob.expression}</Mono>
          </GridField>
          <GridField label="Time zone">{cronJob.timeZoneId || `${zone ?? 'Scheduler zone'} (scheduler zone)`}</GridField>
        </>
      ) : (
        <>
          <GridField
            label="Expression"
            edit={{
              value: cronJob.expression,
              onDraftChange: (expression) => setDraft({ expression, timeZoneId: draft?.timeZoneId ?? cronJob.timeZoneId }),
              preview: draftPreview,
              onCommit: (expression) => commit(expression, draft?.timeZoneId ?? cronJob.timeZoneId),
            }}
          />
          <GridField
            label="Time zone"
            edit={{
              // A cron job with no zone of its own fires in the scheduler's, so the field shows that one, not a blank.
              value: cronJob.timeZoneId || zone || '',
              onDraftChange: (timeZoneId) => setDraft({ expression: draft?.expression ?? cronJob.expression, timeZoneId }),
              onCommit: (timeZoneId) => commit(draft?.expression ?? cronJob.expression, timeZoneId),
            }}
          />
        </>
      )}
      <GridField label="Reads" span={2}>
        {cronJob.reading ?? cronJob.validationMessage ?? ''}
      </GridField>
      <GridField label="Overlap">{cronJob.onOverlap === 'Skip' ? 'Skip while running' : 'Allow'}</GridField>
      <GridField label="Retries">{format.number(cronJob.retries)}</GridField>
      <GridField label="Next due" span={2}>
        {cronJob.isPaused ? 'Paused' : <Time value={cronJob.nextDueUtc} zone={zone} />}
      </GridField>
    </FieldGrid>
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
    <>
      {newRun ? <Link to={`/jobs/runs/${encodeURIComponent(newRun)}`}>Open the new run</Link> : null}
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
        <CommandAction module={module.id} command={toggle} ids={[cronJob.id]} noun="cron job" commands={commands.all} disabledReason={refusal(toggle.id)} />
      ) : null}
    </>
  )
}

const outcomeSeries = [
  { name: 'Succeeded', tone: 'ok', statuses: ['Succeeded', 'DueDone'] },
  { name: 'Failed', tone: 'fail', statuses: ['Failed'] },
  { name: 'Cancelled or skipped', tone: 'off', statuses: ['Cancelled', 'Skipped'] },
] as const satisfies readonly { name: string; tone: string; statuses: readonly RunStatus[] }[]

/**
 * One column per hour from `start`, a whole hour, to the current one, oldest first, counting the finished occurrences
 * due in it by outcome.
 */
function hourlyOutcomes(runs: readonly Run[], start: number, locale: string, timeZone: string): BarColumn[] {
  const label = new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone })
  const columns = Array.from({ length: 24 }, (_, index) => ({ label: label.format(start + index * hour), values: outcomeSeries.map(() => 0) }))

  for (const run of runs) {
    const at = Date.parse(run.executionTime ?? run.createdAt)
    const column = columns[Math.floor((at - start) / hour)]
    const series = outcomeSeries.findIndex((item) => (item.statuses as readonly RunStatus[]).includes(run.status))

    if (column && series !== -1) {
      column.values[series]! += 1
    }
  }

  return columns
}

function occurrenceNote(run: Run) {
  return run.skippedReason ?? firstLine(run.exceptionMessage) ?? run.node
}

/**
 * The cron job's occurrences due in the last 24 hours: their outcomes per hour, then each one newest first with a
 * link to its run. The bars count the occurrences loaded; while more are left to load, the footnote says so.
 */
function CronOccurrences({ cronJob }: { cronJob: CronJob }) {
  const { api } = useDashboard()
  const format = useFormat()
  // The first bar's hour: the list loads from there, so the bars count every occurrence it shows, none from the part of
  // an hour before the first bar.
  const [stableWindowStart] = useState(() => (Math.floor(Date.now() / hour) - 23) * hour)
  const filter = { cronJobId: cronJob.id, from: new Date(stableWindowStart).toISOString() }
  const occurrences = useDashboardKeysetList<Run>({ topic: runsTopic, params: filter, fetchPage: (cursor) => fetchRuns(api, filter, cursor) })
  const rows = occurrences.rows

  if (occurrences.query.isError) {
    return <LoadError message={occurrences.query.error.message} onRetry={() => void occurrences.query.refetch()} />
  }

  if (occurrences.query.isPending) {
    return <Skeleton label="Loading the occurrences" rows={3} />
  }

  const total = occurrences.count

  return (
    <div>
      {rows.length === 0 ? (
        <p>No occurrences in the last 24 hours. One appears each time the cron job fires.</p>
      ) : (
        <>
          <section aria-label="Outcomes per hour, last 24 h" className="hd-fields">
            <h3 className="hd-label">Outcomes per hour, last 24 h</h3>
            <BarChart
              label="Occurrence outcomes per hour"
              series={outcomeSeries}
              columns={hourlyOutcomes(rows, stableWindowStart, format.locale, format.timeZone)}
              unit="occurrences"
            />
            {occurrences.hasMore && total ? (
              <p className="hd-footnote">{`The bars count the ${format.number(rows.length)} latest of ${format.count(total)} occurrences.`}</p>
            ) : null}
          </section>
          <HistoryList
            label="Occurrences"
            items={rows.map((run) => ({
              key: run.id,
              time: <Time value={run.executionTime ?? run.createdAt} short />,
              tone: runStatusMark(run).tone,
              state: statusLabel(run.status),
              note: occurrenceNote(run),
              actions: <Link to={`/jobs/runs/${encodeURIComponent(run.id)}`}>Open run</Link>,
            }))}
          />
          {occurrences.hasMore ? (
            <Button variant="ghost" isPending={occurrences.isLoadingMore} onPress={occurrences.loadMore}>
              Load more
            </Button>
          ) : null}
        </>
      )}
    </div>
  )
}

/** `fires` is unset while they load. */
export function CronPanel({ cronJob, fires, page }: { cronJob: CronJob; fires: readonly string[] | undefined; page?: boolean }) {
  const zone = useCronZone(cronJob)

  return (
    <DetailPanel
      label={`Cron job ${cronJob.id}`}
      name={cronJob.function}
      id={cronJob.id}
      status={cronStatusMark(cronJob)}
      detachTo={page ? undefined : `/jobs/cron-jobs/${encodeURIComponent(cronJob.id)}`}
      actions={<CronActions cronJob={cronJob} />}
      tabs={[
        {
          id: 'schedule',
          label: 'Schedule',
          content: (
            <>
              <div>
                <CronFields key={cronJob.id} cronJob={cronJob} />
              </div>
              <div>
                <NextFires fires={fires} zone={zone} paused={cronJob.isPaused} />
              </div>
            </>
          ),
        },
        { id: 'occurrences', label: 'Occurrences', content: <CronOccurrences cronJob={cronJob} /> },
      ]}
    />
  )
}
