import { useDashboard, type DashboardStateRole } from '@headless/dashboard-sdk'
import type { CappedCount, DashboardApi, DashboardListPage, ListItem } from '@headless/dashboard-sdk/data'
import { useQuery } from '@headless/dashboard-sdk/query'

/** The state groups the server files a lease under, from its stored status; the browser never derives one. */
export const leaseStates = ['Live', 'Expired', 'Ended'] as const
export type LeaseState = (typeof leaseStates)[number]

/** The state filter of the list: a group, or `All`. */
export const stateFilters = ['All', ...leaseStates] as const
export type StateFilter = (typeof stateFilters)[number]

/** The stored statuses a lease snapshot carries. `Stale` names an old generation and never appears on a snapshot. */
export const leaseStatuses = ['Current', 'Expired', 'Settled', 'Released', 'Revoked', 'Abandoned'] as const
export type LeaseStatus = (typeof leaseStatuses)[number]

export const fencedTopic = 'leases.fenced-lease'
export const locksTopic = 'leases.lock'

export interface LeasesSummary {
  fencing: { registered: boolean } | null
  locks: { registered: boolean } | null
}

export interface FencedLease extends ListItem {
  id: string
  tenantId: string | null
  kind: string
  resource: string
  generation: number
  status: LeaseStatus
  state: LeaseState
  holder: string | null
  takeoverCount: number
  grantedAt: string
  expiresAt: string
  endedAt: string | null
  progressContract: string | null
  progressSizeBytes: number | null
}

export interface FencedLeasePage extends DashboardListPage<FencedLease> {
  readAt: string
}

export interface FencedCounts {
  live: CappedCount
  expired: CappedCount
  ended: CappedCount
  total: CappedCount
}

export interface HeldLock extends ListItem {
  id: string
  resource: string
  leaseId: string | null
  ttlMilliseconds: number | null
}

export interface HeldLockPage extends DashboardListPage<HeldLock> {
  total: number
  readAt: string
}

/** The list filter, as the URL holds it. `tenant` is sent only once it holds an id. */
export interface FencedFilter {
  state: StateFilter
  kind: string
  scope: 'any' | 'host' | 'tenant'
  tenant: string
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

/** A field the server writes as `null` when it has none; a missing field reads the same. */
function optionalText(value: unknown, what: string): string | null {
  return value == null ? null : text(value, what)
}

function number(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw shapeError(what)
  }

  return value
}

function optionalNumber(value: unknown, what: string): number | null {
  return value == null ? null : number(value, what)
}

function flag(value: unknown, what: string): boolean {
  if (typeof value !== 'boolean') {
    throw shapeError(what)
  }

  return value
}

function list<T>(value: unknown, what: string, read: (item: unknown) => T): T[] {
  if (!Array.isArray(value)) {
    throw shapeError(what)
  }

  return value.map(read)
}

function oneOf<const T extends readonly string[]>(values: T, value: unknown, what: string): T[number] {
  const found = values.find((candidate) => candidate === value)

  if (found === undefined) {
    throw shapeError(what)
  }

  return found
}

export function readSummary(value: unknown): LeasesSummary {
  const item = record(value, 'summary')
  const fencing = item.fencing == null ? null : record(item.fencing, 'fencing summary')
  const locks = item.locks == null ? null : record(item.locks, 'locks summary')

  return {
    fencing: fencing && { registered: flag(fencing.registered, 'fencing registration') },
    locks: locks && { registered: flag(locks.registered, 'lock registration') },
  }
}

export function readLease(value: unknown): FencedLease {
  const item = record(value, 'lease')

  return {
    id: text(item.id, 'lease id'),
    tenantId: optionalText(item.tenantId, 'tenant id'),
    kind: text(item.kind, 'lease kind'),
    resource: text(item.resource, 'lease resource'),
    generation: number(item.generation, 'generation'),
    status: oneOf(leaseStatuses, item.status, 'lease status'),
    state: oneOf(leaseStates, item.state, 'lease state'),
    holder: optionalText(item.holder, 'holder'),
    takeoverCount: number(item.takeoverCount, 'takeover count'),
    grantedAt: text(item.grantedAt, 'grant time'),
    expiresAt: text(item.expiresAt, 'expiry time'),
    endedAt: optionalText(item.endedAt, 'end time'),
    progressContract: optionalText(item.progressContract, 'progress contract'),
    progressSizeBytes: optionalNumber(item.progressSizeBytes, 'progress size'),
  }
}

export function readLeasePage(value: unknown): FencedLeasePage {
  const item = record(value, 'lease page')

  return {
    items: list(item.items, 'leases', readLease),
    nextCursor: optionalText(item.nextCursor, 'cursor'),
    readAt: text(item.readAt, 'read time'),
  }
}

function readCount(value: unknown, what: string): CappedCount {
  const item = record(value, what)

  return { value: number(item.count, what), capped: flag(item.capped, `${what} cap`) }
}

export function readCounts(value: unknown): FencedCounts {
  const item = record(value, 'lease counts')

  return {
    live: readCount(item.live, 'live count'),
    expired: readCount(item.expired, 'expired count'),
    ended: readCount(item.ended, 'ended count'),
    total: readCount(item.total, 'total count'),
  }
}

export function readLock(value: unknown): HeldLock {
  const item = record(value, 'lock')

  return {
    id: text(item.id, 'lock id'),
    resource: text(item.resource, 'lock resource'),
    leaseId: optionalText(item.leaseId, 'lock lease id'),
    ttlMilliseconds: optionalNumber(item.ttlMilliseconds, 'time to live'),
  }
}

