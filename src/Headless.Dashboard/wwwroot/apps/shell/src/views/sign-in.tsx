import { Button, TextInputField } from '@headless/dashboard-sdk/components'
import { useEffect, useState, type FormEvent } from 'react'
import './sign-in.css'
import { fetchAuthInfo, safeReturnUrl, type BootstrapState, type SignInState } from '../bootstrap.ts'

export interface SignInPageProps {
  state: BootstrapState
  /** The page's query string: `state` and `returnUrl`. */
  search: string
  fetch: typeof fetch
  /** Loads `url` as a new document, so the page renders with the new session. */
  assign: (url: string) => void
}

type Phase =
  | { kind: 'checking' }
  | { kind: 'ready' }
  | { kind: 'busy' }
  | { kind: 'refused'; message: string }
  | { kind: 'forbidden'; message: string }

function isSignInState(value: string | null): value is SignInState {
  return value === 'signed-out' || value === 'session-ended' || value === 'forbidden'
}

const stateText: Record<SignInState, string> = {
  'signed-out': 'Signed out. You stay signed out until you sign in again.',
  'session-ended': 'Your session ended. Sign in again to continue.',
  forbidden: "You can't view this dashboard. Ask for access to it.",
}

async function readProblem(response: Response) {
  try {
    const body: unknown = await response.json()

    if (typeof body === 'object' && body !== null) {
      const { detail, title } = body as { detail?: unknown; title?: unknown }

      if (typeof detail === 'string' && detail) {
        return detail
      }

      if (typeof title === 'string' && title) {
        return title
      }
    }
  } catch {
    // No problem details; the caller's own text stands.
  }

  return undefined
}

function utf8Base64(text: string) {
  let binary = ''

  for (const byte of new TextEncoder().encode(text)) {
    binary += String.fromCharCode(byte)
  }

  return btoa(binary)
}

/**
 * The sign-in page at `{base}/sign-in`, the one place a signed-out operator lands. Host mode hands off to the
 * application's own sign-in, and first tries the host user already signed in there; Basic and API key take the
 * credential once, after which the browser holds only the session cookie.
 */
