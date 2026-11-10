import type { CommandDefinition } from '@headless/dashboard-sdk/commands'
import { DashboardApiError, type ApiRequest } from '@headless/dashboard-sdk/data'
import { renderModule, type RenderModuleOptions } from '@headless/dashboard-sdk/testing'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type { CronJob, JobFunction, JobNode } from '../src/cron.ts'
import { addDays, type DailyBucket } from '../src/insight.tsx'
import jobs from '../src/module.ts'
import type { Attempt, Run } from '../src/runs.ts'

export const now = new Date('2026-11-05T10:00:00Z')

let sequence = 0

export function run(status: Run['status'], overrides: Partial<Run> = {}): Run {
  sequence += 1

  return {
    id: `00000000-0000-0000-0000-${String(sequence).padStart(12, '0')}`,
    kind: 'TimeJob',
    function: 'billing.charge',
    status,
    executionTime: '2026-11-05T09:56:00Z',
    createdAt: '2026-11-05T09:50:00Z',
    executedAt: status === 'Idle' || status === 'Queued' ? undefined : '2026-11-05T09:56:00Z',
    elapsedMilliseconds: 340,
    retryCount: status === 'Failed' ? 2 : 0,
    retries: status === 'Failed' ? 2 : 0,
    exceptionMessage: status === 'Failed' ? 'Gateway timeout' : undefined,
    version: '1',
    ...overrides,
  }
}

export function cronJob(overrides: Partial<CronJob> = {}): CronJob {
  return {
    id: 'c0000000-0000-0000-0000-000000000001',
    function: 'reports.nightly',
    description: 'Nightly report',
    expression: '0 2 * * *',
    reading: 'At 02:00',
    timeZoneId: '',
    isPaused: false,
    definedInCode: false,
    onOverlap: 'Skip',
    retries: 0,
    nextDueUtc: '2026-11-06T01:00:00Z',
    nextFires: [],
    version: '1',
    ...overrides,
  }
}

export function node(overrides: Partial<JobNode> = {}): JobNode {
  return {
    identity: 'node-a',
    state: 'Alive',
    claimedFunctions: [],
    isLocal: false,
    maxConcurrency: 8,
    running: 0,
    runningCapped: false,
    lastHeartbeatAt: '2026-11-05T09:59:50Z',
    lastHostException: null,
    lastHostExceptionAt: null,
    ...overrides,
  }
}

export const functions: JobFunction[] = [
  {
    name: 'billing.charge',
    contractVersion: '1',
    priority: 'Normal',
    maxConcurrency: 0,
    claimedBy: ['node-a'],
    succeeded: 10,
    failed: 2,
    running: 0,
    waiting: 0,
    cronJobs: [],
  },
  {
    name: 'billing.refund',
    contractVersion: '1',
    priority: 'Normal',
    maxConcurrency: 0,
    claimedBy: ['node-a'],
    succeeded: 3,
    failed: 0,
    running: 0,
    waiting: 0,
    cronJobs: [],
  },
]

const base = {
  permission: 'operate' as const,
  typedConfirmationAbove: 100,
  canRun: true,
  arguments: { type: 'object' as const, required: [], properties: {} },
}