export function readLockPage(value: unknown): HeldLockPage {
  const item = record(value, 'lock page')

  return {
    items: list(item.items, 'locks', readLock),
    nextCursor: optionalText(item.nextCursor, 'cursor'),
    total: number(item.total, 'lock total'),
    readAt: text(item.readAt, 'read time'),
  }
}

/**
 * The read time a page of the list carries. The keyset hook hands pages back as its own page type, so the fields this
 * module adds are read back with a check.
 */
export function pageReadAt(page: unknown): string | undefined {
  return isRecord(page) && typeof page.readAt === 'string' && page.readAt !== '' ? page.readAt : undefined
}

/** The provider's held-lock total a lock page carries. */
export function pageTotal(page: unknown): number | undefined {
  return isRecord(page) && typeof page.total === 'number' ? page.total : undefined
}

async function read<T>(api: DashboardApi, path: string, parse: (value: unknown) => T, query?: Record<string, string>) {
  return parse(await api.request<unknown>({ method: 'GET', path, query }))
}

export const queryKeys = {
  summary: ['leases', 'summary'],
  counts: ['leases', 'fenced', 'counts'],
  kinds: ['leases', 'fenced', 'kinds'],
  lease: (id: string) => ['leases', 'fenced', 'one', id],
  lock: (resource: string) => ['leases', 'locks', 'one', resource],
} as const

/** The query parameters a filter sends, shared by the list and its counts. `state` belongs to the list alone. */
export function filterQuery(filter: FencedFilter, prefix: string): Record<string, string> {
  const query: Record<string, string> = {}

  if (filter.kind) {
    query.kind = filter.kind
  }

  if (filter.scope === 'host') {
    query.scope = 'host'
  } else if (filter.scope === 'tenant' && filter.tenant) {
    query.tenant = filter.tenant
  }

  if (prefix) {
    query.prefix = prefix
  }

  return query
}

export function fetchLeases(api: DashboardApi, filter: FencedFilter, prefix: string, cursor: string | undefined) {
  const query = filterQuery(filter, prefix)

  if (filter.state !== 'All') {
    query.state = filter.state
  }

  if (cursor) {
    query.after = cursor
  }

  return api.request<unknown>({ method: 'GET', path: 'leases/fenced', query }).then(readLeasePage)
}

export function fetchLocks(api: DashboardApi, cursor: string | undefined) {
  return api
    .request<unknown>({ method: 'GET', path: 'leases/locks', query: cursor ? { after: cursor } : undefined })
    .then(readLockPage)
}

export function useSummary() {
  const { api } = useDashboard()

  return useQuery({ queryKey: queryKeys.summary, queryFn: () => read(api, 'leases/summary', readSummary) })
}

export function useCounts(filter: FencedFilter, prefix: string, enabled: boolean) {
  const { api } = useDashboard()
  const query = filterQuery(filter, prefix)

  return useQuery({
    queryKey: [...queryKeys.counts, query],
    queryFn: () => read(api, 'leases/fenced/counts', readCounts, query),
    enabled,
  })
}

export function useKinds(enabled: boolean) {
  const { api } = useDashboard()

  return useQuery({
    queryKey: queryKeys.kinds,
    queryFn: () =>
      read(api, 'leases/fenced/kinds', (value) =>
        list(record(value, 'kind list').items, 'kinds', (kind) => text(kind, 'kind')),
      ),
    enabled,
  })
}

export function useLease(id: string | undefined) {
  const { api } = useDashboard()

  return useQuery({
    queryKey: queryKeys.lease(id ?? ''),
    queryFn: () =>
      read(
        api,
        'leases/fenced/lease',
        (value) => {
          const item = record(value, 'lease detail')

          return { lease: readLease(item.lease), readAt: text(item.readAt, 'read time') }
        },
        { id: id ?? '' },
      ),
    enabled: id !== undefined,
    retry: false,
  })
}

export function useLock(resource: string | undefined) {
  const { api } = useDashboard()

  return useQuery({
    queryKey: queryKeys.lock(resource ?? ''),
    queryFn: () =>
      read(
        api,
        'leases/locks/lock',
        (value) => {
          const item = record(value, 'lock detail')

          return { lock: readLock(item.lock), readAt: text(item.readAt, 'read time') }
        },
        { resource: resource ?? '' },
      ),
    enabled: resource !== undefined,
    retry: false,
  })
}

const statusMarks: Readonly<Record<LeaseStatus, { tone: DashboardStateRole; label: string }>> = {
  Current: { tone: 'ok', label: 'Live' },
  Expired: { tone: 'fail', label: 'Expired' },
  Settled: { tone: 'ok', label: 'Settled' },
  Released: { tone: 'off', label: 'Released' },
  Revoked: { tone: 'off', label: 'Revoked' },
  Abandoned: { tone: 'proc', label: 'Abandoned' },
}

/** The stored status as a dot and its own word: `Settled` and `Revoked` both end a lease, and the label says which. */
export function leaseMark(status: LeaseStatus): { tone: DashboardStateRole; label: string } {
  return statusMarks[status]
}

/** The search parameter value as one of `values`, or `undefined` for none or a value the view does not serve. */
export function parseFilter<const T extends readonly string[]>(values: T, value: string | null): T[number] | undefined {
  return values.find((candidate) => candidate === value)
}

/** Reads the list filter from the URL. `?scope=host` and `?tenant=<id>` are exclusive; host wins if both arrive. */
export function readFilter(params: URLSearchParams): FencedFilter {
  const tenant = params.get('tenant') ?? ''
  const scope = params.get('scope') === 'host' ? 'host' : params.has('tenant') ? 'tenant' : 'any'

  return {
    state: parseFilter(stateFilters, params.get('state')) ?? 'All',
    kind: params.get('kind') ?? '',
    scope,
    tenant: scope === 'tenant' ? tenant : '',
  }
}
