import { useQuery } from '@tanstack/react-query'
import { useDashboard, useModule } from '../context.tsx'
import { createCommandsApi } from './api.ts'

/**
 * Why the server refuses each command for `objects` of `kind` now, such as `Running - cancel instead`, or `null` when
 * it allows it. Each object's version is in the key, so a status change asks again. An action is offered only once
 * the server has said it can succeed now: while that answer loads, or after it failed, every command is refused with
 * a reason that says so, rather than letting the operator run what the server may refuse.
 */
export function useAllowedActions(kind: string, objects: readonly { id: string; version?: string }[]) {
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

  return (commandId: string | undefined): string | null => {
    if (!commandId) {
      return null
    }

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
