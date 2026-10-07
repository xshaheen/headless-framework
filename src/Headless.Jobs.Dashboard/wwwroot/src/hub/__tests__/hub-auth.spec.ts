import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import * as signalR from '@microsoft/signalr'

const HUB_URL = '/jobs/dashboard/job-notification-hub'
const BASIC_CREDENTIAL = btoa('admin:pass')

describe('notification hub credentials', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  // Each access token is the value the server's auth service accepts for that mode, and matches what the API sends
  // in its Authorization header.
  it.each([
    ['basic', 'jobs_basic_auth', BASIC_CREDENTIAL, BASIC_CREDENTIAL],
    ['apikey', 'jobs_api_key', 'secret-key', 'Bearer:secret-key'],
    ['host', 'jobs_host_access_key', 'Bearer host-token', 'Bearer host-token'],
    ['custom', 'jobs_custom_credential', 'Token raw value', 'Token raw value'],
  ])('sends the stored %s credential as the access token', async (mode, storageKey, stored, expected) => {
    window.JobsConfig = { basePath: '/jobs/dashboard', auth: { mode, enabled: true, sessionTimeout: 60 } }
    localStorage.setItem(storageKey, stored)

    expect(await createdHubUrl()).toBe(`${HUB_URL}?access_token=${encodeURIComponent(expected)}`)
  })

  it('sends no access token when auth is off', async () => {
    window.JobsConfig = { basePath: '/jobs/dashboard', auth: { mode: 'none', enabled: false, sessionTimeout: 60 } }
    localStorage.setItem('jobs_basic_auth', 'stale')

    expect(await createdHubUrl()).toBe(HUB_URL)
  })

  it("sends no access token when only another mode's credential is stored", async () => {
    window.JobsConfig = { basePath: '/jobs/dashboard', auth: { mode: 'apikey', enabled: true, sessionTimeout: 60 } }
    localStorage.setItem('jobs_basic_auth', BASIC_CREDENTIAL)

    expect(await createdHubUrl()).toBe(HUB_URL)
  })
})

async function createdHubUrl(): Promise<string> {
  const withUrl = vi.spyOn(signalR.HubConnectionBuilder.prototype, 'withUrl')
  // SignalR cannot resolve a relative hub URL outside a real browser; only the URL handed to it matters here.
  vi.spyOn(signalR.HubConnectionBuilder.prototype, 'build').mockReturnValue({} as signalR.HubConnection)
  const { default: BaseHub } = await import('@/hub/base/baseHub')

  new BaseHub()

  return withUrl.mock.calls[0][0]
}
