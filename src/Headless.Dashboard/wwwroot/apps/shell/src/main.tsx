import { createDashboardClient, setObject } from '@headless/dashboard-sdk'
import { createDashboardApi } from '@headless/dashboard-sdk/data'
import { createBrowserRouter } from '@headless/dashboard-sdk/router'
import { createStreamClient } from '@headless/dashboard-sdk/stream'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { ShellApp, withSessionEnd } from './app.tsx'
import { isSignedIn, readBootstrapState, safeReturnUrl, settleSession, signInUrl, type BootstrapState } from './bootstrap.ts'
import { applyTheme, readTheme } from './layout/theme.tsx'
import { createModuleLoader } from './module-loader.ts'
import { buildRoutes } from './router.tsx'

function sessionStorageOrUndefined() {
  try {
    return window.sessionStorage
  } catch {
    return undefined
  }
}

function currentUrl() {
  return `${location.pathname}${location.search}${location.hash}`
}

function isSignInPath(state: BootstrapState) {
  return location.pathname.replace(/\/+$/, '') === `${state.basePath}/sign-in`
}

async function renderSignIn(state: BootstrapState, root: HTMLElement) {
  if (!isSignInPath(state)) {
    // A deep link opened while signed out: the sign-in page returns the operator to it.
    history.replaceState(null, '', signInUrl(state.basePath, { returnUrl: currentUrl() }))
  }

  const { SignInPage } = await import('./views/sign-in.tsx')

  createRoot(root).render(
    <StrictMode>
      <SignInPage state={state} search={location.search} fetch={fetch.bind(window)} assign={(url) => location.assign(url)} />
    </StrictMode>,
  )
}

function renderShell(state: BootstrapState, root: HTMLElement) {
  if (isSignInPath(state)) {
    // Signed in already: the sign-in page has nothing to do, so go where it would have returned.
    const returnUrl = new URLSearchParams(location.search).get('returnUrl')
    history.replaceState(null, '', safeReturnUrl(state.basePath, returnUrl))
  }

  const toSignIn = (signInState: 'signed-out' | 'session-ended') =>
    location.assign(signInUrl(state.basePath, { state: signInState, returnUrl: signInState === 'session-ended' ? currentUrl() : undefined }))

  const api = withSessionEnd(createDashboardApi({ apiBase: state.apiBase }), () => toSignIn('session-ended'))
  const stream = createStreamClient({
    url: state.streamUrl,
    scope: `headless-dashboard:${state.basePath}`,
    // Another tab signed out: this one follows, to the same page.
    onSignedOut: () => toSignIn('signed-out'),
  })
  const client = createDashboardClient()
  const loader = createModuleLoader()
  const router = createBrowserRouter(buildRoutes(state, loader), { basename: state.basePath || '/' })

  // Modules read the session from the shared data client, the same instance the shell fills.
  setObject(client, 'session', 'current', state.session)

  async function signOut() {
    await api.request({ method: 'POST', path: 'auth/sign-out' })
    // Every tab of this browser closes the shared stream and lands on the sign-in page, this one included.
    stream.signOut()
  }

  createRoot(root).render(
    <StrictMode>
      <ShellApp state={state} router={router} client={client} api={api} stream={stream} loader={loader} signOut={signOut} />
    </StrictMode>,
  )
}

async function start() {
  const root = document.getElementById('root')

  if (!root) {
    throw new Error('The page has no #root element; the dashboard renders into it.')
  }

  applyTheme(readTheme())

  const state = readBootstrapState(document)
  const settle = settleSession(state, { fetch: fetch.bind(window), storage: sessionStorageOrUndefined(), reload: () => location.reload() })

  if (isSignedIn(state)) {
    // Signed in: draw at once from the inlined state; a changed permission still reloads once when the answer lands.
    renderShell(state, root)
    await settle
    return
  }

  // Signed out by the inlined state: wait for the server's answer, because a page loaded right after sign-in can
  // predate the cookie, and showing the sign-in page first would be wrong.
  if ((await settle).status === 'ready') {
    await renderSignIn(state, root)
  }
}

void start()
