/** The id of the page-state block the server writes into the document. */
export const stateElementId = 'headless-dashboard-state'

export interface BootstrapRoute {
  path: string
  /** The route chunk's absolute URL. */
  chunk?: string
}

export interface BootstrapNavEntry {
  id: string
  label: string
  to: string
  requires?: 'view' | 'operate'
}

export interface BootstrapRefusal {
  builtAgainst?: string
  shellSdkVersion: string
  message: string
}

export interface BootstrapModule {
  id: string
  displayName: string
  canOperate: boolean
  entry?: string
  css: string[]
  nav: BootstrapNavEntry[]
  routes: BootstrapRoute[]
  commands: { id: string; label: string; requires?: 'view' | 'operate' }[]
  events: string[]
  refusal?: BootstrapRefusal
}

/** The page state the server inlines, so the first render needs no API round trip. */
export interface BootstrapState {
  basePath: string
  apiBase: string
  streamUrl: string
  sdkVersion: string
  authMode: 'none' | 'basic' | 'apiKey' | 'host'
  session: { authenticated: boolean; name?: string }
  traceLinkTemplate?: string
  modules: BootstrapModule[]
}

function isBootstrapState(value: unknown): value is BootstrapState {
  if (typeof value !== 'object' || value === null) {
    return false
  }

  const state = value as Record<string, unknown>
  const session = state.session as Record<string, unknown> | null

  return (
    typeof state.basePath === 'string' &&
    typeof state.apiBase === 'string' &&
    typeof state.sdkVersion === 'string' &&
    typeof session === 'object' &&
    session !== null &&
    typeof session.authenticated === 'boolean' &&
    Array.isArray(state.modules) &&
    state.modules.every(
      (module: unknown) =>
        typeof module === 'object' &&
        module !== null &&
        typeof (module as BootstrapModule).id === 'string' &&
        Array.isArray((module as BootstrapModule).nav) &&
        Array.isArray((module as BootstrapModule).routes),
    )
  )
}

/** Reads the page state the server wrote into `document`. */
export function readBootstrapState(document: Document): BootstrapState {
  const text = document.getElementById(stateElementId)?.textContent

  if (!text) {
    throw new Error(`The page has no #${stateElementId} block; the dashboard's server renders this page.`)
  }

  const value: unknown = JSON.parse(text)

  if (!isBootstrapState(value)) {
    throw new Error(`The #${stateElementId} block does not hold the dashboard's page state.`)
  }

  return value
}
