import { DashboardApiError } from '../data/api.ts'
import type { CommandsApi, OperationScope, RowResult } from './api.ts'

interface OperationTally {
  /** The rows the confirm step counted. */
  total: number
  done: number
  skipped: number
  failed: number
  /** The skipped and failed rows with their reasons, kept until the operator dismisses the panel. */
  problems: RowResult[]
}

/** Where an Operation stands; every state carries the tally of the chunks answered so far. */
export type OperationState =
  | ({ status: 'running' } & OperationTally)
  | ({ status: 'completed' } & OperationTally)
  | ({ status: 'stopped' } & OperationTally)
  | ({ status: 'failed'; error: string } & OperationTally)

export interface OperationRun {
  getState(): OperationState
  subscribe(listener: () => void): () => void
  /** Stops between chunks: the chunk in flight finishes, and no further chunk is sent. */
  stop(): void
  /** Settles when the run ends, however it ends. */
  finished: Promise<OperationState>
}

export interface OperationRunOptions {
  commands: CommandsApi
  commandId: string
  scope: OperationScope
  /** How long the tab waits between chunks, so an Operation never floods the store. */
  chunkDelayMs?: number
  /** Where the run warns before the tab closes; the window in a browser. */
  unloadTarget?: Pick<Window, 'addEventListener' | 'removeEventListener'>
}

function reasonOf(error: unknown) {
  if (error instanceof DashboardApiError) {
    return error.message
  }

  return error instanceof Error && error.message ? error.message : 'The chunk failed. Run the Operation again.'
}

/**
 * Runs an Operation chunk by chunk from this tab, as the confirm step froze it: each chunk sends the signed scope
 * and the previous cursor, and the run ends when no cursor is left, on Stop, or on the first refused chunk. While it
 * runs, closing the tab asks for confirmation first.
 */
export function startOperation(options: OperationRunOptions): OperationRun {
  const { commands, commandId, scope } = options
  const delay = options.chunkDelayMs ?? 250
  const unloadTarget = options.unloadTarget ?? (typeof window === 'undefined' ? undefined : window)
  const listeners = new Set<() => void>()

  let state: OperationState = { status: 'running', total: scope.count, done: 0, skipped: 0, failed: 0, problems: [] }
  let stopRequested = false

  function set(next: OperationState) {
    state = next
    listeners.forEach((listener) => listener())
  }

  function warnBeforeUnload(event: Event) {
    event.preventDefault()
  }

  async function run(): Promise<OperationState> {
    let cursor: string | undefined

    for (;;) {
      if (stopRequested) {
        set({ ...state, status: 'stopped' })
        break
      }

      let chunk

      try {
        chunk = await commands.chunk(commandId, {
          token: scope.token,
          cursor,
          confirmed: true,
          typedCount: scope.confirmation === 'typed' ? scope.count : undefined,
        })
      } catch (error) {
        set({ ...state, status: 'failed', error: reasonOf(error) })
        break
      }

      const tally = { ...state }

      for (const row of chunk.rows) {
        tally[row.outcome]++
      }

      tally.problems = [...state.problems, ...chunk.rows.filter((row) => row.outcome !== 'done')]
      cursor = chunk.nextCursor ?? undefined

      if (chunk.done || cursor === undefined) {
        set({ ...tally, status: 'completed' })
        break
      }

      set({ ...tally, status: 'running' })

      if (!stopRequested) {
        await new Promise((resolve) => setTimeout(resolve, delay))
      }
    }

    unloadTarget?.removeEventListener('beforeunload', warnBeforeUnload)

    return state
  }

  unloadTarget?.addEventListener('beforeunload', warnBeforeUnload)

  return {
    getState: () => state,
    subscribe(listener) {
      listeners.add(listener)

      return () => {
        listeners.delete(listener)
      }
    },
    stop() {
      stopRequested = true
    },
    finished: run(),
  }
}
