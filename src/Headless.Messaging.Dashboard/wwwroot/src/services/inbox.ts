import type { MessageLane } from '@/components/MessageDetailDialog.vue'

export type InboxStatus = 'Failed' | 'Scheduled' | 'Succeeded' | 'Delayed' | 'Queued'

export interface InboxGeneration {
  incarnationId: string
  generation: number
  tenantId: string | null
  messageId: string
  lane: MessageLane
  consumerIdentity: string
  status: InboxStatus
  isOrphaned: boolean
  replayParentIncarnationId: string | null
  effectiveExpiresAt: string | null
  isHeld: boolean
}

export interface InboxOperationRequest {
  operationId: string
  expectedIncarnationId: string
  expectedStatus: InboxStatus
  reason: string
}

export function isTerminalInboxGeneration(status: InboxStatus): boolean {
  return status === 'Succeeded' || status === 'Failed'
}

export function createInboxOperationRequest(
  generation: InboxGeneration,
  operationId: string,
  reason: string,
): InboxOperationRequest {
  return {
    operationId,
    expectedIncarnationId: generation.incarnationId,
    expectedStatus: generation.status,
    reason,
  }
}
