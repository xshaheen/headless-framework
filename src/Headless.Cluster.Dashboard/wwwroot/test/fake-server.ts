import { DashboardApiError, type ApiRequest } from '@headless/dashboard-sdk/data'
import { renderModule, type RenderModuleOptions } from '@headless/dashboard-sdk/testing'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import cluster from '../src/module.ts'

export const now = new Date('2026-11-27T10:00:00Z')

export interface FakeServer {
  extra?: (request: ApiRequest) => unknown
}

/** A fake of the Cluster module's API, the shell's aggregates it reads, and the shell's command endpoints for it. */
export function server({ extra }: FakeServer) {
  return (request: ApiRequest) => {
    const answered = extra?.(request)

    if (answered !== undefined) {
      return answered
    }

    if (request.path === 'counts') {
      return { counts: {}, stale: [] }
    }

    throw new DashboardApiError(404, `No fake for ${request.method} ${request.path}`)
  }
}

export function open(fake: FakeServer, options: RenderModuleOptions = {}) {
  return renderModule(cluster, { api: server(fake), ...options })
}

export function user() {
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
}
