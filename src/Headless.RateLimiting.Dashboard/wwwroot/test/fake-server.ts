import { DashboardApiError, type ApiRequest } from '@headless/dashboard-sdk/data'
import { renderModule, type RenderModuleOptions } from '@headless/dashboard-sdk/testing'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type { Catalogue, LookupView } from '../src/api.ts'
import rateLimiting from '../src/module.ts'

export const now = new Date('2026-11-26T14:20:31Z')
export const subject = 'someone@example.com'
export const fingerprint = 'Xk3qAbCdEfGhIjKlMnOpQrStUvWxYz0123456789fTa'

export const catalogue: Catalogue = {
  purposes: [
    { name: 'otp.verify', limit: 3, windowSeconds: 600 },
    { name: 'password-reset.request', limit: 3, windowSeconds: 3600 },
    { name: 'sign-in.password', limit: 5, windowSeconds: 900 },
  ],
  shortest: { name: 'otp.verify', limit: 3, windowSeconds: 600 },
  counterStore: 'default',
}

/** The window of `sign-in.password` 14:15 to 14:30, read at 14:20:31, with `count` attempts. */
export function window(count: number, overrides: Partial<LookupView> = {}): LookupView {
  return {
    purpose: 'sign-in.password',
    fingerprint,
    count,
    limit: 5,
    remaining: Math.max(0, 5 - count),
    isLockedOut: count >= 5,
    windowStartsAt: '2026-11-26T14:15:00Z',
    windowEndsAt: '2026-11-26T14:30:00Z',
    closesInSeconds: 569,
    quota: { limit: 5, windowSeconds: 900 },
    declared: true,
    ...overrides,
  }
}

export interface FakeServer {
  catalogue?: Catalogue
  /**
   * The answers to successive lookups; the last one repeats. An `Error` answers as a failure, and a promise holds the
   * answer until the test settles it (see `held`).
   */
  lookups?: (LookupView | DashboardApiError | Promise<LookupView>)[]
  /** The answer to a reset: its rows, or an error such as a refusal. */
  reset?: { outcome: 'done' | 'skipped' | 'failed'; reason?: string } | DashboardApiError
}

export function server(fake: FakeServer) {
  let lookup = 0

  return (request: ApiRequest): unknown => {
    if (request.method === 'GET' && request.path === 'rate-limiting/catalogue') {
      return fake.catalogue ?? catalogue
    }

    if (request.method === 'GET' && request.path === 'counts') {
      return {
        counts: { purposes: { count: (fake.catalogue ?? catalogue).purposes.length, capped: false } },
        stale: [],
      }
    }

    if (request.method === 'POST' && request.path === 'rate-limiting/lookup') {
      const answers = fake.lookups ?? [window(0)]
      const answer = answers[Math.min(lookup, answers.length - 1)]!
      lookup += 1

      if (answer instanceof DashboardApiError) {
        throw answer
      }

      return answer
    }

    if (request.method === 'POST' && request.path === 'rate-limiting/commands/rate-limiting.reset') {
      const answer = fake.reset ?? { outcome: 'done' as const }

      if (answer instanceof DashboardApiError) {
        throw answer
      }

      return { rows: [{ id: (request.body as { ids: string[] }).ids[0], ...answer }] }
    }

    throw new DashboardApiError(404, `No fake for ${request.method} ${request.path}`)
  }
}

/** A lookup answer the server holds until the test calls `release`. */
export function held() {
  let release: (view: LookupView) => void = () => undefined
  const answer = new Promise<LookupView>((resolve) => {
    release = resolve
  })

  return { answer, release: (view: LookupView) => release(view) }
}

export function open(fake: FakeServer = {}, options: RenderModuleOptions = {}) {
  return renderModule(rateLimiting, { api: server(fake), ...options })
}

export function user() {
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
}

export function described(element: HTMLElement) {
  return document.getElementById(element.getAttribute('aria-describedby') ?? '')?.textContent
}
