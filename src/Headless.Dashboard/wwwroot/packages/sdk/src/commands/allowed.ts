import { useQuery } from '@tanstack/react-query'
import { useDashboard, useModule } from '../context.tsx'
import { createCommandsApi } from './api.ts'

/**
 * Why the server refuses one command now: `(commandId)` gives a reason when it refuses any object, so an action given
 * it as `disabledReason` never sends a refused row; `.refused(commandId)` gives each refused object's own reason, so a
 * `CommandAction` given it as `refused` runs on the rest and tags these in their rows.
 */
export interface AllowedActions {
  (commandId: string | undefined): string | null
  refused: (commandId: string | undefined) => ReadonlyMap<string, string>
}

/**
 * Why the server refuses each command for `objects` of `kind` now, such as `Running - cancel instead`, answered per
 * object. Each object's version is in the key, so a status change asks again. An action is offered only once the
 * server has said it can succeed now: while that answer loads, or after it failed, every object is refused with a
 * reason that says so, rather than letting the operator run what the server may refuse.
 */
export function useAllowedActions(kind: string, objects: readonly { id: string; version?: string }[]): AllowedActions {
  const { api } = useDashboard()
  const module = useModule()
  const answer = useQuery({
    queryKey: ['allowed', module.id, kind, objects.map((object) => `${object.id}@${object.version ?? ''}`)],
    queryFn: () =>
      createCommandsApi(api, module.id).allowed(
        kind,
        objects.map((object) => object.id),
      ),
    enabled: objects.length > 0,
  })

  function refused(commandId: string | undefined) {
    const reasons = new Map<string, string>()
    const unknown = answer.isPending
      ? 'Checking whether this can run…'
      : answer.isError
        ? 'Could not check whether this can run. Reload to try again.'
        : null

    if (commandId) {
      for (const object of objects) {
        const command = answer.data?.find((candidate) => candidate.id === object.id)?.commands.find((candidate) => candidate.id === commandId)
        const reason = unknown ?? (command && !command.allowed ? (command.disabledReason ?? 'Not allowed now') : null)

        if (reason) {
          reasons.set(object.id, reason)
        }
      }
    }

    return reasons
  }

  const allowed = (commandId: string | undefined) => refused(commandId).values().next().value ?? null

  allowed.refused = refused

  return allowed
}
