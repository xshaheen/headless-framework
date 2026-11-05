import { useState } from 'react'
import { Button } from '../components/button.tsx'
import { useDashboard } from '../context.tsx'
import { DashboardApiError } from '../data/api.ts'
import { ActionConfirm, type ConfirmedAction } from './action-confirm.tsx'
import { createCommandsApi, effectiveConfirmation, type CommandDefinition, type RowResult } from './api.ts'

export interface CommandActionProps {
  module: string
  command: CommandDefinition
  ids: readonly string[]
  /** What the rows are, for the button and toast text: `runs`, `messages`. */
  noun: string
  /** Why the server refuses the command for these rows now, from the allowed-now answer. */
  disabledReason?: string | null
  /** The module's other commands, to find the inverse that Undo calls. */
  commands?: readonly CommandDefinition[]
  variant?: 'primary' | 'secondary' | 'danger'
  onDone?: (rows: RowResult[]) => void
}

function summary(label: string, noun: string, rows: readonly RowResult[]) {
  const done = rows.filter((row) => row.outcome === 'done').length
  const failed = rows.filter((row) => row.outcome === 'failed').length
  const skipped = rows.length - done - failed
  const parts = [`${label}: ${done} ${noun} done`]

  if (skipped > 0) {
    parts.push(`${skipped} skipped`)
  }

  if (failed > 0) {
    parts.push(`${failed} failed`)
  }

  return parts.join(', ')
}

/**
 * Runs a server command on the selected rows. A command with an inverse runs at once and its toast offers Undo; any
 * other confirmed command opens its confirmation, typed past 100 rows. A command the user or the row's state does
 * not allow renders disabled with the server's reason.
 */
export function CommandAction({ module, command, ids, noun, disabledReason, commands, variant, onDone }: CommandActionProps) {
  const { api, toasts } = useDashboard()
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | undefined>()

  const confirmation = effectiveConfirmation(command, ids.length)
  const needsInput = Object.keys(command.arguments.properties).length > 0 || command.requiresReason
  const refusal = !command.canRun ? (command.disabledReason ?? 'Needs operate permission') : disabledReason
  const label = ids.length > 1 ? `${command.label} ${ids.length} ${noun}` : command.label
  const client = createCommandsApi(api, module)

  async function run(action: ConfirmedAction) {
    setBusy(true)
    setError(undefined)

    try {
      const rows = await client.run(command.id, {
        ids,
        arguments: action.arguments,
        reason: action.reason,
        confirmed: confirmation !== 'none',
        typedCount: action.typedCount,
      })
      const inverse = command.inverseCommandId ? commands?.find((candidate) => candidate.id === command.inverseCommandId) : undefined
      const undone = rows.filter((row) => row.outcome === 'done').map((row) => row.id)

      setConfirming(false)
      toasts.show({
        message: summary(command.label, noun, rows),
        action:
          command.inverseCommandId && undone.length > 0
            ? {
                label: 'Undo',
                onAction: () => {
                  // The inverse may require a reason; Undo states its own rather than asking the operator again.
                  void client
                    .run(command.inverseCommandId!, { ids: undone, confirmed: true, reason: `Undo of ${command.label}` })
                    .then((undoRows) => toasts.show({ message: summary(inverse?.label ?? 'Undo', noun, undoRows) }))
                    // The toast outlives this button, so the failure goes to a toast too, not into state that may
                    // belong to an unmounted or reselected component.
                    .catch((undoError: unknown) =>
                      toasts.show({
                        message: undoError instanceof DashboardApiError ? undoError.message : 'Undo failed. Try again.',
                      }),
                    )
                },
              }
            : undefined,
      })
      onDone?.(rows)
    } catch (caught) {
      setError(caught instanceof DashboardApiError ? caught.message : `${command.label} failed. Try again.`)
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <Button
        variant={variant}
        disabledReason={refusal}
        isPending={busy && !confirming}
        onPress={() => {
          if (confirmation === 'none' && !needsInput) {
            void run({ arguments: {} })
          } else {
            setConfirming(true)
          }
        }}
      >
        {label}
      </Button>
      {error && !confirming ? (
        <span role="alert" className="hd-error">
          {error}
        </span>
      ) : null}
      <ActionConfirm
        isOpen={confirming}
        title={label}
        confirmLabel={label}
        confirmation={confirmation}
        count={ids.length}
        reason={command.requiresReason ? 'required' : 'optional'}
        schema={command.arguments}
        isBusy={busy}
        error={error}
        tone={variant === 'danger' ? 'danger' : undefined}
        onCancel={() => {
          setConfirming(false)
          setError(undefined)
        }}
        onConfirm={(action) => void run(action)}
      />
    </>
  )
}
