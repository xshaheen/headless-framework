import type { AddChainJobsRequest } from '@/http/services/types/timeJobService.types'
import { formatLocalDateTimeWithoutZ } from '@/utilities/dateTimeParser'

export interface ChainRetryInterval {
  value?: number
}

export interface ChainJobForm {
  functionName: string
  description: string
  executionDate: Date | undefined
  executionTime: string
  ignoreDateTime: boolean
  retries: number
  requestData: string
  retryIntervals: Array<ChainRetryInterval | number>
}

export interface ChainChildForm extends ChainJobForm {
  runCondition: number | null
}

export interface ChainChildWithGrandChildrenForm extends ChainChildForm {
  grandChildren: ChainChildForm[]
}

const wallClock = (job: ChainJobForm): Date | null => {
  if (!job.executionDate || !job.executionTime) return null
  const [hours, minutes, seconds = 0] = job.executionTime.split(':').map(Number)
  const localDate = new Date(job.executionDate)
  localDate.setHours(hours, minutes, seconds, 0)
  return localDate
}

// The server reads every offset-less time in the chain in the request's timeZoneId (the scheduling zone), as the Add
// dialog's time is read, so root, child, and grandchild times all go out as wall-clock with no zone suffix.
const chainExecutionTime = (job: ChainJobForm) => {
  if (job.ignoreDateTime) return undefined
  const date = wallClock(job)
  return date ? formatLocalDateTimeWithoutZ(date) : null
}

const toIntervals = (job: ChainJobForm): number[] =>
  (job.retryIntervals ?? [])
    .map((item) => (typeof item === 'object' ? item.value : item))
    .filter((value): value is number => value !== undefined)

const toChild = (job: ChainChildForm, children: AddChainJobsRequest[]): AddChainJobsRequest => ({
  function: job.functionName,
  description: job.description,
  runCondition: job.runCondition,
  executionTime: chainExecutionTime(job),
  retries: job.retries,
  request: job.requestData || null,
  intervals: toIntervals(job),
  children,
})

/** Builds the chain request from the wizard's forms, skipping any child or grandchild with no function chosen. */
export function buildChainJobsRequest(
  parent: ChainJobForm,
  children: ChainChildWithGrandChildrenForm[],
): AddChainJobsRequest {
  return {
    function: parent.functionName,
    description: parent.description,
    executionTime: chainExecutionTime(parent),
    retries: parent.retries,
    request: parent.requestData || null,
    intervals: toIntervals(parent),
    children: children
      .filter((child) => child.functionName)
      .map((child) =>
        toChild(
          child,
          child.grandChildren.filter((grandChild) => grandChild.functionName).map((grandChild) => toChild(grandChild, [])),
        ),
      ),
  }
}
