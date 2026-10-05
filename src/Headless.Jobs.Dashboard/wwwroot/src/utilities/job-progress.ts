import { Status } from '@/http/services/types/base/baseHttpResponse.types'
import { parseUtcInstant } from '@/utilities/dateTimeParser'

/** The progress columns a time-job or cron-occurrence row carries; all null until the job first reports. */
export interface JobProgressFields {
  progressPercent?: number | null
  progressMessage?: string | null
  progressUpdatedAt?: string | null
}

/** Payload of the hub's JobProgressNotification. `type` is the server's JobType, as a number or its name. */
export interface JobProgressNotification {
  id: string
  type: number | string
  percent: number
  message: string | null
  updatedAt: string
}

export type JobProgressDisplay = 'active' | 'muted' | 'none'

const TERMINAL_STATUSES = [Status.Done, Status.DueDone, Status.Failed, Status.Cancelled, Status.Skipped]

const toStatus = (status: string | number | null | undefined): Status | undefined =>
  typeof status === 'number' ? status : (Status[status as keyof typeof Status] ?? undefined)

/**
 * How a row shows its stored progress: live while it runs, muted once it has finished so a failed job still shows how
 * far it got, and not at all before it starts or when it never reported. Rows carry the status either as the enum number
 * from the API or as its name after the list services map it for display.
 */
export const jobProgressDisplay = (
  status: string | number | null | undefined,
  percent: number | null | undefined,
): JobProgressDisplay => {
  if (percent == null) return 'none'
  const value = toStatus(status)
  if (value === Status.InProgress) return 'active'
  if (value !== undefined && TERMINAL_STATUSES.includes(value)) return 'muted'
  return 'none'
}

/** At most one decimal, without a trailing `.0`: 42.46 reads `42.5%`, 42 reads `42%`. */
export const formatProgressPercent = (percent: number): string => `${Math.round(percent * 10) / 10}%`

/** Compact age of the last progress report, e.g. `updated 5s ago`; empty when the timestamp is missing or unreadable. */
export const formatProgressAge = (updatedAt: string | null | undefined, now: number = Date.now()): string => {
  if (!updatedAt) return ''
  const time = parseUtcInstant(updatedAt).getTime()
  if (Number.isNaN(time)) return ''

  // A store clock slightly ahead of the browser would otherwise read as a negative age.
  const seconds = Math.max(0, Math.floor((now - time) / 1000))
  if (seconds < 60) return `updated ${seconds}s ago`
  if (seconds < 3600) return `updated ${Math.floor(seconds / 60)}m ago`
  if (seconds < 86400) return `updated ${Math.floor(seconds / 3600)}h ago`
  return `updated ${Math.floor(seconds / 86400)}d ago`
}

/**
 * Writes a progress notification onto the row with the same id, searching chained children too. Returns whether a row
 * matched; a notification for a row that is not on the current page is ignored rather than triggering a reload.
 */
export const applyJobProgress = <T extends JobProgressFields & { id: string; children?: T[] }>(
  rows: T[] | null | undefined,
  notification: JobProgressNotification,
): boolean => {
  for (const row of rows ?? []) {
    if (row.id === notification.id) {
      row.progressPercent = notification.percent
      row.progressMessage = notification.message
      row.progressUpdatedAt = notification.updatedAt
      return true
    }
    if (applyJobProgress(row.children, notification)) return true
  }
  return false
}
