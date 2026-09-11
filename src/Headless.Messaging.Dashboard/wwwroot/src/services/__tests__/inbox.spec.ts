import { describe, expect, it } from 'vitest'
import {
  createInboxOperationRequest,
  isTerminalInboxGeneration,
  type InboxGeneration,
} from '@/services/inbox'

describe('inbox generation actions', () => {
  it.each(['Failed', 'Succeeded'] as const)(
    'enables terminal actions and preserves the %s query status in mutation JSON',
    (status) => {
      const generation: InboxGeneration = {
        incarnationId: '13ac27e8-a78b-428d-bf52-302ca4f1930f',
        generation: 1,
        tenantId: null,
        messageId: 'message-1',
        lane: 'Queue',
        consumerIdentity: 'orders.consumer',
        status,
        isOrphaned: false,
        replayParentIncarnationId: null,
        effectiveExpiresAt: null,
        isHeld: false,
      }

      expect(isTerminalInboxGeneration(generation.status)).toBe(true)
      expect(
        JSON.parse(JSON.stringify(createInboxOperationRequest(generation, 'operation-1', 'retry'))),
      ).toEqual({
        operationId: 'operation-1',
        expectedIncarnationId: generation.incarnationId,
        expectedStatus: status,
        reason: 'retry',
      })
    },
  )

  it.each(['Scheduled', 'Delayed', 'Queued'] as const)(
    'disables terminal actions for %s generations',
    (status) => {
      expect(isTerminalInboxGeneration(status)).toBe(false)
    },
  )
})
