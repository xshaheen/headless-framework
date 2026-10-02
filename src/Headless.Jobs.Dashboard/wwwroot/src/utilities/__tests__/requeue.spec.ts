import { describe, expect, it } from 'vitest'
import { Status } from '@/http/services/types/base/baseHttpResponse.types'
import { canRequeue } from '@/utilities/requeue'

const otherStatuses = [
  Status.Idle,
  Status.Queued,
  Status.InProgress,
  Status.Done,
  Status.DueDone,
  Status.Cancelled,
  Status.Skipped,
]

describe('requeue visibility', () => {
  it('offers requeue for a failed row whether the status is a number or its display name', () => {
    expect(canRequeue(Status.Failed)).toBe(true)
    expect(canRequeue('Failed')).toBe(true)
  })

  it.each(otherStatuses)('hides requeue for status %s', (status) => {
    expect(canRequeue(status)).toBe(false)
    expect(canRequeue(Status[status])).toBe(false)
  })

  it('hides requeue when the status is missing', () => {
    expect(canRequeue(undefined)).toBe(false)
    expect(canRequeue(null)).toBe(false)
    expect(canRequeue('Unknown')).toBe(false)
  })
})
