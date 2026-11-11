import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { CommandAction, createCommandsApi, useAllowedActions } from '@headless/dashboard-sdk/commands'
import {
  Button,
  DetailPanel,
  EmptyState,
  FieldGrid,
  Fields,
  GridField,
  HistoryList,
  LoadError,
  Mono,
  Skeleton,
  Text,
  Timestamp,
} from '@headless/dashboard-sdk/components'
import { useDashboardKeysetList, useDashboardObject } from '@headless/dashboard-sdk/data'
import { DeleteIcon, PauseIcon, PlayIcon, RecurringIcon, ResumeIcon } from '@headless/dashboard-sdk/icons'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery } from '@headless/dashboard-sdk/query'
import { Link } from '@headless/dashboard-sdk/router'
import { useStreamEvent } from '@headless/dashboard-sdk/stream'
import { useEffect, useState } from 'react'
import { cronJobsTopic, cronStatusMark, fetchCronJob, fetchPreview, type CronJob } from './cron.ts'
import { useRunsPerDay } from './insight.tsx'
import { firstLine, RunProgress, useSettledRefetch } from './parts.tsx'
import {
  fetchRuns,
  readRunProjection,
  runStatusMark,
  runsTopic,
  statusLabel,
  useJobsCommands,
  viewOnlyReason,
  type Run,
} from './runs.ts'

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
export function NextFires({
  fires,
  zone,
  paused,
}: {
  fires: readonly string[] | undefined
  zone: string | undefined
  paused: boolean
}) {
  const format = useFormat()
  const label = `Next fires, ${zone ?? 'scheduler zone'}`

  if (!fires && !paused) {
    return <Skeleton label="Loading the next fires" rows={5} shape="lines" />
  }

  return (
    <Fields label="Next fires">
      <Text as="h3" tone="label">
        {label}
      </Text>
      {paused || !fires?.length ? (
        <Text as="p" tone="hint" role="status">
          {paused ? 'No fires scheduled. Resume the cron job to schedule its next fires.' : 'No fires scheduled.'}
        </Text>
      ) : (
        <ol aria-label={label}>
          {fires.map((fire) => (
            <li key={fire}>
              <time dateTime={fire}>
                <Mono>{zone ? format.zonedTime(fire, zone) : format.exactTime(fire)}</Mono>
              </time>
            </li>
          ))}
        </ol>
      )}
    </Fields>
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
  const locked = cronJob.definedInCode
    ? 'Defined in code'
    : !module.canOperate || (editCron && !editCron.canRun)
      ? (editCron?.disabledReason ?? viewOnlyReason)
      : null

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
              <li key={fire}>
                <Mono>{format.zonedTime(fire, shown.timeZoneId)}</Mono>
              </li>
            ))}
          </ol>
        </span>
      )
    ) : undefined

  return (
    <>
      <FieldGrid label="Job" columns={2}>
        <GridField label="Function">
          <Link to={`/jobs/functions/${encodeURIComponent(cronJob.function)}`}>
            <Mono>{cronJob.function}</Mono>
          </Link>
        </GridField>
        <GridField label="Description">{cronJob.description || 'None'}</GridField>
        <GridField label="Overlap">{cronJob.onOverlap === 'Skip' ? 'Skip while running' : 'Allow'}</GridField>
        <GridField label="Retries">{format.number(cronJob.retries)}</GridField>
      </FieldGrid>
      <FieldGrid label="Schedule" columns={2}>
        {locked ? (
          <>
            <GridField label="Expression" action={<Text tone="hint">{locked}</Text>}>
              <Mono>{cronJob.expression}</Mono>
            </GridField>
            <GridField label="Time zone">
              {cronJob.timeZoneId || `${zone ?? 'Scheduler zone'} (scheduler zone)`}
            </GridField>
          </>
        ) : (
          <>
            <GridField
              label="Expression"
              edit={{
                value: cronJob.expression,
                onDraftChange: (expression) =>
                  setDraft({ expression, timeZoneId: draft?.timeZoneId ?? cronJob.timeZoneId }),
                preview: draftPreview,
                onCommit: (expression) => commit(expression, draft?.timeZoneId ?? cronJob.timeZoneId),
              }}
            />
            <GridField
              label="Time zone"
              edit={{
                // A cron job with no zone of its own fires in the scheduler's, so the field shows that one, not a blank.
                value: cronJob.timeZoneId || zone || '',
                onDraftChange: (timeZoneId) =>
                  setDraft({ expression: draft?.expression ?? cronJob.expression, timeZoneId }),
                onCommit: (timeZoneId) => commit(draft?.expression ?? cronJob.expression, timeZoneId),
              }}
            />
          </>
        )}
        <GridField label="Reads" span={2}>
          {cronJob.reading ?? cronJob.validationMessage ?? ''}
        </GridField>
        <GridField label="Next due" span={2}>
          {cronJob.isPaused ? 'Paused' : <Timestamp value={cronJob.nextDueUtc} zone={zone} scheduled />}
        </GridField>
      </FieldGrid>
    </>
  )
}

