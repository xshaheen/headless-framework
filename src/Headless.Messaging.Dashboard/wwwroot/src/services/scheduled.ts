import type { MessageLane } from '@/components/MessageDetailDialog.vue'

/** Pending state only: the Delayed/Queued distinction the runtime uses internally collapses to one Pending status for operators (R2). */
export type ScheduledDeliveryStatus = 'Pending'

export interface ScheduledDeliveryView {
  storageId: string
  messageId: string
  messageName: string
  lane: MessageLane
  /**
   * The provider's full-precision due instant, exactly as the server serialized it. Never parse
   * this to a Date and resubmit it — the fence compares the server column verbatim (KTD4); parse
   * only a throwaway copy for display via utilities/dateTimeParser.
   */
  expectedDueAt: string
  status: ScheduledDeliveryStatus
  isLeased: boolean
  owner: string | null
  lockedUntil: string | null
  inlineAttempts: number
}

export interface ScheduledDeliveryPage {
  items: ScheduledDeliveryView[]
  index: number
  size: number
  totalItems: number
  totalPages: number
  hasPrevious: boolean
  hasNext: boolean
}

export type ScheduledDeliveryAction = 'revoke' | 'dispatch-now'

export type ScheduledDeliveryOutcome =
  'Applied' | 'NotFound' | 'StateConflict' | 'Active' | 'OperationConflict'

export interface ScheduledDeliveryOperationRequest {
  operationId: string
  storageId: string
  expectedDueAt: string
  reason: string
}

export interface ScheduledDeliveryOperationResult {
  operationId: string
  operationType: string
  outcome: ScheduledDeliveryOutcome
  storageId: string
  expectedDueAt: string
  messageName: string | null
  messageId: string | null
  lane: MessageLane | null
  actor: string
  reason: string
  createdAt: string
  isReplay: boolean
}

/** The 403 body shape from DashboardOperatorAuthority.CreateForbiddenResult. */
export interface OperatorActorRequiredProblem {
  code: string
  remedy: string
  message: string
}

const CANNED_REASONS: Record<ScheduledDeliveryAction, string> = {
  revoke: 'Dashboard revoke',
  'dispatch-now': 'Dashboard dispatch now',
}

/** Builds the mutation request with a client-minted operation id and the dashboard's canned reason (A7). */
export function createScheduledDeliveryOperationRequest(
  row: ScheduledDeliveryView,
  action: ScheduledDeliveryAction,
  operationId: string,
): ScheduledDeliveryOperationRequest {
  return {
    operationId,
    storageId: row.storageId,
    expectedDueAt: row.expectedDueAt,
    reason: CANNED_REASONS[action],
  }
}

/**
 * The delayed processor claims due rows on a roughly one-minute cadence (A9); dispatch-now on a row
 * already inside that window cannot advance it, so the action is offered only outside the window, and
 * never while a dispatch lease is live.
 */
const DISPATCH_NOW_LOOKAHEAD_MS = 60_000

export function canDispatchNow(row: ScheduledDeliveryView): boolean {
  if (row.isLeased) return false

  const dueAtMs = new Date(row.expectedDueAt).getTime()
  if (Number.isNaN(dueAtMs)) return true

  return dueAtMs - Date.now() > DISPATCH_NOW_LOOKAHEAD_MS
}

/**
 * Outcome messages are keyed by action plus outcome (KTD6): Active means "may or may not deliver"
 * for revoke and "claimed elsewhere, will be sent once when its lease expires" for dispatch-now.
 */
export function describeOutcome(
  action: ScheduledDeliveryAction,
  outcome: ScheduledDeliveryOutcome,
  isReplay: boolean,
): string {
  if (isReplay) {
    return 'Already applied — this replays the result of an earlier identical request.'
  }

  switch (outcome) {
    case 'Applied':
      return action === 'revoke'
        ? 'Revoked. The ledger keeps the only trace of this row.'
        : 'Dispatched now. Picked up within about a minute by a delayed processor.'
    case 'NotFound':
      return 'No longer a pending scheduled delivery — it may already have been sent, revoked, or retried.'
    case 'StateConflict':
      return 'The row changed since it was listed. Reloading the list before retrying.'
    case 'Active':
      return action === 'revoke'
        ? 'A delivery attempt was already reserved; it may or may not deliver.'
        : 'Claimed elsewhere; it will be sent once when its lease expires.'
    case 'OperationConflict':
      return 'A different request already used this operation id; nothing changed.'
    default:
      return 'Unknown outcome.'
  }
}

/** Outcomes after which the view reloads the list so the operator sees current state before retrying. */
export function shouldReloadAfterOutcome(outcome: ScheduledDeliveryOutcome): boolean {
  return outcome === 'StateConflict' || outcome === 'NotFound' || outcome === 'OperationConflict'
}
