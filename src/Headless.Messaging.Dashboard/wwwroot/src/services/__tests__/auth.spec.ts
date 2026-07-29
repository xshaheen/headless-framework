import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { authService } from '@/services/auth'

describe('host authentication login', () => {
  beforeEach(() => {
    localStorage.clear()
    window.MessagingConfig = {
      basePath: '/messaging',
      auth: { mode: 'host', enabled: true, sessionTimeout: 60 },
    }
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    localStorage.clear()
    delete window.MessagingConfig
  })

  it('initializes authentication before validating a fragment-delivered access token', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        authenticated: true,
        username: 'host-user',
        message: 'Authentication successful',
      }),
    })
    vi.stubGlobal('fetch', fetchMock)

    const loggedIn = await authService.login({ hostAccessKey: 'Bearer demo.jwt' })

    expect(loggedIn).toBe(true)
    expect(fetchMock).toHaveBeenCalledOnce()
    expect(authService.getStatus()).toMatchObject({ authenticated: true, username: 'host-user' })
    expect(localStorage.getItem('messaging_host_access_key')).toBe('Bearer demo.jwt')
  })
})
