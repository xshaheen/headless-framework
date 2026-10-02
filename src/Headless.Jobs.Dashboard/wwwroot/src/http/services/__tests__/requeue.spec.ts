import { beforeEach, describe, expect, it, vi } from 'vitest'

const { sendAsync } = vi.hoisted(() => ({ sendAsync: vi.fn() }))

vi.mock('@/http/base/baseHttpService', () => ({
  useBaseHttpService: () => ({ sendAsync }),
}))

import { cronJobService } from '@/http/services/cronJobService'
import { jobsService } from '@/http/services/jobsService'

describe('requeue services', () => {
  beforeEach(() => {
    sendAsync.mockReset()
    sendAsync.mockResolvedValue({})
  })

  it('posts a time-job requeue with the row id', async () => {
    await jobsService.requeue().requestAsync('3f2b1c4e-0000-4000-8000-000000000001')

    expect(sendAsync).toHaveBeenCalledExactlyOnceWith('POST', 'job/requeue', {
      paramData: { id: '3f2b1c4e-0000-4000-8000-000000000001' },
    })
  })

  it('posts a cron-occurrence requeue with the row id', async () => {
    await cronJobService.requeueOccurrence().requestAsync('3f2b1c4e-0000-4000-8000-000000000002')

    expect(sendAsync).toHaveBeenCalledExactlyOnceWith('POST', 'cron-job-occurrence/requeue', {
      paramData: { id: '3f2b1c4e-0000-4000-8000-000000000002' },
    })
  })

  it('surfaces a refused requeue to the caller', async () => {
    sendAsync.mockRejectedValueOnce(new Error('NotFailed'))

    await expect(jobsService.requeue().requestAsync('3f2b1c4e-0000-4000-8000-000000000003')).rejects.toThrow(
      'NotFailed',
    )
  })
})
