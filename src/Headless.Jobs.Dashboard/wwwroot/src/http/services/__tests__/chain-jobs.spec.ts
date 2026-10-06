import { beforeEach, describe, expect, it, vi } from 'vitest'

const { sendAsync } = vi.hoisted(() => ({ sendAsync: vi.fn() }))

vi.mock('@/http/base/baseHttpService', () => ({
  useBaseHttpService: () => ({ sendAsync }),
}))

vi.mock('@/stores/functionNames', () => ({
  useFunctionNameStore: () => ({ getContractVersion: () => 'v1' }),
}))

import { timeJobService } from '@/http/services/timeJobService'

const chain = {
  function: 'Root',
  executionTime: '2026-03-01T09:30:00',
  children: [{ function: 'Child', children: [] }],
}

describe('chain jobs request', () => {
  beforeEach(() => {
    sendAsync.mockReset()
    sendAsync.mockResolvedValue({})
  })

  it('sends the scheduling time zone, like a single add, so the server reads the root time in it', async () => {
    await timeJobService.addChainJobs().requestAsync(chain as never, 'Africa/Cairo')
    await timeJobService.addTimeJob().requestAsync({ function: 'Root' } as never, 'Africa/Cairo')

    const [chainCall, singleCall] = sendAsync.mock.calls
    expect(chainCall![0]).toBe('POST')
    expect(chainCall![1]).toBe('time-job/add')
    expect(chainCall![2].paramData).toEqual({ timeZoneId: 'Africa/Cairo' })
    expect(chainCall![2].paramData).toEqual(singleCall![2].paramData)
    expect(chainCall![2].bodyData).toMatchObject({
      function: 'Root',
      executionTime: '2026-03-01T09:30:00',
      contractVersion: 'v1',
      children: [{ function: 'Child', contractVersion: 'v1' }],
    })
  })
})
