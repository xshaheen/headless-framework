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
  /** Why the server refuses the command for every row now, from the allowed-now answer. */
  disabledReason?: string | null
  /**
   * The rows the server refuses now, each with its reason (`useAllowedActions(...).refused`). The action runs on the
   * rest and reads `Requeue 3 of 5`; the refused rows join the result as refused.
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

/**
 * The command's label with its verb in the past tense, `Requeue` → `Requeued`, so the toast keeps the button's word:
 * the server names a command by its verb only.
 */
function past(label: string) {
  const [verb = '', ...rest] = label.split(' ')
  const done =
    verb === 'Run'
      ? 'Ran'
      : verb.endsWith('e')
        ? `${verb}d`
        : /[^aeiou]y$/i.test(verb)
          ? `${verb.slice(0, -1)}ied`
          : /^[^aeiou]{1,2}[aeiou][bdgmnpt]$|[^aeiou][aeiou]l$/i.test(verb)
            ? `${verb}${verb.at(-1)}ed`
            : `${verb}ed`

  return [done, ...rest].join(' ')
}

/** `Requeued 3 · 2 refused`: what changed, then each kind of row that did not. */
function summary(label: string, rows: readonly RowResult[]) {
  const counts = { done: 0, skipped: 0, failed: 0 }
  rows.forEach((row) => counts[row.outcome]++)

  return [
    `${past(label)} ${counts.done}`,
    counts.skipped > 0 ? `${counts.skipped} refused` : '',
    counts.failed > 0 ? `${counts.failed} failed` : '',
  ]
    .filter(Boolean)
    .join(' · ')
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
                <span>{row.outcome === 'failed' ? 'Failed' : 'Refused'}</span>
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
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | undefined>()
  const [outcome, setOutcome] = useState<RunOutcome | undefined>()
  const [announced, setAnnounced] = useState('')
  const ids = refused ? checked.filter((id) => !refused.has(id)) : checked
  const selection = checked.join('\n')
  const shownOutcome = outcome?.key === selection ? outcome : undefined

  const confirmation = effectiveConfirmation(command, ids.length)
  const needsInput = Object.keys(command.arguments.properties).length > 0 || command.requiresReason
  const refusal = !command.canRun
    ? (command.disabledReason ?? 'Needs operate permission')
    : (disabledReason ?? (ids.length === 0 && checked.length > 0 ? refused?.values().next().value : null))
  // Every row refused: the button keeps naming what was checked, disabled with the reason beside it.
  const label =
    ids.length > 0 && ids.length < checked.length
      ? `${command.label} ${ids.length} of ${checked.length}`
      : checked.length > 1
        ? `${command.label} ${checked.length} ${noun}`
        : command.label
  // The confirm names its effect even for one row: a bare verb takes its object (`Cancel run`, never `Cancel` beside
  // the dismiss button), while a label that already says it (`Add note`) stays.
  const effect = ids.length > 1 || label.includes(' ') ? label : `${label} ${noun}`
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
      const ran = await client.run(command.id, {
        ids,
        arguments: action.arguments,
        reason: action.reason,
        confirmed: confirmation !== 'none',
        typedCount: action.typedCount,
      })
      // Refused before the run, so never sent; the result counts them with the rows the server refused.
      const rows = [
        ...checked.filter((id) => !ids.includes(id)).map((id): RowResult => ({ id, outcome: 'skipped', reason: refused?.get(id) })),
        ...ran,
      ]
      const missed = rows.filter((row) => row.outcome !== 'done')
      const inverse = command.inverseCommandId ? commands?.find((candidate) => candidate.id === command.inverseCommandId) : undefined
      const undone = rows.filter((row) => row.outcome === 'done').map((row) => row.id)

      setConfirming(false)
      setOutcome({ key: selection, label: command.label, rows })
      onRefused?.(new Map(missed.map((row) => [row.id, row.reason ?? (row.outcome === 'failed' ? 'Failed' : 'Refused')])))
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
            : missed.length > 0 && onViewRefused
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
        title={effect}
        confirmLabel={effect}
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
