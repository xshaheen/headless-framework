import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { CommandsApi, OperationChunk, OperationChunkCall, OperationScope } from '../src/commands/api.ts'
import { startOperation } from '../src/commands/operation-runner.ts'
import { DashboardApiError } from '../src/data/api.ts'

const scope: OperationScope = {
  operationId: 'op1',
  count: 5,
  confirmation: 'typed',
  sample: [],
  token: 'signed',
  expiresAt: '2026-11-05T11:00:00Z',
  chunkSize: 2,
}

function commandsAnswering(chunk: (call: OperationChunkCall) => Promise<OperationChunk>) {
  const calls: OperationChunkCall[] = []
  const commands: CommandsApi = {
    module: 'diag',
    list: vi.fn(),
    allowed: vi.fn(),
    run: vi.fn(),
    confirm: vi.fn(),
    chunk: (_id, call) => {
      calls.push(call)

      return chunk(call)
    },
  }

  return { commands, calls }
}

describe('operation runner', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('sends chunks until no cursor is left and reports done, skipped, and failed counts', async () => {
    const answers: OperationChunk[] = [
      { operationId: 'op1', rows: [{ id: 'a', outcome: 'done' }, { id: 'b', outcome: 'done' }], nextCursor: 'b', done: false },
      { operationId: 'op1', rows: [{ id: 'c', outcome: 'skipped', reason: 'no longer failed' }, { id: 'd', outcome: 'done' }], nextCursor: 'd', done: false },
      { operationId: 'op1', rows: [{ id: 'e', outcome: 'failed', reason: 'store timeout' }], done: true },
    ]
    const { commands, calls } = commandsAnswering(async () => answers.shift()!)

    const run = startOperation({ commands, commandId: 'requeue', scope, chunkDelayMs: 100, unloadTarget: window })
    await vi.runAllTimersAsync()
    const state = await run.finished

    expect(calls.map((call) => call.cursor)).toEqual([undefined, 'b', 'd'])
    expect(calls.every((call) => call.token === 'signed' && call.confirmed && call.typedCount === 5)).toBe(true)
    expect(state).toMatchObject({ status: 'completed', total: 5, done: 3, skipped: 1, failed: 1 })
    expect(state.problems).toEqual([
      { id: 'c', outcome: 'skipped', reason: 'no longer failed' },
      { id: 'e', outcome: 'failed', reason: 'store timeout' },
    ])
  })

  it('sends nothing further after Stop between chunks', async () => {
    let release: (chunk: OperationChunk) => void = () => undefined
    const { commands, calls } = commandsAnswering(
      () => new Promise<OperationChunk>((resolve) => (release = resolve)),
    )

    const run = startOperation({ commands, commandId: 'requeue', scope, chunkDelayMs: 100, unloadTarget: window })
    run.stop()
    release({ operationId: 'op1', rows: [{ id: 'a', outcome: 'done' }], nextCursor: 'a', done: false })
    await vi.runAllTimersAsync()
    const state = await run.finished

    expect(calls).toHaveLength(1)
    expect(state).toMatchObject({ status: 'stopped', done: 1 })
  })

  it('ends as Failed with the reason when a chunk answers 403', async () => {
    const { commands } = commandsAnswering(async () => {
      throw new DashboardApiError(403, 'Needs the jobs.requeue capability')
    })

    const run = startOperation({ commands, commandId: 'requeue', scope, unloadTarget: window })
    const state = await run.finished

    expect(state).toMatchObject({ status: 'failed', error: 'Needs the jobs.requeue capability', done: 0 })
  })

  it('warns before the tab closes while it runs, and not after', async () => {
    let release: (chunk: OperationChunk) => void = () => undefined
    const { commands } = commandsAnswering(() => new Promise<OperationChunk>((resolve) => (release = resolve)))
    const run = startOperation({ commands, commandId: 'requeue', scope, unloadTarget: window })

    const during = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(during)
    expect(during.defaultPrevented).toBe(true)

    release({ operationId: 'op1', rows: [], done: true })
    await run.finished

    const after = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(after)
    expect(after.defaultPrevented).toBe(false)
  })
})
