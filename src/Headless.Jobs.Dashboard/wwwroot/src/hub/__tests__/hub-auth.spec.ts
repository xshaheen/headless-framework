import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import * as signalR from '@microsoft/signalr'

describe('notification hub credentials', () => {
  beforeEach(() => {
    localStorage.clear()
    window.JobsConfig = {
      basePath: '/jobs/dashboard',
      auth: { mode: 'custom', enabled: true, sessionTimeout: 60 },
    }
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  it('passes a custom credential verbatim as the access token, as basic mode passes its own', async () => {
    localStorage.setItem('jobs_custom_credential', 'Token raw value')
    const withUrl = vi.spyOn(signalR.HubConnectionBuilder.prototype, 'withUrl')
    // SignalR cannot resolve a relative hub URL outside a real browser; only the URL handed to it matters here.
    vi.spyOn(signalR.HubConnectionBuilder.prototype, 'build').mockReturnValue({} as signalR.HubConnection)
    const { default: BaseHub } = await import('@/hub/base/baseHub')

    new BaseHub()

    expect(withUrl).toHaveBeenCalledWith(
      `/jobs/dashboard/job-notification-hub?access_token=${encodeURIComponent('Token raw value')}`,
      expect.anything(),
    )
  })
})
