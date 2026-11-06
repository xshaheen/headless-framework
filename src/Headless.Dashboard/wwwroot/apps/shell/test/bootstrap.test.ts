import { describe, expect, it, vi } from 'vitest'
import {
  moduleSidebarEntries,
  readBootstrapState,
  safeReturnUrl,
  sessionMatches,
  settleSession,
  stateElementId,
  type AuthInfo,
} from '../src/bootstrap.ts'
import { moduleManifest, shellState } from './fixtures/modules.tsx'

function fakeFetch(info: AuthInfo) {
  return vi.fn<typeof fetch>(async () => new Response(JSON.stringify(info), { status: 200, headers: { 'Content-Type': 'application/json' } }))
}

function memoryStorage() {
  const items = new Map<string, string>()

  return {
    getItem: (key: string) => items.get(key) ?? null,
    setItem: (key: string, value: string) => void items.set(key, value),
    removeItem: (key: string) => void items.delete(key),
  }
}

const signedIn: AuthInfo = {
  mode: 'host',
  enabled: true,
  signInAvailable: true,
  authenticated: true,
  name: 'Mona Haddad',
  canOperate: true,
  modules: { diag: { canView: true, canOperate: true } },
}

describe('bootstrap', () => {
  it('reads the inlined page state, and refuses a block that is not the dashboard state', () => {
    document.body.innerHTML = `<script type="application/json" id="${stateElementId}">${JSON.stringify(shellState([moduleManifest('diag')]))}</script>`
    expect(readBootstrapState(document).modules[0]?.id).toBe('diag')

    document.body.innerHTML = `<script type="application/json" id="${stateElementId}">{"basePath":"/x"}</script>`
    expect(() => readBootstrapState(document)).toThrow(/does not hold the dashboard's page state/)
  })

  it('reads each product with its sidebar, and refuses a sidebar entry without a route', () => {
    const state = shellState([moduleManifest('diag')])
    document.body.innerHTML = `<script type="application/json" id="${stateElementId}">${JSON.stringify(state)}</script>`

    expect(readBootstrapState(document).products).toEqual(state.products)
    expect(moduleSidebarEntries(state, 'diag').map((entry) => entry.id)).toEqual(['diag.nav.runs', 'diag.nav.settings'])

    const broken = { ...state, products: [{ ...state.products[0], sections: [{ module: 'diag', entries: [{ id: 'x', label: 'X' }] }] }] }
    document.body.innerHTML = `<script type="application/json" id="${stateElementId}">${JSON.stringify(broken)}</script>`
    expect(() => readBootstrapState(document)).toThrow(/does not hold the dashboard's page state/)
  })

  it('reloads once when the inlined state is signed out but the server reports a session, then renders', async () => {
    const state = shellState([], { session: { authenticated: false } })
    const storage = memoryStorage()
    const reload = vi.fn()
    const fetcher = fakeFetch(signedIn)

    expect((await settleSession(state, { fetch: fetcher, storage, reload })).status).toBe('reloading')
    expect(reload).toHaveBeenCalledTimes(1)
    expect(fetcher.mock.calls[0]?.[0]).toBe('/headless/api/auth/info')

    // The reloaded page still differs: it renders instead of reloading in a loop.
    expect((await settleSession(state, { fetch: fetcher, storage, reload })).status).toBe('ready')
    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('does not reload when the inlined state matches the server', async () => {
    const reload = vi.fn()
    const state = shellState([moduleManifest('diag')])

    expect((await settleSession(state, { fetch: fakeFetch(signedIn), storage: memoryStorage(), reload })).status).toBe('ready')
    expect(reload).not.toHaveBeenCalled()
  })

  it('treats a changed operate permission as a different session', () => {
    const state = shellState([moduleManifest('diag', { canOperate: false })])

    expect(sessionMatches(state, signedIn)).toBe(false)
  })

  it('follows only return URLs under the base path', () => {
    expect(safeReturnUrl('/headless', '/headless/diag/runs?x=1')).toBe('/headless/diag/runs?x=1')
    expect(safeReturnUrl('/headless', 'https://evil.example/headless')).toBe('/headless/')
    expect(safeReturnUrl('/headless', '//evil.example')).toBe('/headless/')
    expect(safeReturnUrl('/headless', '/headless/../admin')).toBe('/headless/')
    expect(safeReturnUrl('/headless', '/admin')).toBe('/headless/')
  })
})
