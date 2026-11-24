import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { createCommandsApi, type CommandDefinition, type RowResult } from '@headless/dashboard-sdk/commands'
import type { DashboardStateRole } from '@headless/dashboard-sdk'
import {
  isNewerVersion,
  listKey,
  objectKey,
  type DashboardApi,
  type DashboardListPage,
} from '@headless/dashboard-sdk/data'
import type { ListPageRequest, ListRange, ListSort } from '@headless/dashboard-sdk/table'
import { useQuery, useQueryClient, type QueryClient } from '@headless/dashboard-sdk/query'
import { useNavigate } from '@headless/dashboard-sdk/router'
import { useStreamEvent, useTopics } from '@headless/dashboard-sdk/stream'
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

/**
 * The runs list's filter, as an Operation's scope carries it. The URL carries every field but `correlationId`, which
 * rides in history state (`{ correlationId }`) so the id never reaches a URL.
 */
export interface RunFilter {
  kind: string
  function: string
  /** A known run status, or empty for every status; `readFilter` drops a name the server would not read as one. */
  status: RunStatus | ''
  cronJobId: string
  correlationId: string
  fingerprint: string
}

// Typed as the whole filter, so a field added to RunFilter must be listed here, and so in filterNames.
const emptyFilter: RunFilter = {
  kind: '',
  function: '',
  status: '',
  cronJobId: '',
  correlationId: '',
  fingerprint: '',
}

export const filterNames = Object.keys(emptyFilter) as (keyof RunFilter)[]

/**
 * The list's filter from its URL and its history state. A `correlationId` in the URL is ignored, so an old link that
 * put the id there opens the list unfiltered.
 */
export function readFilter(params: URLSearchParams, state?: unknown): RunFilter {
  const filter = { ...emptyFilter }

  for (const name of filterNames) {
    if (name !== 'status' && name !== 'correlationId') {
      filter[name] = params.get(name) ?? ''
    }
  }

  filter.status = readStatus(params.get('status') ?? '')
  filter.correlationId = readCorrelationState(state)

  return filter
}

