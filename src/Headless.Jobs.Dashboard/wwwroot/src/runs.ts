import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { createCommandsApi, type CommandDefinition, type RowResult } from '@headless/dashboard-sdk/commands'
import type { DashboardStateRole } from '@headless/dashboard-sdk'
import { isNewerVersion, type DashboardApi, type DashboardListPage } from '@headless/dashboard-sdk/data'
import type { ListPageRequest, ListRange } from '@headless/dashboard-sdk/table'
import { useQuery } from '@headless/dashboard-sdk/query'
import { useNavigate } from '@headless/dashboard-sdk/router'
import { useState } from 'react'

export const runKinds = ['TimeJob', 'CronJobOccurrence'] as const

export type RunKind = (typeof runKinds)[number]

export const runStatuses = [
  'Idle',
  'Queued',
  'InProgress',
  'Succeeded',
  'DueDone',
  'Failed',
  'Cancelled',
  'Skipped',
] as const

export type RunStatus = (typeof runStatuses)[number]

/** A time job or a cron occurrence, as the runs list, the run page, and a live change's projection carry it. */
export interface Run {
  id: string
  kind: RunKind
  function: string
  contractVersion?: string
  status: RunStatus
  executionTime?: string
  createdAt: string
  executedAt?: string
  elapsedMilliseconds: number
  retryCount: number
  node?: string
  /** The outer exception's type, such as `System.TimeoutException`, when the stored detail names it. */
  exceptionType?: string
  /** The exception's message, read from the stored detail on the server. */
  exceptionMessage?: string
  /** The outer exception's frames and every inner exception under it, when the stored detail has them. */
  exceptionStack?: string
  failureFingerprint?: string
  skippedReason?: string
  progressPercent?: number
  progressMessage?: string
  /** When the handler last reported progress, so a value the stream stopped refreshing reads its age. */
  progressUpdatedAt?: string
  /** The retries the run may take after its first attempt. */
  retries: number
  /** The wait, in seconds, before each retry; the last one repeats for any retry past the list. */
  retryIntervals?: number[] | null
  correlationId?: string
  causationId?: string
  traceId?: string
  tenantId?: string
  description?: string
  cronJobId?: string
  parentId?: string
  businessKey?: string
  cancelRequested?: boolean
  version: string
}

export interface RunDetail {
  run: Run
  /** The chain steps that run after this one. */
  children: string[]
}

export type AttemptOutcome = 'Succeeded' | 'Failed' | 'Cancelled' | 'Lost' | 'Skipped' | 'Released'

/** One attempt of a run: the node that ran it, when, and how it ended. */
export interface Attempt {
  number: number
  node?: string
  startedAt?: string
  endedAt?: string
  outcome?: AttemptOutcome
  exceptionType?: string
  exceptionMessage?: string
  exceptionStack?: string
  failureFingerprint?: string
}

/** The runs list's filter, as the URL and an Operation's scope carry it. */
export interface RunFilter {
  kind: string
  function: string
  /** A known run status, or empty for every status; `readFilter` drops a name the server would not read as one. */
  status: RunStatus | ''
  cronJobId: string
  correlationId: string
  fingerprint: string
  /** The time range's name in the URL: `1h`, `24h`, `7d`, or empty for any time. */
  range: string
  /** The range's start, fixed when the operator chose it, so the list keeps one cache key. */
  from: string
}

// Typed as the whole filter, so a field added to RunFilter must be listed here, and so in filterNames.
const emptyFilter: RunFilter = {
  kind: '',
  function: '',
  status: '',
  cronJobId: '',
  correlationId: '',
  fingerprint: '',
  range: '',
  from: '',
}

export const filterNames = Object.keys(emptyFilter) as (keyof RunFilter)[]

export function readFilter(params: URLSearchParams): RunFilter {
  const filter = { ...emptyFilter }

  for (const name of filterNames) {
    if (name !== 'status') {
      filter[name] = params.get(name) ?? ''
    }
  }

  filter.status = readStatus(params.get('status') ?? '')

  return filter
}

