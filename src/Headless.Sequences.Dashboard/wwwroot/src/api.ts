import { useDashboard } from '@headless/dashboard-sdk'
import { listKey, type DashboardApi, type DashboardListPage } from '@headless/dashboard-sdk/data'
import { useQuery, type QueryClient } from '@headless/dashboard-sdk/query'

export const sequenceModes = ['Fast', 'GapFree', 'Reported'] as const
export type SequenceMode = (typeof sequenceModes)[number]

export const sequenceResets = ['Never', 'Year', 'Month', 'Day', 'FiscalYear'] as const
export type SequenceReset = (typeof sequenceResets)[number]

export const scopes = ['host', 'tenant'] as const
export type Scope = (typeof scopes)[number]

export const policySources = ['registered', 'default'] as const
export type PolicySource = (typeof policySources)[number]

/** The stream topic the counter list keys its cache under; the module serves no changes, so nothing patches it. */
export const countersTopic = 'sequences.sequence-counter'

/** The topic an open panel's partition list keys its cache under. */
export const partitionsTopic = 'sequences.partition'

export interface SequencesSummary {
  total: number
  fast: number
  gapFree: number
  reported: number
  capped: boolean
  /** The counters in the partition their policy chooses now. */
  current: number
  currentCapped: boolean
  /** The earliest next reset across every policy, whatever the filters. */
  nextResetAt: string | null
}

export interface SequencePolicy {
  start: number
  step: number
  mode: SequenceMode
  blockSize: number
  reset: SequenceReset
  format: string | null
  fiscalYearStartMonth: number
  /** The time zone id the policy's dates and resets are decided in. */
  timeZone: string
}

export interface SequenceCounter {
  id: string
  /** Empty for the host scope. */
  tenantId: string
  isHost: boolean
  name: string
  /** Empty for a counter with no partition. */
  partition: string
  value: number
  updatedAt: string
  lastNumber: string | null
  isCurrentPartition: boolean
  nextResetAt: string | null
  mode: SequenceMode
  reset: SequenceReset
  policySource: PolicySource
  policy: SequencePolicy
}

/** The list's filters as the URL holds them; `tenant` is only ever set with the tenant scope. */
export interface CounterFilter {
  mode?: SequenceMode
  scope?: Scope
  tenant?: string
  current?: boolean
  q?: string
}

/** The server answered a shape this module does not read, so the view shows the read as failed. */
function shapeError(what: string) {
  return new Error(`The server answered an unexpected ${what}.`)
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function record(value: unknown, what: string): Record<string, unknown> {
  if (!isRecord(value)) {
    throw shapeError(what)
  }

  return value
}

function text(value: unknown, what: string): string {
  if (typeof value !== 'string') {
    throw shapeError(what)
  }

  return value
}

function optionalText(value: unknown, what: string): string | null {
  return value == null ? null : text(value, what)
}

function count(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw shapeError(what)
  }

  return value
}

function flag(value: unknown, what: string): boolean {
  if (typeof value !== 'boolean') {
    throw shapeError(what)
  }

  return value
}

function oneOf<const T extends readonly string[]>(values: T, value: unknown, what: string): T[number] {
  const found = values.find((candidate) => candidate === value)

  if (found === undefined) {
    throw shapeError(what)
  }

  return found
}

export function readSummary(value: unknown): SequencesSummary {
  const item = record(value, 'summary')

  return {
    total: count(item.total, 'total'),
    fast: count(item.fast, 'fast count'),
    gapFree: count(item.gapFree, 'gap-free count'),
    reported: count(item.reported, 'reported count'),
    capped: flag(item.capped, 'capped flag'),
    current: count(item.current, 'current count'),
    currentCapped: flag(item.currentCapped, 'current capped flag'),
    nextResetAt: optionalText(item.nextResetAt, 'next reset'),
  }
}

function readPolicy(value: unknown): SequencePolicy {
  const item = record(value, 'policy')

  return {
    start: count(item.start, 'start'),
    step: count(item.step, 'step'),
    mode: oneOf(sequenceModes, item.mode, 'mode'),
    blockSize: count(item.blockSize, 'block size'),
    reset: oneOf(sequenceResets, item.reset, 'reset'),
    format: optionalText(item.format, 'format'),
    fiscalYearStartMonth: count(item.fiscalYearStartMonth, 'fiscal-year start month'),
    timeZone: text(item.timeZone, 'time zone'),
  }
}

