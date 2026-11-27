import { DashboardApiError, type ApiRequest } from '@headless/dashboard-sdk/data'
import { renderModule, type RenderModuleOptions } from '@headless/dashboard-sdk/testing'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type { FencedCounts, FencedLease, HeldLock, LeasesSummary } from '../src/api.ts'
import leases from '../src/module.ts'

export const now = new Date('2026-11-27T10:00:00Z')

export function lease(overrides: Partial<FencedLease> & Pick<FencedLease, 'resource'>): FencedLease {
  const tenantId = overrides.tenantId === undefined ? 'acme' : overrides.tenantId
  const kind = overrides.kind ?? 'ledger-export'

  return {
    id: `${encodeURIComponent(tenantId ?? '')}/${encodeURIComponent(kind)}/${encodeURIComponent(overrides.resource)}`,
    tenantId,
    kind,
    generation: 7,
    status: 'Current',
    state: 'Live',
    holder: 'orders-api-0@14',
    takeoverCount: 0,
    grantedAt: '2026-11-27T09:58:00Z',
    expiresAt: '2026-11-27T10:02:00Z',
    endedAt: null,
    progressContract: null,
    progressSizeBytes: null,
    ...overrides,
  }
}

export function lock(overrides: Partial<HeldLock> & Pick<HeldLock, 'resource'>): HeldLock {
  return { id: overrides.resource, leaseId: '9f1c2b', ttlMilliseconds: 60_000, ...overrides }
}

export function summary(overrides: Partial<LeasesSummary> = {}): LeasesSummary {
  return { fencing: { registered: true }, locks: { registered: true }, ...overrides }
}

/** Counts as the server derives them from `items`, unless the test gives its own. */
export function counts(items: readonly FencedLease[]): FencedCounts {
  const of = (state: FencedLease['state']) => ({
    value: items.filter((item) => item.state === state).length,
    capped: false,
  })

  return { live: of('Live'), expired: of('Expired'), ended: of('Ended'), total: { value: items.length, capped: false } }
}

function wireCount(count: { value: number; capped: boolean }) {
  return { count: count.value, capped: count.capped }
}

export interface FakeServer {
  summary?: LeasesSummary
  leases?: FencedLease[]
  /** Overrides the counts derived from `leases`. */
  counts?: FencedCounts
  kinds?: string[]
  /** The next cursor of the first lease page, when a test pages. */
  leaseNext?: string | null
  /** Lease ids whose lookup answers 404, as for a purged lease. */
  goneLeases?: readonly string[]
  locks?: HeldLock[]
  lockTotal?: number
  goneLocks?: readonly string[]
  /** Answers a request before the defaults; return `undefined` to fall through. */
  extra?: (request: ApiRequest) => unknown
}

export const readAt = '2026-11-27T09:59:58Z'

/** A fake of the Leases module's API and the shell aggregate the sidebar reads. */
export function server(fake: FakeServer) {
  return (request: ApiRequest) => {
    const answered = fake.extra?.(request)

    if (answered !== undefined) {
      return answered
    }

    const query = request.query ?? {}
    const items = fake.leases ?? []

    if (request.path === 'counts') {
      return { counts: {}, stale: [] }
    }

    if (request.path === 'leases/summary') {
      return fake.summary ?? summary()
    }

    if (request.path === 'leases/fenced') {
      const filtered = items.filter(
        (item) =>
          (!query.state || item.state === query.state) &&
          (!query.kind || item.kind === query.kind) &&
          (!query.prefix || item.resource.startsWith(String(query.prefix))),
      )

      return { items: filtered, nextCursor: query.after ? null : (fake.leaseNext ?? null), readAt }
    }

    if (request.path === 'leases/fenced/counts') {
      const value = fake.counts ?? counts(items)

      return {
        live: wireCount(value.live),
        expired: wireCount(value.expired),
        ended: wireCount(value.ended),
        total: wireCount(value.total),
      }
    }

    if (request.path === 'leases/fenced/kinds') {
      return { items: fake.kinds ?? [...new Set(items.map((item) => item.kind))].sort() }
    }

    if (request.path === 'leases/fenced/lease') {
      const id = String(query.id)
      const found = items.find((item) => item.id === id)

      if (!found || fake.goneLeases?.includes(id)) {
        throw new DashboardApiError(404, 'Not found')
      }

      return { lease: found, readAt }
    }

    if (request.path === 'leases/locks') {
      const locks = fake.locks ?? []

      return { items: locks, nextCursor: null, total: fake.lockTotal ?? locks.length, readAt }
    }

    if (request.path === 'leases/locks/lock') {
      const resource = String(query.resource)
      const found = (fake.locks ?? []).find((item) => item.resource === resource)

      if (!found || fake.goneLocks?.includes(resource)) {
        throw new DashboardApiError(404, 'Not found')
      }

      return { lock: found, readAt }
    }

    throw new DashboardApiError(404, `No fake for ${request.method} ${request.path}`)
  }
}

export function open(fake: FakeServer, options: RenderModuleOptions = {}) {
  return renderModule(leases, { api: server(fake), ...options })
}

export function user() {
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
}