export const commands: CommandDefinition[] = [
  {
    ...base,
    id: 'jobs.requeue',
    label: 'Requeue',
    objectKind: 'run',
    confirmation: 'confirm',
    requiresReason: true,
    supportsFilter: true,
  },
  {
    ...base,
    id: 'jobs.cancel',
    label: 'Cancel',
    objectKind: 'run',
    confirmation: 'confirm',
    requiresReason: true,
    supportsFilter: true,
  },
  {
    ...base,
    id: 'jobs.delete',
    label: 'Delete',
    objectKind: 'run',
    confirmation: 'confirm',
    requiresReason: true,
    supportsFilter: true,
  },
  {
    ...base,
    id: 'jobs.run-now',
    label: 'Run now',
    objectKind: 'cron-job',
    confirmation: 'none',
    requiresReason: false,
    supportsFilter: false,
  },
  {
    ...base,
    id: 'jobs.pause',
    label: 'Pause',
    objectKind: 'cron-job',
    confirmation: 'none',
    requiresReason: false,
    supportsFilter: false,
    inverseCommandId: 'jobs.resume',
  },
  {
    ...base,
    id: 'jobs.resume',
    label: 'Resume',
    objectKind: 'cron-job',
    confirmation: 'none',
    requiresReason: false,
    supportsFilter: false,
    inverseCommandId: 'jobs.pause',
  },
  {
    ...base,
    id: 'jobs.edit-cron',
    label: 'Edit schedule',
    objectKind: 'cron-job',
    confirmation: 'none',
    requiresReason: false,
    supportsFilter: false,
    arguments: {
      type: 'object',
      required: ['expression'],
      properties: {
        expression: { type: 'string', title: 'Expression' },
        timeZoneId: { type: 'string', title: 'Time zone' },
      },
    },
  },
  // The run commands below carry the argument schema the shell projects from the module's declaration.
  {
    ...base,
    id: 'jobs.edit',
    label: 'Edit',
    objectKind: 'run',
    confirmation: 'confirm',
    requiresReason: true,
    supportsFilter: false,
    arguments: {
      type: 'object',
      required: [],
      properties: {
        executionTime: { type: 'string', title: 'Execution time', format: 'date-time' },
        request: { type: 'string', title: 'Request', contentMediaType: 'application/json', maxLength: 65_536 },
        retries: { type: 'integer', title: 'Retries', minimum: 0, maximum: 100 },
        retryIntervals: {
          type: 'string',
          title: 'Retry intervals (seconds)',
          contentMediaType: 'application/json',
          maxLength: 2000,
        },
      },
    },
  },
  {
    ...base,
    id: 'jobs.duplicate',
    label: 'Duplicate',
    objectKind: 'run',
    confirmation: 'confirm',
    requiresReason: true,
    supportsFilter: false,
    arguments: {
      type: 'object',
      required: [],
      properties: {
        executionTime: { type: 'string', title: 'Execution time', format: 'date-time', notBeforeNow: true },
      },
    },
  },
  {
    ...base,
    id: 'jobs.force-delete',
    label: 'Force delete',
    objectKind: 'run',
    confirmation: 'confirm',
    requiresReason: true,
    supportsFilter: false,
  },
  {
    ...base,
    id: 'jobs.delete-cron',
    label: 'Delete',
    objectKind: 'cron-job',
    confirmation: 'confirm',
    requiresReason: true,
    supportsFilter: false,
  },
]

/** Why the fake server refuses `commandId` on `row` now, as the module's rules would. */
function refusal(row: Run | CronJob, commandId: string, runs: readonly Run[] = []): string | undefined {
  if ('isPaused' in row) {
    if (commandId === 'jobs.delete-cron') {
      return row.definedInCode
        ? 'Defined in code'
        : runs.some((item) => item.cronJobId === row.id && item.status === 'InProgress')
          ? 'Running — wait or cancel the occurrence'
          : undefined
    }

    if (commandId === 'jobs.run-now' && row.isPaused) {
      return 'Paused — resume first'
    }

    if (commandId === 'jobs.pause' && row.isPaused) {
      return 'Already paused'
    }

    if (commandId === 'jobs.resume' && !row.isPaused) {
      return 'Not paused'
    }

    return undefined
  }

  if (commandId === 'jobs.requeue') {
    return row.status === 'Failed' ? undefined : row.status === 'InProgress' ? 'Running — cancel instead' : 'Not failed'
  }

  if (commandId === 'jobs.cancel') {
    return row.status === 'InProgress' || row.status === 'Queued' ? undefined : 'Not running'
  }

  if (commandId === 'jobs.edit') {
    return row.status === 'Idle' || row.status === 'Queued' ? undefined : 'Already finished — duplicate it instead'
  }

  if (commandId === 'jobs.duplicate') {
    return ['Idle', 'Queued', 'InProgress'].includes(row.status) ? 'Not finished yet' : undefined
  }

  if (commandId === 'jobs.force-delete') {
    return row.status !== 'InProgress'
      ? 'Not running — delete it instead'
      : row.cancelRequested
        ? undefined
        : 'Cancel first'
  }

  return undefined
}

export interface FakeServer {
  runs?: Run[]
  attempts?: Readonly<Record<string, Attempt[]>>
  cronJobs?: CronJob[]
  /** The sidebar counts by status, as `GET api/counts?product=jobs` answers them. */
  counts?: Readonly<Record<string, number>>
  /** The Jobs Overview figures, as `GET api/overview` serves them. */
  figures?: unknown[]
  /** The Jobs attention items, as `GET api/needs-you` serves them. */
  attention?: unknown[]
  /** The registered functions; the two billing functions, both claimed by node-a, by default. */
  functions?: JobFunction[]
  nodes?: JobNode[]
  /** The counts of one day for `GET jobs/series/daily`, `null` for a day the store could not read; zeros by default. */
  daily?: (date: string) => Partial<NonNullable<DailyBucket['counts']>> | null
  extra?: (request: ApiRequest) => unknown
}