export function readCounter(value: unknown): SequenceCounter {
  const item = record(value, 'counter')

  return {
    id: text(item.id, 'counter id'),
    tenantId: text(item.tenantId, 'tenant id'),
    isHost: flag(item.isHost, 'host flag'),
    name: text(item.name, 'name'),
    partition: text(item.partition, 'partition'),
    value: count(item.value, 'value'),
    updatedAt: text(item.updatedAt, 'update time'),
    lastNumber: optionalText(item.lastNumber, 'last number'),
    isCurrentPartition: flag(item.isCurrentPartition, 'current partition flag'),
    nextResetAt: optionalText(item.nextResetAt, 'next reset'),
    mode: oneOf(sequenceModes, item.mode, 'mode'),
    reset: oneOf(sequenceResets, item.reset, 'reset'),
    policySource: oneOf(policySources, item.policySource, 'policy source'),
    policy: readPolicy(item.policy),
  }
}

export function readCounterPage(value: unknown): DashboardListPage<SequenceCounter> {
  const item = record(value, 'counter list')

  if (!Array.isArray(item.items)) {
    throw shapeError('counter list')
  }

  return { items: item.items.map(readCounter), nextCursor: optionalText(item.nextCursor, 'cursor') }
}

/** The filter's query values; `undefined` members are left out of the request. */
function filterQuery({ scope, tenant, q }: CounterFilter): Record<string, string> {
  const query: Record<string, string> = {}

  if (scope) {
    query.scope = scope
  }

  if (scope === 'tenant' && tenant) {
    query.tenant = tenant
  }

  if (q) {
    query.q = q
  }

  return query
}

/** The tenant scope with no submitted id names no tenant yet, so nothing is read for it. */
export function isReadable(filter: CounterFilter) {
  return filter.scope !== 'tenant' || Boolean(filter.tenant)
}

export const queryKeys = {
  summary: ['sequences', 'summary'],
  counter: (id: string) => ['sequences', 'counter', id],
} as const

/**
 * Reads every mounted query of the module again: the summary, the counter list, an open panel's partitions, and a
 * counter read by id. Invalidation refetches only enabled queries, so a read the view holds back stays unsent.
 */
export function refreshSequences(client: QueryClient) {
  void client.invalidateQueries({ queryKey: ['sequences'] })
  void client.invalidateQueries({ queryKey: listKey(countersTopic) })
  void client.invalidateQueries({ queryKey: listKey(partitionsTopic) })
}

/** The counts for the scope and search; the summary leaves the mode and current filters out, so the chips keep theirs. */
export function useSummary(filter: CounterFilter) {
  const { api } = useDashboard()
  const query = filterQuery(filter)

  return useQuery({
    queryKey: [...queryKeys.summary, query],
    queryFn: async () => readSummary(await api.request<unknown>({ method: 'GET', path: 'sequences/summary', query })),
    enabled: isReadable(filter),
  })
}

export async function fetchCounters(api: DashboardApi, filter: CounterFilter, cursor: string | undefined) {
  const query = filterQuery(filter)

  if (filter.mode) {
    query.mode = filter.mode
  }

  if (filter.current) {
    query.current = 'true'
  }

  if (cursor) {
    query.after = cursor
  }

  return readCounterPage(await api.request<unknown>({ method: 'GET', path: 'sequences/counters', query }))
}

export async function fetchPartitions(api: DashboardApi, id: string, cursor: string | undefined) {
  const query: Record<string, string> = { id }

  if (cursor) {
    query.before = cursor
  }

  return readCounterPage(await api.request<unknown>({ method: 'GET', path: 'sequences/counter/partitions', query }))
}

/** One counter by id, read only for a panel opened from a URL whose row the list does not hold. */
export function useCounter(id: string | undefined) {
  const { api } = useDashboard()

  return useQuery({
    queryKey: queryKeys.counter(id ?? ''),
    queryFn: async () =>
      readCounter(await api.request<unknown>({ method: 'GET', path: 'sequences/counter', query: { id: id ?? '' } })),
    enabled: id !== undefined,
    retry: false,
  })
}

/** The search parameter value as one of `values`, or `undefined` for none or a value the view does not serve. */
export function parseFilter<const T extends readonly string[]>(values: T, value: string | null): T[number] | undefined {
  return values.find((candidate) => candidate === value)
}

/** The list's filter from the page URL: a value the view does not serve reads as no filter. */
export function readFilter(params: URLSearchParams): CounterFilter {
  const scope = parseFilter(scopes, params.get('scope'))

  return {
    mode: parseFilter(sequenceModes, params.get('mode')),
    scope,
    tenant: scope === 'tenant' ? params.get('tenant') || undefined : undefined,
    current: params.get('current') === 'true' ? true : undefined,
    q: params.get('q') || undefined,
  }
}

export const modeLabels: Readonly<Record<SequenceMode, string>> = {
  Fast: 'Fast',
  GapFree: 'Gap-free',
  Reported: 'Reported',
}

export const resetLabels: Readonly<Record<SequenceReset, string>> = {
  Never: 'Never',
  Year: 'Year',
  Month: 'Month',
  Day: 'Day',
  FiscalYear: 'Fiscal year',
}
