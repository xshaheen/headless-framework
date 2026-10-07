import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

// The host's custom validator receives the raw Authorization header value, so the dashboard must send the credential
// exactly as typed: no scheme prefix, no encoding.
const CREDENTIAL = 'Token team=ops; sig=abc123'

describe('custom authentication', () => {
  beforeEach(() => {
    localStorage.clear()
    window.JobsConfig = {
      basePath: '/jobs/dashboard',
      auth: { mode: 'custom', enabled: true, sessionTimeout: 60 },
    }
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  it('validates the credential verbatim through auth/validate and keeps it for later calls', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ authenticated: true, username: 'custom-user' }),
    })
    vi.stubGlobal('fetch', fetchMock)
    const { authService } = await import('@/services/auth')

    expect(await authService.login({ customCredential: CREDENTIAL })).toBe(true)

    expect(fetchMock).toHaveBeenCalledWith(
      '/jobs/dashboard/api/auth/validate',
      expect.objectContaining({ method: 'POST', headers: { Authorization: CREDENTIAL } }),
    )
    expect(authService.getAuthHeader()).toBe(CREDENTIAL)
    expect(authService.getAccessToken()).toBe(CREDENTIAL)
    expect(authService.isAuthenticated()).toBe(true)
  })

  it('drops a credential the validator rejects', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ authenticated: false, message: 'Custom authentication failed' }),
      }),
    )
    const { authService } = await import('@/services/auth')

    expect(await authService.login({ customCredential: CREDENTIAL })).toBe(false)

    expect(authService.getAuthHeader()).toBeNull()
    expect(localStorage.getItem('jobs_custom_credential')).toBeNull()
  })

  it('restores a stored credential as signed in on the next start', async () => {
    localStorage.setItem('jobs_custom_credential', CREDENTIAL)
    localStorage.setItem('jobs_signed_in_at', String(Date.now()))
    const { authService } = await import('@/services/auth')

    await authService.initialize()

    expect(authService.isAuthenticated()).toBe(true)
  })
})
