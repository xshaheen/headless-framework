import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { AuthMode } from '../src/bootstrap.ts'
import { SignInPage } from '../src/views/sign-in.tsx'
import { shellState } from './fixtures/modules.tsx'

interface Answers {
  signInAvailable?: boolean
  /** The status `POST api/auth/session` answers. */
  session?: number
  sessionBody?: unknown
}

function renderSignIn(mode: AuthMode, search: string, answers: Answers = {}) {
  const calls: { url: string; init?: RequestInit }[] = []
  const fetcher = vi.fn<typeof fetch>(async (input, init) => {
    const url = String(input)
    calls.push({ url, init })

    if (url.endsWith('/auth/info')) {
      return Response.json({ mode, enabled: true, signInAvailable: answers.signInAvailable ?? true, authenticated: false, canOperate: false })
    }

    const status = answers.session ?? 401

    return new Response(status === 204 ? null : JSON.stringify(answers.sessionBody ?? {}), { status })
  })
  const assign = vi.fn()
  const state = shellState([], { authMode: mode, session: { authenticated: false } })

  render(<SignInPage state={state} search={search} fetch={fetcher} assign={assign} />)

  return { calls, assign }
}

describe('sign-in page', () => {
  it('offers Sign in with the host in Host mode with a challenge scheme, returning to the page asked for', async () => {
    const { assign } = renderSignIn('host', '?returnUrl=%2Fheadless%2Fdiag%2Fruns')

    const link = await screen.findByRole('link', { name: /^Sign in with / })
    expect(link.getAttribute('href')).toBe('/headless/auth/sign-in?returnUrl=%2Fheadless%2Fdiag%2Fruns')
    expect(assign).not.toHaveBeenCalled()
  })

  it('signs a host user already signed in to the application straight in', async () => {
    const { assign } = renderSignIn('host', '?returnUrl=%2Fheadless%2Fdiag', { session: 204 })

    await vi.waitFor(() => expect(assign).toHaveBeenCalledWith('/headless/diag'))
  })

  it('says the session comes from the application in Host mode without a challenge scheme', async () => {
    renderSignIn('host', '', { signInAvailable: false })

    expect(await screen.findByText(/Your session comes from the application/)).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Go to the application' })).toBeTruthy()
    expect(screen.queryByRole('link', { name: /^Sign in with / })).toBeNull()
  })

  it('takes a user name and password in Basic mode, and the browser then holds only the cookie', async () => {
    const user = userEvent.setup()
    const { calls, assign } = renderSignIn('basic', '', { session: 204 })

    await user.type(screen.getByRole('textbox', { name: /User name/ }), 'ops')
    await user.type(screen.getByLabelText('Password'), 'secret')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    await vi.waitFor(() => expect(assign).toHaveBeenCalledWith('/headless/'))
    const exchange = calls.find((call) => call.url === '/headless/api/auth/session')!
    const headers = exchange.init?.headers as Record<string, string>
    expect(headers.Authorization).toBe(`Basic ${btoa('ops:secret')}`)
    expect(headers['X-Requested-With']).toBe('XMLHttpRequest')
  })

  it('says a wrong password is wrong, in place', async () => {
    const user = userEvent.setup()
    renderSignIn('basic', '', { session: 401 })

    await user.type(screen.getByRole('textbox', { name: /User name/ }), 'ops')
    await user.type(screen.getByLabelText('Password'), 'nope')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect((await screen.findByRole('alert')).textContent).toBe('The user name or password is wrong. Check them and try again.')
  })

  it('says when Caps Lock is on in the password, and shows the password on request', async () => {
    const user = userEvent.setup()
    renderSignIn('basic', '')
    const password = screen.getByLabelText('Password')

    await user.type(password, 'Se{CapsLock}CRET')

    const hint = screen.getByText('Caps Lock is on')
    expect(password.getAttribute('aria-describedby')).toBe(hint.id)
    expect(password.getAttribute('type')).toBe('password')

    const show = screen.getByRole('button', { name: 'Show password' })
    expect(show.getAttribute('aria-pressed')).toBe('false')
    await user.click(show)

    expect(password.getAttribute('type')).toBe('text')
    expect(screen.getByRole('button', { name: 'Hide password' }).getAttribute('aria-pressed')).toBe('true')
  })

  it('takes the key in API key mode', async () => {
    const user = userEvent.setup()
    const { calls } = renderSignIn('apiKey', '', { session: 204 })

    expect(screen.queryByRole('textbox', { name: /User name/ })).toBeNull()
    await user.type(screen.getByLabelText('API key'), 'k-123')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    await vi.waitFor(() =>
      expect((calls.find((call) => call.url.endsWith('/auth/session'))?.init?.headers as Record<string, string>).Authorization).toBe(
        'Bearer k-123',
      ),
    )
  })

  it('shows Signed out and does not sign in again by itself', async () => {
    const { calls, assign } = renderSignIn('host', '?state=signed-out', { session: 204 })

    expect((await screen.findByRole('status')).textContent).toContain('Signed out')
    await screen.findByRole('link', { name: /^Sign in with / })
    expect(calls.some((call) => call.url.endsWith('/auth/session'))).toBe(false)
    expect(assign).not.toHaveBeenCalled()
  })

  it('shows Your session ended', () => {
    renderSignIn('basic', '?state=session-ended')

    expect(screen.getByRole('status').textContent).toBe('Your session ended. Sign in again to continue.')
  })

  it("shows the server's 403 reason when no view policy allows the host user", async () => {
    renderSignIn('host', '', {
      session: 403,
      sessionBody: { title: 'No dashboard view policy allows this user.', detail: 'The user passes none of the dashboard\'s view policies: ops.view.' },
    })

    expect((await screen.findByRole('alert')).textContent).toContain('ops.view')
  })
})
