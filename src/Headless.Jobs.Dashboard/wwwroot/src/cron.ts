import type { DashboardStateRole } from '@headless/dashboard-sdk'
import type { DashboardApi } from '@headless/dashboard-sdk/data'

/** A cron job as its list row, its page, and a live change carry it; `nextFires` is filled on its page and a function's list. */
export interface CronJob {
  id: string
  function: string
  description?: string
  expression: string
  /** The server's plain reading of the expression: `Every 5 minutes`. */
  reading?: string
  /** The zone the expression fires in; empty means the scheduler's zone. */
  timeZoneId: string
  isPaused: boolean
  definedInCode: boolean
  onOverlap: 'Allow' | 'Skip'
  retries: number
  nextDueUtc: string
  validationMessage?: string
  nextFires: string[]
  version: string
}

/** What a stored or typed expression would do, in the zone it fires in, as the server computes it. */
export interface SchedulePreview {
  timeZoneId: string
  reading?: string
  nextFires: string[]
  validationMessage?: string
}

/** A registered function, what runs it, and how its runs went in the last 24 hours. */
export interface JobFunction {
  name: string
  contractVersion: string
  priority: string
  maxConcurrency: number
  requestType?: string
  cronExpression?: string
  /** The live nodes whose run filter claims it. */
  claimedBy: string[]
  succeeded: number
  failed: number
  running: number
  waiting: number
  lastExecutedAt?: string
  cronJobs: string[]
}

/** A live node and the functions its run filter claims; `null` when it published none. */
export interface JobNode {
  identity: string
  state: string
  role?: string
  claimedFunctions?: string[] | null
}

export const cronJobsTopic = 'jobs.cron-job'

/** An active cron job waits for its next fire, so it reads as scheduled; a paused one is stopped on purpose. */
export function cronStatusMark(cronJob: Pick<CronJob, 'isPaused'>): { tone: DashboardStateRole; label: string } {
  return cronJob.isPaused ? { tone: 'off', label: 'Paused' } : { tone: 'sched', label: 'Active' }
}

const nodeTones: Readonly<Record<string, DashboardStateRole>> = { Alive: 'ok', Suspected: 'proc', Dead: 'fail', Left: 'off' }

export function nodeStatusMark(node: Pick<JobNode, 'state'>): { tone: DashboardStateRole; label: string } {
  return { tone: nodeTones[node.state] ?? 'off', label: node.state }
}

/** The cron job's display name: its description, or its function and expression. */
export function cronJobTitle(cronJob: Pick<CronJob, 'description' | 'function' | 'expression'>) {
  return cronJob.description || `${cronJob.function} (${cronJob.expression})`
}

/** The cron jobs, or one function's with up to 50 next fires each, which a Function page draws on its strip. */
export async function fetchCronJobs(api: DashboardApi, functionName?: string) {
  const answer = await api.request<{ items: CronJob[] }>({
    method: 'GET',
    path: 'jobs/cron-jobs',
    query: functionName ? { function: functionName, fires: '50' } : undefined,
  })

  return { items: answer.items }
}

export function fetchCronJob(api: DashboardApi, id: string) {
  return api.request<CronJob>({ method: 'GET', path: `jobs/cron-jobs/${encodeURIComponent(id)}`, query: { fires: '5' } })
}

export function fetchPreview(api: DashboardApi, expression: string, timeZoneId: string) {
  return api.request<SchedulePreview>({
    method: 'GET',
    path: 'jobs/cron-preview',
    query: { expression, count: '5', ...(timeZoneId ? { timeZoneId } : {}) },
  })
}

export async function fetchFunctions(api: DashboardApi) {
  return (await api.request<{ items: JobFunction[] }>({ method: 'GET', path: 'jobs/functions' })).items
}

export async function fetchNodes(api: DashboardApi) {
  return (await api.request<{ items: JobNode[] }>({ method: 'GET', path: 'jobs/nodes' })).items
}