export function SignInPage({ state, search, fetch: fetcher, assign }: SignInPageProps) {
  const query = new URLSearchParams(search)
  const shown = query.get('state')
  const notice = isSignInState(shown) ? shown : undefined
  const returnUrl = safeReturnUrl(state.basePath, query.get('returnUrl'))
  const mode = state.authMode
  // After an explicit sign-out, nothing signs the operator in again until they choose Sign in.
  const trySilently = mode === 'host' && notice === undefined
  const [phase, setPhase] = useState<Phase>(trySilently ? { kind: 'checking' } : { kind: 'ready' })
  const [signInAvailable, setSignInAvailable] = useState<boolean | undefined>(undefined)
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [apiKey, setApiKey] = useState('')

  async function exchange(authorization?: string) {
    const headers: Record<string, string> = { 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json' }

    if (authorization) {
      headers.Authorization = authorization
    }

    return fetcher(`${state.apiBase}/auth/session`, { method: 'POST', headers, credentials: 'same-origin' })
  }

  useEffect(() => {
    if (mode !== 'host') {
      return undefined
    }

    let current = true

    fetchAuthInfo(state.apiBase, fetcher)
      .then((info) => {
        if (current) {
          setSignInAvailable(info.signInAvailable)
        }
      })
      .catch(() => {
        if (current) {
          setSignInAvailable(false)
        }
      })

    if (trySilently) {
      exchange()
        .then(async (response) => {
          if (!current) {
            return
          }

          if (response.ok) {
            assign(returnUrl)
          } else if (response.status === 403) {
            setPhase({ kind: 'forbidden', message: (await readProblem(response)) ?? stateText.forbidden })
          } else {
            setPhase({ kind: 'ready' })
          }
        })
        .catch(() => {
          if (current) {
            setPhase({ kind: 'ready' })
          }
        })
    }

    return () => {
      current = false
    }
    // Runs once per page: the sign-in page is its own document load, and its inputs never change while it shows.
  }, [])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const credential =
      mode === 'basic' ? (userName && password ? `Basic ${utf8Base64(`${userName}:${password}`)}` : undefined) : apiKey ? `Bearer ${apiKey}` : undefined

    if (!credential) {
      setPhase({ kind: 'refused', message: mode === 'basic' ? 'Enter your user name and password.' : 'Enter the API key.' })
      return
    }

    setPhase({ kind: 'busy' })

    try {
      const response = await exchange(credential)

      if (response.ok) {
        assign(returnUrl)
        return
      }

      setPhase({
        kind: 'refused',
        message:
          response.status === 401
            ? mode === 'basic'
              ? 'The user name or password is wrong. Check them and try again.'
              : 'The API key is wrong. Check it and try again.'
            : ((await readProblem(response)) ?? `Sign-in failed: the dashboard answered ${response.status}. Try again.`),
      })
    } catch {
      setPhase({ kind: 'refused', message: 'The dashboard could not be reached. Check your connection and try again.' })
    }
  }

  const hostSignIn = `${state.basePath}/auth/sign-in?returnUrl=${encodeURIComponent(returnUrl)}`
  const forbidden = phase.kind === 'forbidden' ? phase.message : notice === 'forbidden' ? stateText.forbidden : undefined
  const host = typeof location === 'undefined' ? 'the application' : location.host

  return (
    <main className="hd-sign-in">
      <section className="hd-sign-in-panel" aria-labelledby="sign-in-title">
        <div>
          <h1 id="sign-in-title" className="hd-page-title">
            Headless dashboard
          </h1>
          <p className="hd-hint">Background jobs, messages, and what needs an operator, for this application.</p>
        </div>
        {forbidden ? (
          <p role="alert" className="hd-sign-in-state">
            {forbidden}
          </p>
        ) : notice ? (
          <p role="status" className="hd-sign-in-state">
            {stateText[notice]}
          </p>
        ) : null}
        {mode === 'host' ? (
          phase.kind === 'checking' ? (
            <p role="status" className="hd-hint">
              Checking for your sign-in to the application…
            </p>
          ) : signInAvailable === false ? (
            <p className="hd-prose">
              Your session comes from the application. Sign in there, then open the dashboard again.{' '}
              <a href="/">Go to the application</a>
            </p>
          ) : (
            <a className="hd-btn hd-btn-primary hd-sign-in-action" href={hostSignIn}>
              {`Sign in with ${host}`}
            </a>
          )
        ) : mode === 'none' ? (
          <p className="hd-prose">
            This dashboard needs no sign-in. <a href={`${state.basePath}/`}>Open the dashboard</a>
          </p>
        ) : (
          <form className="hd-sign-in-form" onSubmit={(event) => void submit(event)} noValidate>
            {mode === 'basic' ? (
              <>
                <TextInputField label="User name" name="username" value={userName} onChange={setUserName} isRequired />
                <PasswordField label="Password" value={password} onChange={setPassword} />
              </>
            ) : (
              <PasswordField label="API key" value={apiKey} onChange={setApiKey} />
            )}
            {phase.kind === 'refused' ? (
              <p role="alert" className="hd-error">
                {phase.message}
              </p>
            ) : null}
            <Button type="submit" variant="primary" isPending={phase.kind === 'busy'}>
              Sign in
            </Button>
          </form>
        )}
      </section>
    </main>
  )
}

/** A secret field: a native password input, so password managers and the platform treat it as one. */
function PasswordField({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  const id = `sign-in-${label.toLowerCase().replace(/\s+/g, '-')}`

  return (
    <div className="hd-field">
      <label className="hd-label" htmlFor={id}>
        {label}
      </label>
      <input
        id={id}
        className="hd-input"
        type="password"
        required
        autoComplete={label === 'Password' ? 'current-password' : 'off'}
        value={value}
        onChange={(event) => onChange(event.currentTarget.value)}
      />
    </div>
  )
}
