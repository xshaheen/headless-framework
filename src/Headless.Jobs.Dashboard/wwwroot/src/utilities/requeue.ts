import { Status } from '@/http/services/types/base/baseHttpResponse.types'

/**
 * Whether the dashboard offers requeue for a row. Only a Failed row can be requeued; the server refuses every other
 * status, so showing the action elsewhere would only produce an error. Rows carry the status either as the enum
 * number from the API or as its name after the list services map it for display.
 */
export const canRequeue = (status: string | number | null | undefined): boolean =>
  status === Status.Failed || status === Status[Status.Failed]
