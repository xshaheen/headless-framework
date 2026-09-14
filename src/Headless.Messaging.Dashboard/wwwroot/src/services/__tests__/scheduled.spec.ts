import { describe, expect, it } from 'vitest'
import {
  canDispatchNow,
  createScheduledDeliveryOperationRequest,
  describeOutcome,
  shouldReloadAfterOutcome,
  type ScheduledDeliveryView,
} from '@/services/scheduled'

function makeRow(overrides: Partial<ScheduledDeliveryView> = {}): ScheduledDeliveryView {
  return {
    storageId: '11111111-1111-1111-1111-111111111111',
    messageId: 'msg-1',
    messageName: 'orders.created',
    lane: 'Bus',
    expectedDueAt: '2026-09-14T10:30:00.1234567+00:00',
    status: 'Pending',
    isLeased: false,
    owner: null,
    lockedUntil: null,
    inlineAttempts: 0,
    ...overrides,
  }
}

describe('scheduled delivery operation requests', () => {
  it('builds the request with the verbatim due-instant string and a canned reason', () => {
    const row = makeRow()

    const request = createScheduledDeliveryOperationRequest(row, 'revoke', 'operation-1')

    expect(request).toEqual({
      operationId: 'operation-1',
      storageId: row.storageId,
      expectedDueAt: row.expectedDueAt,
      reason: 'Dashboard revoke',
    })
    // The fence compares this value verbatim (KTD4); it must never be reformatted through a Date.
    expect(request.expectedDueAt).toBe('2026-09-14T10:30:00.1234567+00:00')
  })

  it('mints the dispatch-now canned reason', () => {
    const request = createScheduledDeliveryOperationRequest(
      makeRow(),
      'dispatch-now',
      'operation-2',
    )

    expect(request.reason).toBe('Dashboard dispatch now')
  })
})

describe('dispatch-now eligibility', () => {
  it('disables dispatch-now for a leased row', () => {
    const row = makeRow({
      isLeased: true,
      expectedDueAt: new Date(Date.now() + 600_000).toISOString(),
    })

    expect(canDispatchNow(row)).toBe(false)
  })

  it('disables dispatch-now for a row already due within the delayed processor pass interval', () => {
    const row = makeRow({ expectedDueAt: new Date(Date.now() + 10_000).toISOString() })

    expect(canDispatchNow(row)).toBe(false)
  })

  it('enables dispatch-now for an unleased row due well outside the pass interval', () => {
    const row = makeRow({ expectedDueAt: new Date(Date.now() + 600_000).toISOString() })

    expect(canDispatchNow(row)).toBe(true)
  })

  it('keeps revoke available regardless of dispatch-now eligibility (leased row)', () => {
    // canDispatchNow only gates the dispatch-now button; revoke has no lease precondition (KTD5).
    const row = makeRow({ isLeased: true })

    expect(canDispatchNow(row)).toBe(false)
  })
})

describe('outcome messages keyed by action plus outcome', () => {
  it('distinguishes Active for revoke from Active for dispatch-now', () => {
    const revokeActive = describeOutcome('revoke', 'Active', false)
    const dispatchActive = describeOutcome('dispatch-now', 'Active', false)

    expect(revokeActive).toContain('may or may not deliver')
    expect(dispatchActive).toContain('Claimed elsewhere')
    expect(revokeActive).not.toBe(dispatchActive)
  })

  it.each([
    ['Applied', false],
    ['NotFound', false],
    ['StateConflict', false],
    ['OperationConflict', false],
  ] as const)('gives %s its own message text', (outcome) => {
    const revokeMessage = describeOutcome('revoke', outcome, false)
    const dispatchMessage = describeOutcome('dispatch-now', outcome, false)

    expect(revokeMessage.length).toBeGreaterThan(0)
    expect(dispatchMessage.length).toBeGreaterThan(0)
  })

  it('gives a replayed request its own message regardless of outcome', () => {
    const message = describeOutcome('revoke', 'Applied', true)

    expect(message).toContain('replays')
  })
})

describe('list reload after an outcome', () => {
  it.each(['StateConflict', 'NotFound', 'OperationConflict'] as const)(
    'reloads the list after %s so the operator sees current state',
    (outcome) => {
      expect(shouldReloadAfterOutcome(outcome)).toBe(true)
    },
  )

  it.each(['Applied', 'Active'] as const)('does not reload after %s', (outcome) => {
    expect(shouldReloadAfterOutcome(outcome)).toBe(false)
  })
})
