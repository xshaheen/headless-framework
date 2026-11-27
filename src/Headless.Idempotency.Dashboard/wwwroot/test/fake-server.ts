import { DashboardApiError, type ApiRequest } from '@headless/dashboard-sdk/data'
import { renderModule, type RenderModuleOptions } from '@headless/dashboard-sdk/testing'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type { LookupView, RecordView, StoreView } from '../src/api.ts'
import idempotency from '../src/module.ts'

export const now = new Date('2026-11-27T14:20:31Z')
export const key = 'order-4711-charge'
export const hash = '9c41e2'.padEnd(64, '0')

export const store: StoreView = {
  provider: 'Headless.Idempotency.PostgreSql',
  location: '"headless"."idempotency_records"',
  defaultRetentionSeconds: 86400,
  defaultLeaseSeconds: 120,
  minimumLeaseSeconds: 1,
  maximumLeaseSeconds: 86400,
  purgeIntervalSeconds: 3600,
  purgeBatchSize: 1000,
}

/** The cache provider: its key prefix, and no purge because its entries expire on their own. */
export const cacheStore: StoreView = {
  ...store,
  provider: 'Headless.Idempotency.Caching',
  location: 'headless:idempotency:',
  purgeIntervalSeconds: undefined,
}

const base: RecordView = {
  status: 'Completed',
  generation: 48211,
  takeoverCount: 0,
  fingerprint: { algorithm: 'v1', hash },
  isLeaseExpired: false,
  retentionUntil: '2026-11-28T14:02:13Z',
  observedAt: '2026-11-27T14:20:31Z',
}

export const records = {
  completed: { ...base, resultContract: 'orders.create/v1', resultSizeBytes: 812 },
  inFlight: {
    ...base,
    status: 'Pending',
    generation: 48302,
    leaseExpiresAt: '2026-11-27T14:22:00Z',
    recoveryPoint: 'payment-authorized',
    recoveryContract: 'payments.recovery/v1',
    recoveryStateSizeBytes: 96,
  },
  pastRetention: {
    ...base,
    status: 'Pending',
    generation: 48303,
    leaseExpiresAt: '2026-11-27T14:30:00Z',
    retentionUntil: '2026-11-27T14:20:00Z',
  },
  stalled: {
    ...base,
    status: 'Released',
    generation: 48117,
    takeoverCount: 1,
    leaseExpiresAt: '2026-11-27T14:14:31Z',
    isLeaseExpired: true,
  },
  released: { ...base, status: 'Released', generation: 48118 },
  notAdmitted: { ...base, status: 'Released', generation: undefined },
} satisfies Record<string, RecordView>

export function found(record: RecordView, tenantId?: string): LookupView {
  return { tenantId, found: true, record }
}

export function notFound(tenantId?: string): LookupView {
  return { tenantId, found: false }
}

export interface FakeServer {
  store?: StoreView
  /**
   * The answers to successive lookups; the last one repeats. An `Error` answers as a failure, and a promise holds the
   * answer until the test settles it (see `held`).
   */
  lookups?: (LookupView | DashboardApiError | Promise<LookupView>)[]
}

export function server(fake: FakeServer) {
  let lookup = 0

  return (request: ApiRequest): unknown => {
    if (request.method === 'GET' && request.path === 'idempotency/store') {
      return fake.store ?? store
    }

    if (request.method === 'POST' && request.path === 'idempotency/lookup') {
      const answers = fake.lookups ?? [notFound()]
      const answer = answers[Math.min(lookup, answers.length - 1)]!
      lookup += 1

      if (answer instanceof DashboardApiError) {
        throw answer
      }

      return answer
    }

    throw new DashboardApiError(404, `No fake for ${request.method} ${request.path}`)
  }
}

/** A lookup answer the server holds until the test calls `release`. */
export function held() {
  let release: (view: LookupView) => void = () => undefined
  const answer = new Promise<LookupView>((resolve) => {
    release = resolve
  })

  return { answer, release: (view: LookupView) => release(view) }
}

export function open(fake: FakeServer = {}, options: RenderModuleOptions = {}) {
  return renderModule(idempotency, { api: server(fake), ...options })
}

export function user() {
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
}