/**
 * The run status `value` names, read case-insensitively as the server reads it, so `failed` is `Failed`; `''` for no
 * status or one no run has, which the list then reads as every status rather than a filter nothing can match.
 */
function readStatus(value: string): RunStatus | '' {
  return runStatuses.find((status) => status.toLowerCase() === value.toLowerCase()) ?? ''
}

export function isFiltered(filter: RunFilter) {
  return filterNames.some((name) => filter[name] !== '')
}

/**
 * What narrows the list besides its URL filter: the search text and a custom day range, which the operator types and
 * the server matches, so the list, its live matching, and an Operation over it read the same rows.
 */
export interface RunScope {
  search?: string
  range?: ListRange
}

export const rangeSpans: Readonly<Record<string, number>> = { '1h': 3_600_000, '24h': 86_400_000, '7d': 604_800_000 }

/** Whether `run` matches `filter` as the server reads it, so a live change can tell a row it left the filter. */
export function runMatches(run: Run, filter: RunFilter, scope: RunScope = {}) {
  return (
    searchMatches(run, scope.search) &&
    (!scope.range || (run.executionTime !== undefined && inRange(Date.parse(run.executionTime), scope.range))) &&
    (!filter.kind || run.kind === filter.kind) &&
    (!filter.function || run.function === filter.function) &&
    (!filter.status || run.status === filter.status) &&
    (!filter.cronJobId || run.cronJobId === filter.cronJobId) &&
    (!filter.correlationId || run.correlationId === filter.correlationId) &&
    (!filter.fingerprint || run.failureFingerprint === filter.fingerprint) &&
    (!filter.from || (run.executionTime !== undefined && Date.parse(run.executionTime) >= Date.parse(filter.from)))
  )
}

function inRange(at: number, range: ListRange) {
  return at >= Date.parse(range.start) && at < Date.parse(range.end)
}

/**
 * Whether the search text is in the run's function, id, business key, or description, ignoring case, as the server
 * reads it; an occurrence's description is its cron job's, which the server does not search.
 */
function searchMatches(run: Run, search: string | undefined) {
  if (!search) {
    return true
  }

  const text = search.toLowerCase()
  const fields = [run.function, run.id, run.businessKey, run.kind === 'TimeJob' ? run.description : undefined]

  return fields.some((field) => field?.toLowerCase().includes(text) === true)
}

/** The filter as an Operation's scope: the server freezes exactly the rows it matches at confirm. */
export function operationScope(filter: RunFilter, scope: RunScope = {}) {
  return {
    ...(filter.kind ? { kind: filter.kind } : {}),
    ...(filter.function ? { functions: [filter.function] } : {}),
    ...(filter.status ? { statuses: [filter.status] } : {}),
    ...(filter.cronJobId ? { cronJobId: filter.cronJobId } : {}),
    ...(filter.correlationId ? { correlationId: filter.correlationId } : {}),
    ...(filter.fingerprint ? { fingerprint: filter.fingerprint } : {}),
    ...(filter.from && !scope.range ? { from: filter.from } : {}),
    ...(scope.range ? { from: scope.range.start, before: scope.range.end } : {}),
    ...(scope.search ? { search: scope.search } : {}),
  }
}

/** What the filtered rows are, for the confirm and progress text: `failed runs of billing.charge`. */
export function runsNoun(filter: Pick<RunFilter, 'status' | 'function'>) {
  const status = filter.status ? `${statusLabel(filter.status as RunStatus).toLowerCase()} ` : ''

  return `${status}runs${filter.function ? ` of ${filter.function}` : ''}`
}

/** The state role each run status paints in. Due done shares ok with Succeeded; its label says it ran late. */
const runRoles: Readonly<Record<RunStatus, DashboardStateRole>> = {
  Idle: 'sched',
  Queued: 'enq',
  InProgress: 'proc',
  Succeeded: 'ok',
  DueDone: 'ok',
  Failed: 'fail',
  Cancelled: 'off',
  Skipped: 'off',
}

const labels: Partial<Record<RunStatus, string>> = { InProgress: 'In progress', DueDone: 'Succeeded late' }

