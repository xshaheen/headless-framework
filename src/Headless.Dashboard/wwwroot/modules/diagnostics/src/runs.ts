import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { createCommandsApi, type CommandDefinition } from '@headless/dashboard-sdk/commands'
import type { DashboardStateRole } from '@headless/dashboard-sdk'
import type { CappedCount, DashboardApi, DashboardListPage } from '@headless/dashboard-sdk/data'
import { useQuery } from '@headless/dashboard-sdk/query'
import { useState } from 'react'

/** A run as the module's list and a live change's projection carry it. */
export interface Run {
  id: string
  status: string
  attempts: number
  lastError?: string
  traceId: string
  createdAt: string
  updatedAt: string
  version: string
}

export type RunEventKind = 'Created' | 'Started' | 'Succeeded' | 'Failed' | 'Requeued' | 'Note'

export interface RunEvent {
  kind: RunEventKind | string
  at: string
  text?: string
}

/** A run as the run page and the inspector show it: the row's fields and its timeline. */
export interface RunDetail extends Run {
  timeline: RunEvent[]
}

export interface RunFilter {
  status: string
  error: string
}

interface RunListAnswer {
  items: Run[]
  count: number
  countCapped: boolean
  /** The keyset cursor of the page's last run, when more runs follow it. */
  nextCursor?: string | null
}

const runsPageSize = 100

/** Whether `run` matches `filter` as the server reads it: the status and the last error, each exactly. */
export function runMatches(run: Run, filter: RunFilter) {
  return (!filter.status || run.status === filter.status) && (!filter.error || run.lastError === filter.error)
}

export const runStatuses = ['Queued', 'Running', 'Succeeded', 'Failed'] as const

const runRoles: Readonly<Record<(typeof runStatuses)[number], DashboardStateRole>> = {
  Queued: 'enq',
  Running: 'proc',
  Succeeded: 'ok',
  Failed: 'fail',
}

/**
 * The mark a run's status shows. `Requeuing…` is the client's own state while a requeue waits for its change, and
 * reads as queued, where the requeue puts the run; a status this build does not know reads as off.
 */
export function runStatusMark(status: string, requeuing = false): { tone: DashboardStateRole; label: string } {
  return requeuing
    ? { tone: 'enq', label: 'Requeuing…' }
    : { tone: runRoles[status as keyof typeof runRoles] ?? 'off', label: status }
}

export function runsTopic(module: string) {
  return `${module}.run`
}

/** One page of runs, newest first: the first page, or the page after the run `cursor` names. */
export async function fetchRuns(
  api: DashboardApi,
  module: string,
  filter: RunFilter,
  cursor?: string,
): Promise<DashboardListPage<Run> & { count: CappedCount }> {
  const query: Record<string, string> = { limit: String(runsPageSize) }

  if (cursor) {
    query.after = cursor
  }

  if (filter.status) {
    query.status = filter.status
  }

  if (filter.error) {
    query.error = filter.error
  }

  const answer = await api.request<RunListAnswer>({ method: 'GET', path: `${encodeURIComponent(module)}/runs`, query })

  return { items: answer.items, count: { value: answer.count, capped: answer.countCapped }, nextCursor: answer.nextCursor ?? null }
}

export function fetchRun(api: DashboardApi, module: string, id: string) {
  return api.request<RunDetail>({ method: 'GET', path: `${encodeURIComponent(module)}/runs/${encodeURIComponent(id)}` })
}

export function addRun(api: DashboardApi, module: string) {
  return api.request<RunDetail>({ method: 'POST', path: `${encodeURIComponent(module)}/runs` })
}

export const viewOnlyReason = 'Needs operate permission'

/** The module's commands as the server describes them to this user, with a viewer's commands disabled. */
export function useRunCommands() {
  const { api } = useDashboard()
  const module = useModule()
  const commands = useQuery({
    queryKey: ['commands', module.id],
    queryFn: () => createCommandsApi(api, module.id).list(),
    staleTime: Infinity,
  })
  const all = commands.data ?? []

  function find(name: string): CommandDefinition | undefined {
    const command = all.find((candidate) => candidate.id === `${module.id}.${name}`)

    if (!command || module.canOperate) {
      return command
    }

    return { ...command, canRun: false, disabledReason: command.disabledReason ?? viewOnlyReason }
  }

  return { all, fail: find('fail'), requeue: find('requeue'), addNote: find('add-note'), query: commands }
}

/**
 * The runs a requeue finished on but whose change has not arrived: each shows `Requeuing…` until its version moves
 * past the one it had when the operator confirmed.
 */
export function useRequeuing() {
  const [pending, setPending] = useState<ReadonlyMap<string, string>>(new Map())

  return {
    isRequeuing: (run: Pick<Run, 'id' | 'version'>) => pending.get(run.id) === run.version,
    mark: (ids: readonly string[], versions: ReadonlyMap<string, string>) =>
      setPending((current) => {
        const next = new Map(current)

        for (const id of ids) {
          const version = versions.get(id)

          if (version !== undefined) {
            next.set(id, version)
          }
        }

        return next
      }),
  }
}
