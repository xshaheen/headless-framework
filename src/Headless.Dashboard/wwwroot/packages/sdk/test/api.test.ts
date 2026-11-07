import { describe, expect, it } from 'vitest'
import { createDashboardApi, DashboardApiError } from '../src/data/api.ts'

describe('dashboard API client', () => {
  it('says the dashboard did not answer when the request never reaches it, not the browser’s Failed to fetch', async () => {
    const api = createDashboardApi({
      apiBase: '/headless/api',
      fetch: () => Promise.reject(new TypeError('Failed to fetch')),
    })

    const failure = api.request({ method: 'GET', path: 'overview' })

    await expect(failure).rejects.toBeInstanceOf(DashboardApiError)
    await expect(failure).rejects.toThrow('The dashboard did not answer. Check the connection, then try again.')
  })
})