export function statusLabel(status: RunStatus) {
  return labels[status] ?? status
}

const finished = new Set<RunStatus>(['Succeeded', 'DueDone', 'Failed', 'Cancelled', 'Skipped'])

export function isFinished(status: RunStatus) {
  return finished.has(status)
}

export function kindLabel(kind: RunKind) {
  return kind === 'TimeJob' ? 'Time job' : 'Occurrence'
}

/**
 * The mark a run shows. A command the server finished reads `Requeuing…`, `Cancelling…`, or `Deleting…` until the
 * stream moves the row's version; a stored cancel request reads `Cancelling…` until the job stops. Either keeps the
 * stored status's role, because the server has not confirmed a new one.
 */
export function runStatusMark(
  run: Pick<Run, 'status' | 'cancelRequested'>,
  pending?: string,
): { tone: DashboardStateRole; label: string } {
  const tone = runRoles[run.status] ?? 'off'

  if (pending) {
    return { tone, label: pending }
  }

  if (run.cancelRequested && !isFinished(run.status)) {
    return { tone, label: 'Cancelling…' }
  }

  return { tone, label: statusLabel(run.status) }
}

/**
 * The state role each attempt outcome paints in. A lost attempt died with its node, so it reads as failed; a released
 * one went back to the queue, so it reads as queued.
 */
export const attemptTones: Readonly<Record<AttemptOutcome, DashboardStateRole>> = {
  Succeeded: 'ok',
  Failed: 'fail',
  Cancelled: 'off',
  Lost: 'fail',
  Skipped: 'off',
  Released: 'enq',
}

export const runsTopic = 'jobs.run'

