import { useState } from 'react'
import { Button } from '../components/button.tsx'
import { useSelectionCommand, useViewKeys } from '../components/keymap.tsx'
import { Announcer } from '../components/live.tsx'
import { Mono } from '../components/marks.tsx'
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
  /** This is the selection's recommended recovery command: `R` runs it, or announces why the server refuses it. */
  recovery?: boolean
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

/** The outcome of the last run on one selection: the rows the server did not change, each with its reason. */
interface RunOutcome {
  /** The selection it belongs to, so a reselected inspector never shows another selection's refusals. */
  key: string
  label: string
  rows: RowResult[]
  error?: string
}

function RowOutcomes({ outcome }: { outcome: RunOutcome }) {
  const refused = outcome.rows.filter((row) => row.outcome !== 'done')

  if (refused.length === 0 && !outcome.error) {
    return null
  }

  return (
    <div role="alert" className="hd-outcomes">
      {outcome.error ? <p className="hd-error">{outcome.error}</p> : null}
      {refused.length > 0 ? (
        <>
          <p className="hd-hint">{outcome.label}: not changed</p>
          {/* The same list the Operation panel shows, so a run over ids and an Operation report refusals alike. */}
          <ul className="hd-operation-problems" aria-label="Rows not changed">
            {refused.map((row) => (
              <li key={row.id}>
                <Mono>{row.id}</Mono>
                <span>{row.outcome === 'failed' ? 'Failed' : 'Skipped'}</span>
                <span className="hd-hint">{row.reason ?? ''}</span>
              </li>
            ))}
          </ul>
        </>
      ) : null}
    </div>
  )
}

/**
 * Runs a server command on the selected rows. A command with an inverse runs at once and its toast offers Undo; any
 * other confirmed command opens its confirmation, typed past 100 rows. A command the user or the row's state does
 * not allow renders disabled with the server's reason.
 */
export function CommandAction({ module, command, ids, noun, disabledReason, commands, variant, recovery, onDone }: CommandActionProps) {
  const { api, toasts } = useDashboard()
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | undefined>()
  const [outcome, setOutcome] = useState<RunOutcome | undefined>()
  const [announced, setAnnounced] = useState('')
  const selection = ids.join('\n')
  const shownOutcome = outcome?.key === selection ? outcome : undefined

  const confirmation = effectiveConfirmation(command, ids.length)
  const needsInput = Object.keys(command.arguments.properties).length > 0 || command.requiresReason
  const refusal = !command.canRun ? (command.disabledReason ?? 'Needs operate permission') : disabledReason
  const label = ids.length > 1 ? `${command.label} ${ids.length} ${noun}` : command.label
  const client = createCommandsApi(api, module)

  function press() {
    // `R` and the palette call this directly, so a run in flight or an open confirmation does not start another.
    if (busy || confirming) {
      return
    }

    if (confirmation === 'none' && !needsInput) {
      void run({ arguments: {} })
    } else {
      setConfirming(true)
    }
  }

  // `R` and the palette go through the same refusal as the button, so a refused command stays refused.
  useViewKeys({ recover: () => (refusal ? setAnnounced(refusal) : press()) }, recovery === true && ids.length > 0)
  useSelectionCommand(refusal || busy || ids.length === 0 ? null : { id: command.id, label, run: press })

  async function run(action: ConfirmedAction) {
    setBusy(true)
    setError(undefined)
    setOutcome(undefined)

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
      setOutcome({ key: selection, label: command.label, rows })
      toasts.show({
        message: summary(command.label, noun, rows),
        action:
          command.inverseCommandId && undone.length > 0
            ? {
                label: 'Undo',
                onAction: () => {
                  const undoLabel = inverse?.label ?? 'Undo'

                  // The inverse may require a reason; Undo states its own rather than asking the operator again.
                  void client
                    .run(command.inverseCommandId!, { ids: undone, confirmed: true, reason: `Undo of ${command.label}` })
                    .then((undoRows) => {
                      toasts.show({ message: summary(undoLabel, noun, undoRows) })
                      setOutcome({ key: selection, label: undoLabel, rows: undoRows })
                    })
                    // The toast outlives this button, so the failure goes to a toast as well. The inline copy is keyed
                    // to the selection, so it shows only while the same rows are still selected.
                    .catch((undoError: unknown) => {
                      const message = undoError instanceof DashboardApiError ? undoError.message : 'Undo failed. Try again.'
                      toasts.show({ message })
                      setOutcome({ key: selection, label: undoLabel, rows: [], error: message })
                    })
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
        onPress={press}
      >
        {label}
      </Button>
      {recovery ? <Announcer message={announced} /> : null}
      {error && !confirming ? (
        <span role="alert" className="hd-error">
          {error}
        </span>
      ) : null}
      {shownOutcome && !confirming ? <RowOutcomes outcome={shownOutcome} /> : null}
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
