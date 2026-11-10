// The React Compiler's memo caches cost the SDK more bytes than these rare renders save, and the shell's assets sit
// near their size budget.
'use no memo'

import { useRef, useState } from 'react'
import { Button } from '../components/button.tsx'
import { Text } from '../components/display/text.tsx'
import { useSelectionCommand, useViewKeys } from '../components/keymap.tsx'
import { Announcer } from '../components/live.tsx'
import { Mono } from '../components/marks.tsx'
import { useDashboard } from '../context.tsx'
import { DashboardApiError } from '../data/api.ts'
import { ActionConfirm, problemsClass, type ConfirmedAction } from './action-confirm.tsx'
import { createCommandsApi, effectiveConfirmation, type CommandDefinition, type RowResult } from './api.ts'

export interface CommandActionProps {
  module: string
  command: CommandDefinition
  ids: readonly string[]
  /** What the rows are, for the button and toast text: `runs`, `messages`. */
  noun: string
  /**
   * Why the server refuses the command now, from the allowed-now answer (`useAllowedActions(...)(commandId)`): set when
   * it refuses any row, so the action never sends a refused row. Ignored when `refused` is given.
   */
  disabledReason?: string | null
  /**
   * The rows the server refuses now, each with its reason (`useAllowedActions(...).refused`). The action then runs on
   * the rest and reads `Requeue 3 of 5`, and is disabled only when every row is refused; the refused rows join the
   * result as refused.
   */
  refused?: ReadonlyMap<string, string>
  /** The rows the last run did not change, each with its reason, for the list to tag in their rows. */
  onRefused?: (reasons: ReadonlyMap<string, string>) => void
  /** Shows the refused rows in the list; the result toast offers it as `View`. */
  onViewRefused?: () => void
  /** The module's other commands, to find the inverse that Undo calls. */
  commands?: readonly CommandDefinition[]
  variant?: 'primary' | 'secondary' | 'danger'
  /** This is the selection's recommended recovery command: `R` runs it, or announces why the server refuses it. */
  recovery?: boolean
  onDone?: (rows: RowResult[]) => void
}

/** `Requeue: 3 done · 2 refused`: the command's own label, uninflected, then each kind of row. */
function summary(label: string, rows: readonly RowResult[]) {
  const counts = { done: 0, skipped: 0, failed: 0 }
  rows.forEach((row) => counts[row.outcome]++)

  return `${label}: ${counts.done} done${counts.skipped ? ` · ${counts.skipped} refused` : ''}${counts.failed ? ` · ${counts.failed} failed` : ''}`
}

