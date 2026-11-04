import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import {
  getApiBaseUrl,
  getAuthMode,
  getBasePath,
  getStatsPollingInterval,
  isHostMode,
  resolveApiUrl,
  resolvePath,
} from '@/utilities/pathResolver'

const baseConfig = {
  basePath: '/messaging/dashboard',
  auth: { mode: 'none' as const, enabled: false },
}

afterEach(() => {
  delete window.MessagingConfig
})

describe('with basePath only (no backend domain)', () => {
  beforeEach(() => {
    window.MessagingConfig = { ...baseConfig }
  })

  it('prefixes absolute paths with the base path', () => {
    expect(resolvePath('/published')).toBe('/messaging/dashboard/published')
  })

  it('joins relative paths under the base path', () => {
    expect(resolvePath('published')).toBe('/messaging/dashboard/published')
  })

  it('passes fully-qualified URLs through unchanged', () => {
    expect(resolvePath('https://example.test/x')).toBe('https://example.test/x')
  })

  it('derives the API base from the base path', () => {
    expect(getApiBaseUrl()).toBe('/messaging/dashboard/api')
  })

  it('builds API endpoint URLs regardless of a leading slash', () => {
    expect(resolveApiUrl('/published')).toBe('/messaging/dashboard/api/published')
    expect(resolveApiUrl('published')).toBe('/messaging/dashboard/api/published')
  })
})

describe('with an ssl: backend domain', () => {
  beforeEach(() => {
    window.MessagingConfig = { ...baseConfig, backendDomain: 'ssl:api.test.com' }
  })

  it('upgrades to https and strips the ssl: prefix', () => {
    expect(getApiBaseUrl()).toBe('https://api.test.com/api')
    expect(resolveApiUrl('nodes')).toBe('https://api.test.com/api/nodes')
  })
})

describe('with a plain backend domain', () => {
  beforeEach(() => {
    window.MessagingConfig = { ...baseConfig, backendDomain: 'api.test.com' }
  })

  it('uses http and the domain for the API base', () => {
    expect(getApiBaseUrl()).toBe('http://api.test.com/api')
  })
})

describe('polling interval', () => {
  it('returns the configured interval', () => {
    window.MessagingConfig = { ...baseConfig, statsPollingInterval: 12000 }
    expect(getStatsPollingInterval()).toBe(12000)
  })

  it('defaults to the 2000ms server default when unset', () => {
    window.MessagingConfig = { ...baseConfig }
    expect(getStatsPollingInterval()).toBe(2000)
  })

  it.each([0, -500, Number.NaN, Number.POSITIVE_INFINITY])(
    'falls back to 2000ms for the unusable interval %s',
    (statsPollingInterval) => {
      window.MessagingConfig = { ...baseConfig, statsPollingInterval }
      expect(getStatsPollingInterval()).toBe(2000)
    },
  )
})

describe('host mode detection', () => {
  it.each([
    ['host', true],
    ['basic', false],
    ['apikey', false],
    ['none', false],
  ] as const)('reports host mode for %s as %s', (mode, expected) => {
    window.MessagingConfig = { ...baseConfig, auth: { mode, enabled: mode !== 'none' } }
    expect(isHostMode()).toBe(expected)
  })
})

describe('without configuration', () => {
  it('falls back to sane defaults instead of throwing', () => {
    expect(getBasePath()).toBe('/')
    expect(getApiBaseUrl()).toBe('/api')
    expect(getAuthMode()).toBe('none')
    expect(resolvePath('foo')).toBe('foo')
  })
})
