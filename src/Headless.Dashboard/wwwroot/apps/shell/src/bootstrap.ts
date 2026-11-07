export const stateElementId = 'headless-dashboard-state'

export type Permission = 'view' | 'operate'

export interface BootstrapRoute {
  path: string
  /** The route chunk's absolute URL. */
  chunk?: string
}

export interface BootstrapNavEntry {
  id: string
  label: string
  to: string
  requires?: Permission | null
}

export interface BootstrapCommand {
  id: string
  label: string
  requires?: Permission | null
}

export interface BootstrapRefusal {
  builtAgainst?: string | null
  shellSdkVersion: string
  message: string
}

export interface BootstrapModule {
  id: string
  displayName: string
  canOperate: boolean
  /** The entry chunk's absolute URL; absent for a refused module. */
  entry?: string
  css: string[]
  nav: BootstrapNavEntry[]
  routes: BootstrapRoute[]
  /** The static fields of each object kind; the route to an object needs the module's entry. */
  objectKinds?: Record<string, unknown>
  commands: BootstrapCommand[]
  events: string[]
  refusal?: BootstrapRefusal
}

export type AuthMode = 'none' | 'basic' | 'apiKey' | 'host'

/** The page state the server inlines, so the first render needs no API round trip. */
export interface BootstrapState {
  basePath: string
  apiBase: string
  streamUrl: string
  sdkVersion: string
  authMode: AuthMode
  session: { authenticated: boolean; name?: string | null }
  traceLinkTemplate?: string
  modules: BootstrapModule[]
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isAuthMode(value: unknown): value is AuthMode {
  return value === 'none' || value === 'basic' || value === 'apiKey' || value === 'host'
}

function isPermission(value: unknown): value is Permission | null | undefined {
  return value === undefined || value === null || value === 'view' || value === 'operate'
}

function isNavEntry(value: unknown): value is BootstrapNavEntry {
  return (
    isRecord(value) &&
    typeof value.id === 'string' &&
    typeof value.label === 'string' &&
    typeof value.to === 'string' &&
    isPermission(value.requires)
  )
}

function isCommand(value: unknown): value is BootstrapCommand {
  return isRecord(value) && typeof value.id === 'string' && typeof value.label === 'string' && isPermission(value.requires)
}

function isRoute(value: unknown): value is BootstrapRoute {
  return isRecord(value) && typeof value.path === 'string' && (value.chunk === undefined || typeof value.chunk === 'string')
}

function isRefusal(value: unknown): value is BootstrapRefusal {
  return (
    isRecord(value) &&
    typeof value.shellSdkVersion === 'string' &&
    typeof value.message === 'string' &&
    (value.builtAgainst === undefined || value.builtAgainst === null || typeof value.builtAgainst === 'string')
  )
}

function arrayOf<T>(value: unknown, check: (item: unknown) => item is T): T[] | undefined {
  if (value === undefined || value === null) {
    return []
  }

  return Array.isArray(value) && value.every(check) ? value : undefined
}

function readModule(value: unknown): BootstrapModule | undefined {
  if (!isRecord(value) || typeof value.id !== 'string' || typeof value.displayName !== 'string') {
    return undefined
  }

  const nav = arrayOf(value.nav, isNavEntry)
  const routes = arrayOf(value.routes, isRoute)
  const commands = arrayOf(value.commands, isCommand)
  const css = arrayOf(value.css, (item): item is string => typeof item === 'string')
  const events = arrayOf(value.events, (item): item is string => typeof item === 'string')

  if (!nav || !routes || !commands || !css || !events) {
    return undefined
  }

  if (value.refusal !== undefined && value.refusal !== null && !isRefusal(value.refusal)) {
    return undefined
  }

  return {
    id: value.id,
    displayName: value.displayName,
    canOperate: value.canOperate === true,
    entry: typeof value.entry === 'string' ? value.entry : undefined,
    css,
    nav,
    routes,
    objectKinds: isRecord(value.objectKinds) ? value.objectKinds : undefined,
    commands,
    events,
    refusal: isRefusal(value.refusal) ? value.refusal : undefined,
  }
}

function readState(value: unknown): BootstrapState | undefined {
  if (!isRecord(value) || !isRecord(value.session) || !Array.isArray(value.modules)) {
    return undefined
  }

  const { basePath, apiBase, streamUrl, sdkVersion, authMode, session, traceLinkTemplate } = value

  if (
    typeof basePath !== 'string' ||
    typeof apiBase !== 'string' ||
    typeof streamUrl !== 'string' ||
    typeof sdkVersion !== 'string' ||
    !isAuthMode(authMode) ||
    typeof session.authenticated !== 'boolean'
  ) {
    return undefined
  }

  const modules = value.modules.map(readModule)

  if (!modules.every((module): module is BootstrapModule => module !== undefined)) {
    return undefined
  }

  return {
    basePath,
    apiBase,
    streamUrl,
    sdkVersion,
    authMode,
    session: { authenticated: session.authenticated, name: typeof session.name === 'string' ? session.name : null },
    traceLinkTemplate: typeof traceLinkTemplate === 'string' ? traceLinkTemplate : undefined,
    modules,
  }
}

export function readBootstrapState(document: Document): BootstrapState {
  const text = document.getElementById(stateElementId)?.textContent

  if (!text) {
    throw new Error(`The page has no #${stateElementId} block; the dashboard's server renders this page.`)
  }

  const state = readState(JSON.parse(text) as unknown)

  if (!state) {
    throw new Error(`The #${stateElementId} block does not hold the dashboard's page state.`)
  }

  return state
}

/** Whether the page needs a signed-in session; a dashboard without sign-in is open to everyone. */
export function isSignedIn(state: BootstrapState) {
  return state.authMode === 'none' || state.session.authenticated
}

/** What `GET api/auth/info` answers: the session now, which the inlined state may predate. */
export interface AuthInfo {
  mode: string
  enabled: boolean
  /** Whether Host mode has a challenge scheme, so the sign-in page can hand off to the host's sign-in. */
  signInAvailable: boolean
  authenticated: boolean
  name?: string | null
  canOperate: boolean
  modules?: Record<string, { canView: boolean; canOperate: boolean }>
}

export async function fetchAuthInfo(apiBase: string, fetcher: typeof fetch): Promise<AuthInfo> {
  const response = await fetcher(`${apiBase}/auth/info`, {
    headers: { Accept: 'application/json' },
    credentials: 'same-origin',
    cache: 'no-store',
  })

  if (!response.ok) {
    throw new Error(`The dashboard answered ${response.status} for its sign-in state.`)
  }

  const info = readAuthInfo(await response.json())

  if (!info) {
    throw new Error('The dashboard answered its sign-in state in a shape this page does not read.')
  }

  return info
}

function readAuthInfo(value: unknown): AuthInfo | undefined {
  if (!isRecord(value) || typeof value.mode !== 'string' || typeof value.authenticated !== 'boolean') {
    return undefined
  }

  let modules: AuthInfo['modules']

  if (isRecord(value.modules)) {
    modules = {}

    for (const [id, access] of Object.entries(value.modules)) {
      if (!isRecord(access) || typeof access.canView !== 'boolean' || typeof access.canOperate !== 'boolean') {
        return undefined
      }

      modules[id] = { canView: access.canView, canOperate: access.canOperate }
    }
  }

  return {
    mode: value.mode,
    enabled: value.enabled === true,
    signInAvailable: value.signInAvailable === true,
    authenticated: value.authenticated,
    name: typeof value.name === 'string' ? value.name : null,
    canOperate: value.canOperate === true,
    modules,
  }
}

/**
 * Whether the inlined state shows the same session as `info`: the same sign-in, the same modules, and the same
 * operate permission in each. A refused module's permission is not compared, because its entry never carries one.
 */
export function sessionMatches(state: BootstrapState, info: AuthInfo) {
  if (state.authMode === 'none') {
    return true
  }

  if (state.session.authenticated !== info.authenticated) {
    return false
  }

  if (!info.authenticated || !info.modules) {
    return true
  }

  const viewable = Object.entries(info.modules)
    .filter(([, access]) => access.canView)
    .map(([id]) => id)
    .sort()
  const inlined = state.modules.map((module) => module.id).sort()

  if (viewable.join('\n') !== inlined.join('\n')) {
    return false
  }

  return state.modules.every((module) => module.refusal || module.canOperate === info.modules?.[module.id]?.canOperate)
}

export interface SettleSessionOptions {
  fetch: typeof fetch
  /** Remembers, for this tab, that the page already reloaded once. */
  storage?: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>
  reload: () => void
}

export type SessionSettlement = { status: 'reloading' } | { status: 'ready'; info?: AuthInfo }

/**
 * Compares the inlined state with the session the server reports now, and reloads the page once when they differ.
 *
 * A page loaded right after a sign-in, such as the return from an identity provider's redirect, can be rendered
 * before the browser sent the new `SameSite=Strict` cookie, so the inlined state reads signed out although the
 * session exists. One reload fixes that; the marker in session storage keeps a lasting difference from reloading in a
 * loop, and the page then renders from the inlined state.
 */
export async function settleSession(state: BootstrapState, options: SettleSessionOptions): Promise<SessionSettlement> {
  if (state.authMode === 'none') {
    return { status: 'ready' }
  }

  const marker = `headless-dashboard:reloaded:${state.basePath}`
  let info: AuthInfo

  try {
    info = await fetchAuthInfo(state.apiBase, options.fetch)
  } catch {
    // Without an answer there is nothing to compare; the inlined state is the best this page knows.
    return { status: 'ready' }
  }

  const storage = options.storage

  if (sessionMatches(state, info)) {
    safely(() => storage?.removeItem(marker))
    return { status: 'ready', info }
  }

  if (safely(() => storage?.getItem(marker)) === '1') {
    safely(() => storage?.removeItem(marker))
    return { status: 'ready', info }
  }

  safely(() => storage?.setItem(marker, '1'))
  options.reload()

  return { status: 'reloading' }
}

// Storage throws in a private window or with site data blocked; the reload guard is a convenience, never a blocker.
function safely<T>(action: () => T): T | undefined {
  try {
    return action()
  } catch {
    return undefined
  }
}

/** The states the sign-in page explains, carried in its `state` query parameter. */
export type SignInState = 'signed-out' | 'session-ended' | 'forbidden'

/** The sign-in page's URL, returning to `returnUrl` after sign-in. */
export function signInUrl(basePath: string, options: { state?: SignInState; returnUrl?: string } = {}) {
  const query = new URLSearchParams()

  if (options.state) {
    query.set('state', options.state)
  }

  if (options.returnUrl) {
    query.set('returnUrl', options.returnUrl)
  }

  const text = query.toString()

  return `${basePath}/sign-in${text ? `?${text}` : ''}`
}

/**
 * A return URL the shell will follow: a path under the dashboard's own base path, so the sign-in page never sends
 * the operator to another site or elsewhere in the host. Anything else returns to the dashboard's home.
 */
export function safeReturnUrl(basePath: string, returnUrl: string | null | undefined) {
  const home = `${basePath}/`

  if (!returnUrl || !returnUrl.startsWith('/') || returnUrl.startsWith('//') || returnUrl.includes('\\')) {
    return home
  }

  const path = returnUrl.split(/[?#]/)[0] ?? ''

  if (path.split('/').some((segment) => segment === '.' || segment === '..' || /%2e/i.test(segment))) {
    return home
  }

  const underBase =
    path === basePath || path.startsWith(home) || (basePath === '' && path.startsWith('/'))

  return underBase && !path.startsWith(`${basePath}/sign-in`) ? returnUrl : home
}