/** The correlation id a link handed the runs list in its history state, or `''` for none. */
export function readCorrelationState(state: unknown) {
  return typeof state === 'object' &&
    state !== null &&
    'correlationId' in state &&
    typeof state.correlationId === 'string'
    ? state.correlationId
    : ''
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
 * What narrows the list besides its filter: the search text and the due-time range, which the server matches, so the
 * list, its live matching, and an Operation over it read the same rows.
 */
export interface RunScope {
  search?: string
  range?: ListRange
}

/** The search text in the URL (`?q=`). Jobs search reads function names, ids, business keys, and descriptions. */
export function readSearch(params: URLSearchParams) {
  return params.get('q')?.trim() ?? ''
}

export const runSortKeys = ['executionTime', 'function', 'status'] as const

const sortDirections = ['ascending', 'descending'] as const

/**
 * The sort in the URL, `?sort=function&direction=descending`, named as the server's list reads them; `undefined`, the
 * newest-first default, for a key no column sorts by. A missing or unknown direction reads ascending, the first one a
 * column's sort button asks for.
 */
export function readSort(params: URLSearchParams): ListSort | undefined {
  const key = oneOf(runSortKeys, params.get('sort'))

  return key ? { key, direction: oneOf(sortDirections, params.get('direction')) ?? 'ascending' } : undefined
}

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
    (!filter.fingerprint || run.failureFingerprint === filter.fingerprint)
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

/**
 * The topic on which the server announces that a deleted cron job's stored occurrences are gone: one change whose id
 * is the cron job's, whatever the number of occurrences, because a cron job can store any number of them.
 */
export const cronOccurrencesTopic = 'jobs.cron-occurrences'

interface CachedRunPages {
  pages: DashboardListPage<Run>[]
  pageParams: unknown[]
}

function isRunPage(value: unknown): value is DashboardListPage<Run> {
  return isRecord(value) && Array.isArray(value.items)
}

function isRunPages(value: unknown): value is CachedRunPages {
  return isRecord(value) && Array.isArray(value.pages) && value.pages.every(isRunPage)
}

/** A page without the occurrences of `cronJobId`, its exact count lowered by the rows it dropped. */
function withoutOccurrences(page: DashboardListPage<Run>, cronJobId: string): DashboardListPage<Run> {
  const items = page.items.filter((run) => run.cronJobId !== cronJobId)

  return items.length === page.items.length ? page : { ...page, items }
}

/**
 * Drops a deleted cron job's occurrences from every cached run list and run, in place. A list's count, on its first
 * page, drops by the rows it held; when more pages are left to load, rows it never held may have gone too, so that
 * list is read again for its count and its next page.
 */
export function dropCronOccurrences(client: QueryClient, cronJobId: string) {
  for (const [key, data] of client.getQueriesData({ queryKey: listKey(runsTopic) })) {
    if (isRunPage(data)) {
      const page = withoutOccurrences(data, cronJobId)
      const dropped = data.items.length - page.items.length

      if (dropped > 0) {
        client.setQueryData(key, lowerCount(page, dropped))
      }
    } else if (isRunPages(data)) {
      const pages = data.pages.map((page) => withoutOccurrences(page, cronJobId))
      const dropped = data.pages.reduce((sum, page, index) => sum + page.items.length - pages[index]!.items.length, 0)

      if (dropped === 0) {
        continue
      }

      const [first, ...rest] = pages
      client.setQueryData(key, { ...data, pages: first ? [lowerCount(first, dropped), ...rest] : pages })

      if (pages.at(-1)?.nextCursor) {
        void client.invalidateQueries({ queryKey: key, exact: true })
      }
    }
  }

  // An open run's page reads the run again and shows that it is gone. The key is the prefix every run's object key
  // starts with.
  void client.invalidateQueries({
    queryKey: objectKey(runsTopic, '').slice(0, 2),
    predicate: (query) => isRecord(query.state.data) && query.state.data.cronJobId === cronJobId,
  })
}

function lowerCount(page: DashboardListPage<Run>, dropped: number): DashboardListPage<Run> {
  // A capped count is a floor the server stopped at, so it stays.
  return page.count && !page.count.capped
    ? { ...page, count: { value: Math.max(0, page.count.value - dropped), capped: false } }
    : page
}

/**
 * Keeps the cached run lists free of a deleted cron job's occurrences while the calling view is mounted: the server
 * announces them as one change on {@link cronOccurrencesTopic} rather than one removal per run.
 */
export function useCronOccurrenceRemovals() {
  const client = useQueryClient()

  useTopics([cronOccurrencesTopic])
  // The shell's cache binding also files the change as a new row of this topic; no list reads that topic, so it never
  // shows.
  useStreamEvent('change', (change) => {
    if (change.topic === cronOccurrencesTopic) {
      dropCronOccurrences(client, change.id)
    }
  })
}

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

/** A filter a list reads runs by: the runs list's, or an open-ended due-time window, such as a cron job's last day. */
export interface RunReadFilter extends Partial<RunFilter> {
  /** The earliest due time read, as a UTC instant; a `range` in the request replaces it. */
  from?: string
}

/** The list's query as the search route's JSON body: the repeatable filters as arrays, and the limit as a number. */
function searchBody(query: Readonly<Record<string, string>>, correlationId: string) {
  const { limit, function: fn, status, ...rest } = query

  return {
    ...rest,
    correlationId,
    limit: Number(limit),
    ...(fn ? { function: [fn] } : {}),
    ...(status ? { status: [status] } : {}),
  }
}

/**
 * One page of runs, newest first unless `request` names a sort: the first page, or the page after the run `cursor`
 * names. The server refuses a cursor read under another sort, search, or range, so the keyset list never sends one.
 * Only the first page carries the count; the keyset list reads it from there.
 */
export async function fetchRuns(
  api: DashboardApi,
  filter: RunReadFilter,
  cursor?: string,
  request: Partial<Pick<ListPageRequest, 'sort' | 'search' | 'range'>> = {},
): Promise<DashboardListPage<Run>> {
  const query: Record<string, string> = { limit: String(runsPageSize) }

  for (const name of filterNames) {
    const value = filter[name]

    if (value && name !== 'correlationId') {
      query[name] = value
    }
  }

  if (filter.from) {
    query.from = filter.from
  }

  if (request.sort) {
    query.sort = request.sort.key
    query.direction = request.sort.direction
  }

  if (request.search) {
    query.search = request.search
  }

  if (request.range) {
    query.from = request.range.start
    query.before = request.range.end
  }

  if (cursor) {
    query.after = cursor
  }

  // A correlation id goes in the body of the search route, every page of it, so the id never enters a URL.
  const answer = filter.correlationId
    ? await api.request<RunListAnswer>({
        method: 'POST',
        path: 'jobs/runs/search',
        body: searchBody(query, filter.correlationId),
      })
    : await api.request<RunListAnswer>({ method: 'GET', path: 'jobs/runs', query })

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

/** Opens a path of this module, such as a run's own page. */
export function useModuleNavigate() {
  const navigate = useNavigate()
  const module = useModule()

  return (path: string) => void navigate(`/${module.id}/${path}`)
}