/** A fake of the Jobs module's API, the shell's aggregates it reads, and the shell's command endpoints for it. */
export function server({
  runs = [],
  attempts = {},
  cronJobs = [],
  counts = {},
  figures = [],
  attention = [],
  functions: registered = functions,
  nodes = [],
  daily,
  extra,
}: FakeServer) {
  return (request: ApiRequest) => {
    const answered = extra?.(request)

    if (answered !== undefined) {
      return answered
    }

    const query = request.query ?? {}

    if (request.path === 'counts') {
      return {
        counts: Object.fromEntries(Object.entries(counts).map(([key, count]) => [key, { count, capped: false }])),
        stale: [],
      }
    }

    if (request.path === 'overview') {
      return { modules: [{ id: 'jobs', displayName: 'Jobs', stale: false, items: figures, tracks: [] }] }
    }

    if (request.path === 'needs-you') {
      return { modules: [{ id: 'jobs', displayName: 'Jobs', stale: false, items: attention }] }
    }

    if (request.method === 'GET' && request.path === 'jobs/runs') {
      const items = runs.filter(
        (row) =>
          (!query.status || row.status === query.status) &&
          (!query.fingerprint || row.failureFingerprint === query.fingerprint) &&
          (!query.function || row.function === query.function) &&
          (!query.correlationId || row.correlationId === query.correlationId) &&
          (!query.cronJobId || row.cronJobId === query.cronJobId),
      )

      return { items, count: items.length, countCapped: false }
    }

    const attemptsOf = /^jobs\/runs\/(.+)\/attempts$/.exec(request.path)

    if (request.method === 'GET' && attemptsOf) {
      return { items: attempts[decodeURIComponent(attemptsOf[1]!)] ?? [] }
    }

    const one = /^jobs\/runs\/([^/]+)$/.exec(request.path)

    if (request.method === 'GET' && one) {
      const row = runs.find((candidate) => candidate.id === decodeURIComponent(one[1]!))

      if (!row) {
        throw new DashboardApiError(404, 'Not found')
      }

      return { run: row, children: [] }
    }

    if (request.method === 'GET' && request.path === 'jobs/functions') {
      return { items: registered }
    }

    if (request.method === 'GET' && request.path === 'jobs/nodes') {
      return { items: nodes }
    }

    if (request.method === 'GET' && request.path === 'jobs/series/daily') {
      const from = String(query.from)
      const days: DailyBucket[] = []

      for (let date = from; date <= String(query.to); date = addDays(date, 1)) {
        const zero = {
          Idle: 0,
          Queued: 0,
          InProgress: 0,
          Succeeded: 0,
          DueDone: 0,
          Failed: 0,
          Cancelled: 0,
          Skipped: 0,
        }
        const counts = daily ? daily(date) : zero

        days.push({
          date,
          start: `${date}T00:00:00Z`,
          end: `${addDays(date, 1)}T00:00:00Z`,
          counts: counts && { ...zero, ...counts },
        })
      }

      return {
        zone: query.zone ?? 'UTC',
        from,
        to: query.to,
        asOf: now.toISOString(),
        exact: days.every((day) => day.counts),
        days,
      }
    }

    if (request.method === 'GET' && request.path === 'jobs/cron-jobs') {
      // As the server does, the list carries next fires only when asked for them.
      return { items: cronJobs.map((item) => (query.fires ? item : { ...item, nextFires: [] })) }
    }

    const cron = /^jobs\/cron-jobs\/(.+)$/.exec(request.path)

    if (request.method === 'GET' && cron) {
      const row = cronJobs.find((candidate) => candidate.id === decodeURIComponent(cron[1]!))

      if (!row) {
        throw new DashboardApiError(404, 'Not found')
      }

      return row
    }

    if (request.method === 'GET' && request.path === 'jobs/cron-preview') {
      return {
        timeZoneId: query.timeZoneId || 'Europe/Berlin',
        reading: 'At 02:00',
        nextFires: [],
        validationMessage: undefined,
      }
    }

    if (request.path === 'jobs/commands') {
      return { commands }
    }

    if (request.path === 'jobs/commands/allowed') {
      const ids = [query.id ?? []].flat()
      const all: (Run | CronJob)[] = [...runs, ...cronJobs]

      return {
        objects: ids.map((id) => {
          const row = all.find((candidate) => candidate.id === id)!

          return {
            kind: query.kind,
            id,
            commands: commands.map((command) => {
              const reason = refusal(row, command.id, runs)

              return { id: command.id, allowed: !reason, disabledReason: reason }
            }),
          }
        }),
      }
    }

    throw new DashboardApiError(404, `No fake for ${request.method} ${request.path}`)
  }
}

export function open(fake: FakeServer, options: RenderModuleOptions = {}) {
  return renderModule(jobs, { api: server(fake), ...options })
}

export function change(row: Run) {
  return { module: 'jobs', kind: 'run', id: row.id, topic: 'jobs.run', version: row.version, projection: { ...row } }
}

export function user() {
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
}