/** What a done delete says: the server's count of the stored occurrences that went with the cron job. */
export function deletedText(outcome: string) {
  return outcome ? `Deleted: ${outcome}` : 'Deleted'
}

/** How many occurrences the store holds for a cron job, which a delete removes with it: one row read for its count. */
function useStoredOccurrences(cronJobId: string, enabled: boolean) {
  const { api } = useDashboard()
  const reread = useSettledRefetch(['jobs', 'stored-occurrences', cronJobId])
  const answer = useQuery({
    queryKey: ['jobs', 'stored-occurrences', cronJobId],
    queryFn: async () => {
      const page = await api.request<{ count: number; countCapped: boolean }>({
        method: 'GET',
        path: 'jobs/runs',
        query: { cronJobId, limit: '1' },
      })

      return { value: page.count, capped: page.countCapped }
    },
    enabled,
  })

  // A new occurrence of this cron job changes the count the delete confirmation states. A change with no projection
  // names no cron job, and may be a removed occurrence, so it is read again too, once per burst.
  useStreamEvent('change', (change) => {
    const run = readRunProjection(change.projection)

    if (change.topic === runsTopic && (!run || run.cronJobId === cronJobId)) {
      reread()
    }
  })

  return answer.data
}

/**
 * The actions the server allows now on a cron job: `Run now`, which returns the new occurrence, `Pause` or `Resume`,
 * each the other's Undo, and `Delete`. A refused one renders disabled with the server's reason.
 */
export function CronActions({ cronJob, onDeleted }: { cronJob: CronJob; onDeleted?: (outcome: string) => void }) {
  const module = useModule()
  const format = useFormat()
  const commands = useJobsCommands()
  const refusal = useAllowedActions('cron-job', [cronJob])
  const [newRun, setNewRun] = useState<string>()
  const [deleted, setDeleted] = useState<string>()
  const toggle = cronJob.isPaused ? commands.resume : commands.pause
  const listed = commands.all.find((command) => command.id === `${module.id}.delete-cron`)
  // A viewer sees Delete disabled with the reason, as every other Jobs action reads for them.
  const remove =
    listed && !module.canOperate
      ? { ...listed, canRun: false, disabledReason: listed.disabledReason ?? viewOnlyReason }
      : listed
  const stored = useStoredOccurrences(cronJob.id, remove !== undefined)

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
          icon={PlayIcon}
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
          icon={cronJob.isPaused ? ResumeIcon : PauseIcon}
          appearance="icon"
          commands={commands.all}
          disabledReason={refusal(toggle.id)}
        />
      ) : null}
      {remove ? (
        <CommandAction
          module={module.id}
          command={remove}
          ids={[cronJob.id]}
          noun="cron job"
          variant="danger"
          icon={DeleteIcon}
          appearance="icon"
          commands={commands.all}
          disabledReason={refusal(remove.id)}
          reach={
            stored
              ? `${format.count(stored)} stored ${stored.value === 1 && !stored.capped ? 'occurrence goes' : 'occurrences go'} with it`
              : undefined
          }
          // The server says how many stored occurrences went with it; the cron job itself leaves the list on its change.
          onDone={(rows) => {
            const done = rows.find((row) => row.outcome === 'done')

            if (done) {
              setDeleted(done.reason ?? '')
              onDeleted?.(done.reason ?? '')
            }
          }}
        />
      ) : null}
      {deleted !== undefined ? (
        <Text role="status" tone="hint">
          {deletedText(deleted)}
        </Text>
      ) : null}
    </>
  )
}

