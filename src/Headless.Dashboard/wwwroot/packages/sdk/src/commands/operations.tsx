import { createContext, useContext, useState, useSyncExternalStore, type ReactNode } from 'react'
import { Button } from '../components/button.tsx'
import { ProgressBar } from '../components/live.tsx'
import { useDashboard } from '../context.tsx'
import { DashboardApiError } from '../data/api.ts'
import { ActionConfirm, type ConfirmedAction } from './action-confirm.tsx'
import { createCommandsApi, type CommandDefinition, type OperationScope } from './api.ts'
import { startOperation, type OperationRun, type OperationState } from './operation-runner.ts'

/** What an Operation acts on: one command of one module over a filter. */
export interface OperationRequest {
  module: string
  command: CommandDefinition
  filter: unknown
  /** What the rows are, for the confirm and progress text: `failed runs of billing.charge`. */
  noun: string
}

interface ConfirmStep {
  request: OperationRequest
  /** Absent until the operator gave the reason and arguments and the server counted the scope. */
  scope?: { scope: OperationScope; reason?: string }
  busy: boolean
  error?: string
}

interface ActiveOperation {
  key: number
  request: OperationRequest
  run: OperationRun
}

interface OperationsValue {
  /** Starts an Operation: confirm its scope, then run it chunk by chunk with progress and Stop. */
  start(request: OperationRequest): void
}

const OperationsContext = createContext<OperationsValue | null>(null)

export interface OperationHostProps {
  children: ReactNode
  /** How long the tab waits between chunks. */
  chunkDelayMs?: number
}

/**
 * Hosts Operations in the shell's layout, outside any route, so an Operation keeps running across in-app navigation
 * and its progress and Stop show on every view.
 */
export function OperationHost({ children, chunkDelayMs }: OperationHostProps) {
  const { api } = useDashboard()
  const [confirm, setConfirm] = useState<ConfirmStep | null>(null)
  const [active, setActive] = useState<ActiveOperation[]>([])
  const [nextKey, setNextKey] = useState(1)

  function start(request: OperationRequest) {
    setConfirm({ request, busy: false })
  }

  async function countScope(step: ConfirmStep, action: ConfirmedAction) {
    setConfirm({ ...step, busy: true, error: undefined })

    try {
      const scope = await createCommandsApi(api, step.request.module).confirm(step.request.command.id, {
        filter: step.request.filter,
        arguments: action.arguments,
        reason: action.reason,
      })
      setConfirm({ ...step, busy: false, scope: { scope, reason: action.reason } })
    } catch (error) {
      setConfirm({ ...step, busy: false, error: error instanceof DashboardApiError ? error.message : 'Counting the rows failed. Try again.' })
    }
  }

  function run(step: ConfirmStep & { scope: NonNullable<ConfirmStep['scope']> }) {
    const operation = startOperation({
      commands: createCommandsApi(api, step.request.module),
      commandId: step.request.command.id,
      scope: step.scope.scope,
      chunkDelayMs,
    })

    setActive((current) => [...current, { key: nextKey, request: step.request, run: operation }])
    setNextKey((key) => key + 1)
    setConfirm(null)
  }

  const { command, noun } = confirm?.request ?? { command: undefined, noun: '' }
  const scope = confirm?.scope?.scope

  return (
    <OperationsContext.Provider value={{ start }}>
      {active.length > 0 ? (
        <section className="hd-operations" aria-label="Operations">
          {active.map((operation) => (
            <OperationPanel
              key={operation.key}
              label={`${operation.request.command.label} ${operation.request.noun}`}
              run={operation.run}
              onDismiss={() => setActive((current) => current.filter((item) => item.key !== operation.key))}
              onRunAgain={() => {
                setActive((current) => current.filter((item) => item.key !== operation.key))
                start(operation.request)
              }}
            />
          ))}
        </section>
      ) : null}
      {children}
      {confirm && command ? (
        scope ? (
          <ActionConfirm
            isOpen
            title={`${command.label} ${scope.count} ${noun}`}
            reach={`Runs in chunks of ${scope.chunkSize} from this tab. Rows that arrive after now are not touched.`}
            confirmLabel={`${command.label} ${scope.count} ${noun}`}
            confirmation={scope.confirmation === 'none' ? 'confirm' : scope.confirmation}
            count={scope.count}
            sample={scope.sample}
            isBusy={confirm.busy}
            error={confirm.error}
            onCancel={() => setConfirm(null)}
            onConfirm={() => run({ ...confirm, scope: confirm.scope! })}
          />
        ) : (
          <ActionConfirm
            isOpen
            title={`${command.label} ${noun}`}
            reach="The next step counts the rows the filter matches now, and shows a sample, before anything runs."
            confirmLabel="Count rows"
            confirmation="none"
            reason="required"
            schema={command.arguments}
            isBusy={confirm.busy}
            error={confirm.error}
            onCancel={() => setConfirm(null)}
            onConfirm={(action) => void countScope(confirm, action)}
          />
        )
      ) : null}
    </OperationsContext.Provider>
  )
}

/** Starts Operations through the shell's host. */
export function useOperations(): OperationsValue {
  const value = useContext(OperationsContext)

  if (!value) {
    throw new Error('useOperations must be called inside <OperationHost>; the shell layout and renderModule provide it.')
  }

  return value
}

const endText: Record<Exclude<OperationState['status'], 'running'>, string> = {
  completed: 'Completed',
  stopped: 'Stopped',
  failed: 'Failed',
}

export interface OperationPanelProps {
  label: string
  run: OperationRun
  onDismiss: () => void
  onRunAgain: () => void
}

/**
 * An Operation's progress, from the chunk answers alone, with Stop while it runs. When it ends, the skipped and
 * failed rows stay listed with their reasons until dismissed, and `Run again` confirms the remaining rows anew.
 */
export function OperationPanel({ label, run, onDismiss, onRunAgain }: OperationPanelProps) {
  const state = useSyncExternalStore(run.subscribe, run.getState)
  const processed = state.done + state.skipped + state.failed
  const counts = `${processed} of ${state.total} rows · ${state.done} done · ${state.skipped} skipped · ${state.failed} failed`

  return (
    <div className="hd-operation" role="group" aria-label={label}>
      <div className="hd-fields">
        <strong>{state.status === 'running' ? label : `${label}: ${endText[state.status]}`}</strong>
        <ProgressBar label={`${label} progress`} value={processed} max={state.total} valueText={counts} />
        {state.status === 'failed' ? (
          <p role="alert" className="hd-error">
            {state.error}
          </p>
        ) : null}
      </div>
      <div className="hd-actions">
        {state.status === 'running' ? (
          <Button variant="danger" onPress={() => run.stop()}>
            Stop
          </Button>
        ) : (
          <>
            <Button onPress={onRunAgain}>Run again</Button>
            <Button variant="ghost" onPress={onDismiss}>
              Dismiss
            </Button>
          </>
        )}
      </div>
      {state.problems.length > 0 && state.status !== 'running' ? (
        <ul className="hd-operation-problems" aria-label="Rows not changed">
          {state.problems.map((row) => (
            <li key={row.id}>
              <bdi className="hd-mono">{row.id}</bdi>
              <span>{row.outcome === 'failed' ? 'Failed' : 'Skipped'}</span>
              <span className="hd-hint">{row.reason ?? ''}</span>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  )
}
