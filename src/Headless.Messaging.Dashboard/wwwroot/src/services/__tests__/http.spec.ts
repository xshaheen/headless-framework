import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { authService } from '@/services/auth'
import { httpService, HttpError } from '@/services/http'

describe('http service outcome handling', () => {
  beforeEach(() => {
    window.MessagingConfig = { basePath: '/messaging' }
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    delete window.MessagingConfig
  })

  it('ends the session only on 401, dispatching auth:logout', async () => {
    const logoutSpy = vi.spyOn(authService, 'logout').mockImplementation(() => {})
    const eventSpy = vi.fn()
    window.addEventListener('auth:logout', eventSpy)
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue({
          ok: false,
          status: 401,
          headers: new Headers(),
          text: async () => '',
        }),
    )

    await expect(httpService.get('/scheduled')).rejects.toThrow('Authentication required')

    expect(logoutSpy).toHaveBeenCalledOnce()
    expect(eventSpy).toHaveBeenCalledOnce()
    window.removeEventListener('auth:logout', eventSpy)
  })

  it.each([403, 404, 409])(
    'returns the parsed body for a %i response without ending the session',
    async (status) => {
      const logoutSpy = vi.spyOn(authService, 'logout').mockImplementation(() => {})
      const body = { code: 'g:operator_actor_required', remedy: 'Use Host auth.', message: 'nope' }
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue({
          ok: false,
          status,
          headers: new Headers({ 'content-type': 'application/json' }),
          text: async () => JSON.stringify(body),
        }),
      )

      const error = await httpService.get('/scheduled/revoke').catch((e: unknown) => e)

      expect(error).toBeInstanceOf(HttpError)
      expect((error as HttpError).status).toBe(status)
      expect((error as HttpError).body).toEqual(body)
      expect(logoutSpy).not.toHaveBeenCalled()
    },
  )

  it('still throws for a network error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network down')))

    await expect(httpService.get('/scheduled')).rejects.toThrow('network down')
  })
})
