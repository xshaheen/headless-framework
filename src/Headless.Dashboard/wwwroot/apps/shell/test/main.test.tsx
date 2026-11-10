import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { stateElementId } from '../src/bootstrap.ts'
import { moduleManifest, shellState } from './fixtures/modules.tsx'

const stream = vi.hoisted(() => ({ close: vi.fn(), signOut: vi.fn(), scope: '' }))

vi.mock('@headless/dashboard-sdk/stream', () => ({
  createStreamClient: (options: { scope: string }) => {
    stream.scope = options.scope
    return stream
  },
}))

// The page's lifecycle is under test, not the shell it renders.
vi.mock('../src/app.tsx', () => ({
  ShellApp: () => null,
  withSessionEnd: (api: unknown) => api,
}))

describe('page lifecycle', () => {
  beforeAll(async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        throw new Error('offline')
      }),
    )
    const state = document.createElement('script')
    state.id = stateElementId
    state.type = 'application/json'
    state.textContent = JSON.stringify(shellState([moduleManifest('diag')]))
    const root = document.createElement('div')
    root.id = 'root'
    document.body.append(state, root)

    // The page starts once per load, so every case reads the one start this import runs.
    await import('../src/main.tsx')
    await vi.waitFor(() => expect(stream.scope).not.toBe(''))
  })

  afterAll(() => {
    vi.unstubAllGlobals()
    document.body.replaceChildren()
  })

  it('leaves the shared stream when the tab goes away', () => {
    expect(stream.close).not.toHaveBeenCalled()

    window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: false }))

    expect(stream.close).toHaveBeenCalledTimes(1)
  })

  it('shares the stream only with tabs of the same user, under a scope that names no user', () => {
    expect(stream.scope).toMatch(/^headless-dashboard:\/headless:[0-9a-f]{24}$/)
  })
})
