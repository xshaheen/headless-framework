import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { createCommandsApi, type CommandDefinition } from '@headless/dashboard-sdk/commands'
import type { StatusTone } from '@headless/dashboard-sdk/components'
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
}

export const runStatuses = ['Queued', 'Running', 'Succeeded', 'Failed'] as const

const tones: Readonly<Record<string, StatusTone>> = {
  Queued: 'waiting',
  Running: 'active',
  Succeeded: 'healthy',
  Failed: 'needs',
}

/** The mark a run's status shows. `Requeuing…` is the client's own state while a requeue waits for its change. */
export function runStatusMark(status: string, requeuing = false): { tone: StatusTone; label: string } {
  return requeuing ? { tone: 'active', label: 'Requeuing…' } : { tone: tones[status] ?? 'waiting', label: status }
}

export function runsTopic(module: string) {
  return `${module}.run`
}

export async function fetchRuns(api: DashboardApi, module: string, filter: RunFilter): Promise<DashboardListPage<Run> & { count: CappedCount }> {
  const query: Record<string, string> = {}

  if (filter.status) {
    query.status = filter.status
  }

  if (filter.error) {
    query.error = filter.error
  }

  const answer = await api.request<RunListAnswer>({ method: 'GET', path: `${encodeURIComponent(module)}/runs`, query })

  return { items: answer.items, count: { value: answer.count, capped: answer.countCapped } }
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
 * Why the server refuses each command for `runs` now, such as `Running - cancel instead`. The runs' versions are in
 * the key, so a status change asks again.
 */
export function useRefusals(runs: readonly Pick<Run, 'id' | 'version'>[]) {
  const { api } = useDashboard()
  const module = useModule()
  const ids = runs.map((run) => run.id)
  const answer = useQuery({
    queryKey: ['allowed', module.id, runs.map((run) => `${run.id}@${run.version}`)],
    queryFn: () => createCommandsApi(api, module.id).allowed('run', ids),
    enabled: ids.length > 0,
  })

  return (commandId: string | undefined): string | null => {
    if (!commandId) {
      return null
    }

    // An action is offered only once the server has said it can succeed now, so it stays disabled while that answer
    // is loading or after it failed, rather than letting the operator run what the server may refuse.
    if (answer.isPending) {
      return 'Checking whether this can run…'
    }

    if (answer.isError) {
      return 'Could not check whether this can run. Reload to try again.'
    }

    for (const object of answer.data) {
      const command = object.commands.find((candidate) => candidate.id === commandId)

      if (command && !command.allowed) {
        return command.disabledReason ?? 'Not allowed now'
      }
    }

    return null
  }
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

const relativeFormat = new Intl.RelativeTimeFormat('en', { numeric: 'auto' })

const exactFormat = new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'long', timeZone: 'UTC' })

const units: readonly [Intl.RelativeTimeFormatUnit, number][] = [
  ['day', 86_400],
  ['hour', 3_600],
  ['minute', 60],
  ['second', 1],
]

/** `4 minutes ago`, `in 12 seconds`. */
export function relativeTime(at: Date, now: number) {
  const seconds = Math.round((at.getTime() - now) / 1_000)

  for (const [unit, size] of units) {
    if (Math.abs(seconds) >= size || unit === 'second') {
      return relativeFormat.format(Math.round(seconds / size), unit)
    }
  }

  return relativeFormat.format(0, 'second')
}

/** The exact time with its zone and offset, for the hover text. The SDK exposes no display time zone yet, so UTC. */
export function exactTime(at: Date) {
  return exactFormat.format(at)
}
