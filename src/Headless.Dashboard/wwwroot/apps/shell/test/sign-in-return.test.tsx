import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { stateElementId, type BootstrapState } from '../src/bootstrap.ts'
import { moduleManifest, shellState } from './fixtures/modules.tsx'

vi.mock('@headless/dashboard-sdk/stream', () => ({
  createStreamClient: () => ({ close: vi.fn(), signOut: vi.fn() }),
}))

// The page's addresses are under test, not the pages it renders.
vi.mock('../src/app.tsx', () => ({
  ShellApp: () => null,
  withSessionEnd: (api: unknown) => api,
}))
vi.mock('../src/views/sign-in.tsx', () => ({ SignInPage: () => null }))

/** Loads the page once, as a browser does on each navigation, with `state` inlined. */
async function load(state: BootstrapState) {
  vi.resetModules()
  document.body.replaceChildren()
  const script = document.createElement('script')
  script.id = stateElementId
  script.type = 'application/json'
  script.textContent = JSON.stringify(state)
  const root = document.createElement('div')
  root.id = 'root'
  document.body.append(script, root)

  await import('../src/main.tsx')
}

describe('signing in from a trace link', () => {
  beforeAll(() => {
    // No answer from the server: the page trusts its inlined state.
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        throw new Error('offline')
      }),
    )
  })

  afterAll(() => {
    vi.unstubAllGlobals()
    document.body.replaceChildren()
    sessionStorage.clear()
  })

  it('keeps the id out of the return URL and lands on the trace of that id after sign-in', async () => {
    const modules = [moduleManifest('jobs')]
    history.replaceState(null, '', '/headless/trace#ord-7')

    // Signed out: the page moves to sign-in, returning to the trace.
    await load(shellState(modules, { session: { authenticated: false } }))
    await vi.waitFor(() => expect(location.pathname).toBe('/headless/sign-in'))

    const returnUrl = new URLSearchParams(location.search).get('returnUrl')
    expect(returnUrl).toBe('/headless/trace')
    expect(location.href).not.toContain('ord-7')

    // The sign-in page sends the browser to the return URL, which loads the page again signed in.
    history.replaceState(null, '', returnUrl)
    await load(shellState(modules))

    await vi.waitFor(() => expect(location.hash).toBe('#ord-7'))
    expect(location.pathname).toBe('/headless/trace')
  })
})
