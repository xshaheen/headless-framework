import { DashboardApiError, type ApiRequest } from '@headless/dashboard-sdk/data'
import { renderModule, type RenderModuleOptions } from '@headless/dashboard-sdk/testing'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type { SequenceCounter, SequencePolicy, SequencesSummary } from '../src/api.ts'
import sequences from '../src/module.ts'

export const now = new Date('2026-11-27T10:00:00Z')

export const invoicePolicy: SequencePolicy = {
  start: 1,
  step: 1,
  mode: 'GapFree',
  blockSize: 1,
  reset: 'Year',
  format: 'INV-{yyyy}-{seq:D6}',
  fiscalYearStartMonth: 1,
  timeZone: 'Africa/Cairo',
}

/** A counter as the server serves it; the id is built the way the server builds it. */
export function counter(overrides: Partial<SequenceCounter> & Pick<SequenceCounter, 'name'>): SequenceCounter {
  const tenantId = overrides.tenantId ?? 'acme'
  const partition = overrides.partition ?? ''
  const policy = overrides.policy ?? {
    ...invoicePolicy,
    mode: overrides.mode ?? 'Fast',
    reset: overrides.reset ?? 'Never',
    format: null,
  }

  return {
    id: [tenantId, overrides.name, partition].map(encodeURIComponent).join('/'),
    tenantId,
    isHost: tenantId === '',
    partition,
    value: 3,
    updatedAt: '2026-11-27T09:30:00Z',
    lastNumber: null,
    isCurrentPartition: true,
    nextResetAt: null,
    mode: policy.mode,
    reset: policy.reset,
    policySource: 'registered',
    policy,
    ...overrides,
  }
}

export const acmeInvoice = counter({
  name: 'invoice',
  partition: '2026',
  lastNumber: 'INV-2026-000003',
  nextResetAt: '2026-12-31T22:00:00Z',
  policy: invoicePolicy,
})

export const acmeInvoiceLastYear = counter({
  name: 'invoice',
  partition: '2025',
  value: 41,
  lastNumber: 'INV-2025-000041',
  isCurrentPartition: false,
  nextResetAt: '2026-12-31T22:00:00Z',
  policy: invoicePolicy,
})

export const hostTicket = counter({ tenantId: '', name: 'ticket', value: 12 })

export const globexOrder = counter({ tenantId: 'globex', name: 'order', partition: '2026-11', reset: 'Month' })

export function summaryOf(
  rows: readonly SequenceCounter[],
  overrides: Partial<SequencesSummary> = {},
): SequencesSummary {
  return {
    total: rows.length,
    fast: rows.filter((row) => row.mode === 'Fast').length,
    gapFree: rows.filter((row) => row.mode === 'GapFree').length,
    reported: rows.filter((row) => row.mode === 'Reported').length,
    capped: false,
    current: rows.filter((row) => row.isCurrentPartition).length,
    currentCapped: false,
    nextResetAt: '2026-11-30T22:00:00Z',
    ...overrides,
  }
}

export interface FakeServer {
  counters?: SequenceCounter[]
  summary?: SequencesSummary
  /** The page size of the counter list; every counter on one page by default. */
  pageSize?: number
  /** A status the module's reads fail with, as the server's problem answers. */
  failure?: { status: number; message: string }
  extra?: (request: ApiRequest) => unknown
}

function scoped(rows: readonly SequenceCounter[], query: Readonly<Record<string, string | readonly string[]>>) {
  const search = typeof query.q === 'string' ? query.q : undefined

  return rows.filter(
    (row) =>
      (query.scope !== 'host' || row.isHost) &&
      (query.scope !== 'tenant' || row.tenantId === query.tenant) &&
      (search === undefined || row.name.includes(search) || row.tenantId.includes(search)),
  )
}

/** A fake of the Sequences module's API, filtering the way the server does. */
export function server(fake: FakeServer) {
  return (request: ApiRequest) => {
    const answered = fake.extra?.(request)

    if (answered !== undefined) {
      return answered
    }

    const query = request.query ?? {}
    const rows = fake.counters ?? []

    if (fake.failure && request.path.startsWith('sequences/')) {
      throw new DashboardApiError(fake.failure.status, fake.failure.message)
    }

    if (request.path === 'sequences/summary') {
      return fake.summary ?? summaryOf(scoped(rows, query))
    }

    if (request.path === 'sequences/counters') {
      const matching = scoped(rows, query).filter(
        (row) => (!query.mode || row.mode === query.mode) && (query.current !== 'true' || row.isCurrentPartition),
      )
      const start = typeof query.after === 'string' ? Number(query.after) : 0
      const size = fake.pageSize ?? matching.length
      const end = start + size

      return { items: matching.slice(start, end), nextCursor: end < matching.length ? String(end) : null }
    }

    if (request.path === 'sequences/counter') {
      const found = rows.find((row) => row.id === query.id)

      if (!found) {
        throw new DashboardApiError(404, 'No counter with this id is among the newest 1,000 partitions of its name.')
      }

      return found
    }

    if (request.path === 'sequences/counter/partitions') {
      const found = rows.find((row) => row.id === query.id)

      return {
        items: rows
          .filter((row) => found && row.tenantId === found.tenantId && row.name === found.name)
          .sort((a, b) => b.partition.localeCompare(a.partition)),
        nextCursor: null,
      }
    }

    throw new DashboardApiError(404, `No fake for ${request.method} ${request.path}`)
  }
}

export function open(fake: FakeServer, options: RenderModuleOptions = {}) {
  return renderModule(sequences, { api: server(fake), ...options })
}

export function user() {
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
}
