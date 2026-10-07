import { describe, expect, it } from 'vitest'
import { buildChainJobsRequest, runConditionLabel, type ChainChildForm } from '@/utilities/chain-jobs'

const form = (functionName: string, time: string | null): ChainChildForm => ({
  functionName,
  description: '',
  runCondition: 0,
  executionDate: time ? new Date(2026, 2, 1) : undefined,
  executionTime: time ?? '',
  ignoreDateTime: false,
  retries: 0,
  requestData: '',
  retryIntervals: [{ value: 30 }, 60],
})

describe('buildChainJobsRequest', () => {
  // The server reads every offset-less time in the chain in the request's timeZoneId, so a time typed for a child
  // must mean the same zone as one typed for the root: none of them may carry a zone suffix.
  it('sends the root, child, and grandchild times as wall-clock without a zone suffix', () => {
    const request = buildChainJobsRequest(form('Root', '09:30'), [
      { ...form('Child', '10:15'), grandChildren: [form('GrandChild', '11:00:05')] },
    ])

    expect(request.executionTime).toBe('2026-03-01T09:30:00')
    expect(request.children![0]!.executionTime).toBe('2026-03-01T10:15:00')
    expect(request.children![0]!.children![0]!.executionTime).toBe('2026-03-01T11:00:05')
  })

  it('leaves out unset times, an immediate run, and rows with no function chosen', () => {
    const request = buildChainJobsRequest(form('Root', null), [
      { ...form('Child', '10:15'), ignoreDateTime: true, grandChildren: [form('', '11:00')] },
      { ...form('', '12:00'), grandChildren: [] },
    ])

    expect(request.executionTime).toBeNull()
    expect(request.children).toHaveLength(1)
    expect(request.children![0]!.executionTime).toBeUndefined()
    expect(request.children![0]!.children).toEqual([])
    expect(request.intervals).toEqual([30, 60])
  })
})

describe('runConditionLabel', () => {
  // On Success is the enum's zero value, so the review step must not treat it as unset.
  it('names On Success, which is the zero value', () => {
    expect(runConditionLabel(0)).toBe('On Success')
  })

  it('names other conditions instead of printing their number', () => {
    expect(runConditionLabel(3)).toBe('On Failure or Cancelled')
  })

  it('reports an unset or unknown condition as None', () => {
    expect(runConditionLabel(null)).toBe('None')
    expect(runConditionLabel(undefined)).toBe('None')
    expect(runConditionLabel(99)).toBe('None')
  })
})