/** The rows an action was pressed on, so the confirm and the run name the rows the operator saw, not later ones. */
interface Pressed {
  key: string
  ids: readonly string[]
  checked: readonly string[]
  refused?: ReadonlyMap<string, string>
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
    <div role="alert" className="grid basis-full gap-1 wrap-anywhere">
      {outcome.error ? (
        <Text as="p" tone="error">
          {outcome.error}
        </Text>
      ) : null}
      {refused.length > 0 ? (
        <>
          <Text as="p" tone="hint">{`${outcome.label}: not changed`}</Text>
          {/* The same list the Operation panel shows, so a run over ids and an Operation report refusals alike. */}
          <ul className={problemsClass} aria-label="Rows not changed">
            {refused.map((row) => (
              <li key={row.id}>
                <Mono>{row.id}</Mono>
                <span>{row.outcome === 'failed' ? 'Failed' : 'Refused'}</span>
                <Text tone="hint">{row.reason ?? ''}</Text>
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
export function CommandAction({
  module,
  command,
  ids: checked,
  noun,
  disabledReason,
  refused,
  onRefused,
  onViewRefused,
  commands,
  variant,
  recovery,
  onDone,
}: CommandActionProps) {
  const { api, toasts } = useDashboard()
  // Set from the press until the run answers or the confirm closes: the rows the operator pressed on.
  const [pressed, setPressed] = useState<Pressed | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | undefined>()
  const [outcome, setOutcome] = useState<RunOutcome | undefined>()
  const [announced, setAnnounced] = useState('')
  const selection = checked.join('\n')
  // The selection now, read after a run answers: its refusals belong only to the rows it was pressed on.
  const current = useRef(selection)
  current.current = selection
  const live: Pressed = { key: selection, ids: refused ? checked.filter((id) => !refused.has(id)) : checked, checked, refused }
  const { ids } = live
  const at = pressed ?? live
  const shownOutcome = outcome?.key === selection ? outcome : undefined

  const confirmation = effectiveConfirmation(command, at.ids.length)
  const needsInput = Object.keys(command.arguments.properties).length > 0 || command.requiresReason
  // Per-row refusals disable the action only when they cover every row. Without them, `disabledReason` disables it on
  // any refused row, so a caller that cannot leave refused rows out never sends one.
  const reasons = refused && ids.length === 0 ? checked.map((id) => refused.get(id)) : []
  const refusal = !command.canRun
    ? (command.disabledReason ?? 'Needs operate permission')
    : refused
      ? reasons.length > 0
        ? reasons.every((reason) => reason === reasons[0])
          ? reasons[0]
          : 'None of these can run now'
        : null
      : disabledReason
  // Every row refused: the button keeps naming what was checked, disabled with the reason beside it.
  const label =
    at.ids.length > 0 && at.ids.length < at.checked.length
      ? `${command.label} ${at.ids.length} of ${at.checked.length}`
      : at.checked.length > 1
        ? `${command.label} ${at.checked.length} ${noun}`
        : command.label
  // The confirm names its effect even for one row: a bare verb takes its object (`Cancel run`, never `Cancel` beside
  // the dismiss button), while a label that already says it (`Add note`) stays.
  const effect = at.ids.length > 1 || label.includes(' ') ? label : `${label} ${noun}`
  const client = createCommandsApi(api, module)

  function press() {
    // `R` and the palette call this directly, so a run in flight or an open confirmation does not start another, and
    // an empty set never sends.
    if (busy || pressed || ids.length === 0) {
      return
    }

    if (confirmation === 'none' && !needsInput) {
      void run({ arguments: {} }, live)
    } else {
      setPressed(live)
    }
  }

  // `R` and the palette go through the same refusal as the button, so a refused command stays refused; `R` announces
  // it even when every checked row is refused.
  useViewKeys({ recover: () => (refusal ? setAnnounced(refusal) : press()) }, recovery === true && checked.length > 0)
  useSelectionCommand(refusal || busy || ids.length === 0 ? null : { id: command.id, label, run: press })

  async function run(action: ConfirmedAction, pressedRows: Pressed) {
    setBusy(true)
    setError(undefined)
    setOutcome(undefined)

    try {
      const ran = await client.run(command.id, {
        ids: pressedRows.ids,
        arguments: action.arguments,
        reason: action.reason,
        confirmed: confirmation !== 'none',
        typedCount: action.typedCount,
      })
      // Refused before the run, so never sent; the result counts them with the rows the server refused.
      const rows = [
        ...pressedRows.checked
          .filter((id) => !pressedRows.ids.includes(id))
          .map((id): RowResult => ({ id, outcome: 'skipped', reason: pressedRows.refused?.get(id) })),
        ...ran,
      ]
      const missed = rows.filter((row) => row.outcome !== 'done')
      const inverse = command.inverseCommandId ? commands?.find((candidate) => candidate.id === command.inverseCommandId) : undefined
      const undone = rows.filter((row) => row.outcome === 'done').map((row) => row.id)
      const key = pressedRows.key
      // A selection changed while the run was in flight has cleared its refusals; the old ones must not come back.
      const same = current.current === key

      setPressed(null)
      setOutcome({ key, label: command.label, rows })

      if (same) {
        onRefused?.(new Map(missed.map((row) => [row.id, row.reason ?? (row.outcome === 'failed' ? 'Failed' : 'Refused')])))
      }

      toasts.show({
        message: summary(command.label, rows),
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
                      toasts.show({ message: summary(undoLabel, undoRows) })
                      setOutcome({ key, label: undoLabel, rows: undoRows })
                    })
                    // The toast outlives this button, so the failure goes to a toast as well. The inline copy is keyed
                    // to the selection, so it shows only while the same rows are still selected.
                    .catch((undoError: unknown) => {
                      const message = undoError instanceof DashboardApiError ? undoError.message : 'Undo failed. Try again.'
                      toasts.show({ message })
                      setOutcome({ key, label: undoLabel, rows: [], error: message })
                    })
                },
              }
            : same && missed.length > 0 && onViewRefused
              ? { label: 'View', onAction: onViewRefused }
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
        isPending={busy && !pressed}
        onPress={press}
      >
        {label}
      </Button>
      {recovery ? <Announcer message={announced} /> : null}
      {error && !pressed ? (
        <Text tone="error" role="alert">
          {error}
        </Text>
      ) : null}
      {shownOutcome && !pressed ? <RowOutcomes outcome={shownOutcome} /> : null}
      <ActionConfirm
        isOpen={pressed !== null}
        title={effect}
        confirmLabel={effect}
        confirmation={confirmation}
        count={at.ids.length}
        reason={command.requiresReason ? 'required' : 'optional'}
        schema={command.arguments}
        isBusy={busy}
        error={error}
        tone={variant === 'danger' ? 'danger' : undefined}
        onCancel={() => {
          setPressed(null)
          setError(undefined)
        }}
        onConfirm={(action) => void run(action, pressed!)}
      />
    </>
  )
}