function occurrenceNote(run: Run) {
  return run.skippedReason ?? firstLine(run.exceptionMessage) ?? run.node
}

/** The cron job's occurrences per day by status over a chosen range, as the server counts them. */
function CronRunsPerDay({ cronJob }: { cronJob: CronJob }) {
  const { control, body } = useRunsPerDay({ cronJobId: cronJob.id, unit: 'occurrences', unitOne: 'occurrence' })

  return (
    <Fields label="Occurrences per day">
      <Text as="h3" tone="label">
        Occurrences per day
      </Text>
      {control}
      {body}
    </Fields>
  )
}

/**
 * The cron job's occurrences due in the last 24 hours, newest first, each with its progress while it runs and a link to
 * its run.
 */
function CronOccurrences({ cronJob }: { cronJob: CronJob }) {
  const { api } = useDashboard()
  const [stableWindowStart] = useState(() => Date.now() - 24 * hour)
  const filter = { cronJobId: cronJob.id, from: new Date(stableWindowStart).toISOString() }
  const occurrences = useDashboardKeysetList<Run>({
    topic: runsTopic,
    params: filter,
    fetchPage: (cursor) => fetchRuns(api, filter, cursor),
  })
  const rows = occurrences.rows

  if (occurrences.query.isError) {
    return <LoadError message={occurrences.query.error.message} onRetry={() => void occurrences.query.refetch()} />
  }

  if (occurrences.query.isPending) {
    return <Skeleton label="Loading the occurrences" rows={3} shape="rows" />
  }

  return (
    <div>
      {rows.length === 0 ? (
        <EmptyState icon={RecurringIcon} title="No occurrences in the last 24 hours.">
          One appears each time the cron job fires.
        </EmptyState>
      ) : (
        <>
          <HistoryList
            label="Occurrences"
            items={rows.map((run) => ({
              key: run.id,
              time: <Timestamp value={run.executionTime ?? run.createdAt} />,
              tone: runStatusMark(run).tone,
              state: statusLabel(run.status),
              // Progress joins the note rather than replacing it, so the node, a skip reason, or a failure's first
              // line stays readable on an occurrence that also reported progress.
              note:
                run.progressPercent !== undefined ? (
                  <>
                    {occurrenceNote(run)} <RunProgress run={run} compact />
                  </>
                ) : (
                  occurrenceNote(run)
                ),
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
export function CronPanel({
  cronJob,
  fires,
  page,
  onDeleted,
}: {
  cronJob: CronJob
  fires: readonly string[] | undefined
  page?: boolean
  onDeleted?: (outcome: string) => void
}) {
  const zone = useCronZone(cronJob)

  return (
    <DetailPanel
      label={`Cron job ${cronJob.id}`}
      name={cronJob.function}
      id={cronJob.id}
      status={cronStatusMark(cronJob)}
      detachTo={page ? undefined : `/jobs/cron-jobs/${encodeURIComponent(cronJob.id)}`}
      actions={<CronActions cronJob={cronJob} onDeleted={onDeleted} />}
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
        {
          id: 'occurrences',
          label: 'Occurrences',
          content: (
            <>
              <div>
                <CronRunsPerDay cronJob={cronJob} />
              </div>
              <div>
                <CronOccurrences cronJob={cronJob} />
              </div>
            </>
          ),
        },
      ]}
    />
  )
}