/** The fields of a live change's projection that decide which reads the change makes stale. */
export interface RunProjectionFacts {
  cronJobId?: string
  kind?: RunKind
  status?: RunStatus
  executionTime?: string
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function oneOf<T extends string>(values: readonly T[], value: unknown): T | undefined {
  return values.find((candidate) => candidate === value)
}

/**
 * The deciding fields of a change's projection, each kept only when it holds its served type; `undefined` when the
 * change carries no projection, which the stream sends when the row must be read again or was removed.
 */
export function readRunProjection(projection: unknown): RunProjectionFacts | undefined {
  if (!isRecord(projection)) {
    return undefined
  }

  const { cronJobId, kind, status, executionTime } = projection

  return {
    cronJobId: typeof cronJobId === 'string' ? cronJobId : undefined,
    kind: oneOf(runKinds, kind),
    status: oneOf(runStatuses, status),
    executionTime: typeof executionTime === 'string' ? executionTime : undefined,
  }
}

interface RunListAnswer {
  items: Run[]
  /** Sent with the first page only, the one read without a cursor. */
  count?: number
  countCapped?: boolean
  nextCursor?: string | null
}

const runsPageSize = 100

/**
 * One page of runs, newest first unless `request` names a sort: the first page, or the page after the run `cursor`
 * names. The server refuses a cursor read under another sort, search, or range, so the keyset list never sends one.
 * Only the first page carries the count; the keyset list reads it from there.
 */
export async function fetchRuns(
  api: DashboardApi,
  filter: Partial<RunFilter>,
  cursor?: string,
  request: Partial<Pick<ListPageRequest, 'sort' | 'search' | 'range'>> = {},
): Promise<DashboardListPage<Run>> {
  const query: Record<string, string> = { limit: String(runsPageSize) }

  // `range` lives only in the URL; the server filters on the `from` it fixed.
  for (const name of filterNames.filter((field) => field !== 'range')) {
    const value = filter[name]

    if (value) {
      query[name] = value
    }
  }

  if (request.sort) {
    query.sort = request.sort.key
    query.direction = request.sort.direction
  }

  if (request.search) {
    query.search = request.search
  }

  // A custom range replaces the preset's start, so the list never reads two starts at once.
  if (request.range) {
    query.from = request.range.start
    query.before = request.range.end
  }

  if (cursor) {
    query.after = cursor
  }

  const answer = await api.request<RunListAnswer>({ method: 'GET', path: 'jobs/runs', query })

  return {
    items: answer.items,
    ...(answer.count === undefined ? {} : { count: { value: answer.count, capped: answer.countCapped ?? false } }),
    nextCursor: answer.nextCursor ?? null,
  }
}

/**
 * One run with its chain children. The children sit beside the run's fields, so a live change's projection patches
 * the cached run in place and keeps them.
 */
export async function fetchRun(api: DashboardApi, id: string): Promise<Run & { children: string[] }> {
  const detail = await api.request<RunDetail>({ method: 'GET', path: `jobs/runs/${encodeURIComponent(id)}` })

  return { ...detail.run, children: detail.children }
}

export async function fetchAttempts(api: DashboardApi, id: string) {
  return (
    await api.request<{ items: Attempt[] }>({ method: 'GET', path: `jobs/runs/${encodeURIComponent(id)}/attempts` })
  ).items
}

export const viewOnlyReason = 'Needs operate permission'

/** The module's server commands as the server describes them to this user, with a viewer's commands disabled. */
export function useJobsCommands() {
  const { api } = useDashboard()
  const module = useModule()
  const commands = useQuery({
    queryKey: ['commands', module.id],
    queryFn: () => createCommandsApi(api, module.id).list(),
    staleTime: Infinity,
  })
  const all = commands.data ?? []

  function find(name: string): CommandDefinition | undefined {
    const command = all.find((candidate) => candidate.id === `${module.id}.${name}`)

    if (!command || module.canOperate) {
      return command
    }

    return { ...command, canRun: false, disabledReason: command.disabledReason ?? viewOnlyReason }
  }

  return {
    all,
    requeue: find('requeue'),
    cancel: find('cancel'),
    delete: find('delete'),
    runNow: find('run-now'),
    pause: find('pause'),
    resume: find('resume'),
    editCron: find('edit-cron'),
    edit: find('edit'),
    duplicate: find('duplicate'),
    forceDelete: find('force-delete'),
  }
}

/** The words a run reads while a finished command waits for its change: `Requeuing…`. */
export const pendingLabels: Readonly<Record<string, string>> = {
  requeue: 'Requeuing…',
  cancel: 'Cancelling…',
  delete: 'Deleting…',
  'force-delete': 'Deleting…',
}

/**
 * The runs a command finished on whose change has not arrived: each reads the command's pending label until its
 * version moves past the one it had when the operator ran it.
 */
export function usePending() {
  const [pending, setPending] = useState<ReadonlyMap<string, { version: string; label: string }>>(new Map())

  return {
    labelOf: (run: Pick<Run, 'id' | 'version'>) => {
      const entry = pending.get(run.id)

      return entry && !isNewerVersion(run.version, entry.version) ? entry.label : undefined
    },
    mark: (ids: readonly string[], versions: ReadonlyMap<string, string>, label: string) =>
      setPending((current) => {
        const next = new Map(current)

        for (const id of ids) {
          const version = versions.get(id)

          if (version !== undefined) {
            next.set(id, { version, label })
          }
        }

        return next
      }),
  }
}

export type Pending = ReturnType<typeof usePending>

/** A run action's `onDone`: the runs it finished on read its pending label until their change arrives. */
export function markDone(
  pending: Pending,
  versions: ReadonlyMap<string, string>,
  name: 'requeue' | 'cancel' | 'delete' | 'force-delete',
) {
  return (rows: readonly RowResult[]) =>
    pending.mark(
      rows.filter((row) => row.outcome === 'done').map((row) => row.id),
      versions,
      pendingLabels[name]!,
    )
}

/** Opens a path of this module, such as the runs list filtered by a correlation id. */
export function useModuleNavigate() {
  const navigate = useNavigate()
  const module = useModule()

  return (path: string) => void navigate(`/${module.id}/${path}`)
}

/** The runs list filtered by one correlation id: the trace until the cross-module trace page exists. */
export function tracePath(correlationId: string) {
  return `runs?correlationId=${encodeURIComponent(correlationId)}`
}
