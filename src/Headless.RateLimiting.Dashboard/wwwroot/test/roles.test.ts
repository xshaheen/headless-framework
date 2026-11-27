import { describe, expect, it } from 'vitest'
import { formatCountdown, formatWindow, resetArguments, subjectId, toWindowSeconds } from '../src/api.ts'
import rateLimiting from '../src/module.ts'
import { window } from './fake-server.ts'

describe('rate limiting module', () => {
  it('declares one Rate limits entry in the platform product counted by declared purposes', () => {
    expect(rateLimiting.id).toBe('rate-limiting')
    expect(rateLimiting.product).toBe('platform')
    expect(rateLimiting.sidebar.flatMap((section) => section.entries)).toEqual([
      expect.objectContaining({ label: 'Rate limits', icon: 'gauge', countKey: 'purposes' }),
    ])
  })

  it('reads windows in the largest whole unit', () => {
    expect(formatWindow(900)).toBe('15 min')
    expect(formatWindow(3600)).toBe('1 h')
    expect(formatWindow(90)).toBe('90 s')
    expect(formatCountdown(569)).toBe('9 m 29 s')
    expect(toWindowSeconds('15', 'minutes')).toBe(900)
    expect(toWindowSeconds('1.5', 'hours')).toBeUndefined()
    expect(toWindowSeconds('0', 'seconds')).toBeUndefined()
  })

  it('targets the purpose and fingerprint and sends a quota only for a custom purpose', () => {
    expect(subjectId(window(5))).toMatch(/^sign-in\.password:/)
    expect(resetArguments({ purpose: 'p', subject: 's' })).toEqual({ subject: 's' })
    expect(resetArguments({ purpose: 'p', subject: 's', quota: { limit: 3, windowSeconds: 60 } })).toEqual({
      subject: 's',
      limit: 3,
      windowSeconds: 60,
    })
  })
})
