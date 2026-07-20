import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import {
  consumeHostAccessTokenFromFragment,
  getApiBaseUrl,
  getAuthMode,
  getBasePath,
  getStatsPollingInterval,
  resolveApiUrl,
  resolvePath,
} from '@/utilities/pathResolver'

const baseConfig = {
  basePath: '/messaging',
  auth: { mode: 'none' as const, enabled: false, sessionTimeout: 0 },
}

afterEach(() => {
  delete window.MessagingConfig
  window.history.replaceState(null, '', '/')
})

describe('host access token fragments', () => {
  it('normalizes and removes an encoded token fragment in Host mode', () => {
    window.history.replaceState(null, '', '/messaging/login?redirect=%2F#access_token=demo.jwt')

    expect(consumeHostAccessTokenFromFragment('host')).toBe('Bearer demo.jwt')
    expect(window.location.pathname).toBe('/messaging/login')
    expect(window.location.search).toBe('?redirect=%2F')
    expect(window.location.hash).toBe('')
  })

  it('does not add a second Bearer prefix', () => {
    window.history.replaceState(null, '', '/messaging/login#access_token=Bearer%20demo.jwt')

    expect(consumeHostAccessTokenFromFragment('host')).toBe('Bearer demo.jwt')
    expect(window.location.hash).toBe('')
  })

  it('removes but does not consume a token fragment outside Host mode', () => {
    window.history.replaceState(null, '', '/messaging/login#access_token=demo.jwt')

    expect(consumeHostAccessTokenFromFragment('apikey')).toBeNull()
    expect(window.location.hash).toBe('')
  })

  it('ignores query-string tokens', () => {
    window.history.replaceState(null, '', '/messaging/login?access_token=demo.jwt')

    expect(consumeHostAccessTokenFromFragment('host')).toBeNull()
  })
})

describe('with basePath only (no backend domain)', () => {
  beforeEach(() => {
    window.MessagingConfig = { ...baseConfig }
  })

  it('prefixes absolute paths with the base path', () => {
    expect(resolvePath('/dashboard')).toBe('/messaging/dashboard')
  })

  it('joins relative paths under the base path', () => {
    expect(resolvePath('dashboard')).toBe('/messaging/dashboard')
  })

  it('passes fully-qualified URLs through unchanged', () => {
    expect(resolvePath('https://example.test/x')).toBe('https://example.test/x')
  })

  it('derives the API base from the base path', () => {
    expect(getApiBaseUrl()).toBe('/messaging/api')
  })

  it('builds API endpoint URLs regardless of a leading slash', () => {
    expect(resolveApiUrl('/published')).toBe('/messaging/api/published')
    expect(resolveApiUrl('published')).toBe('/messaging/api/published')
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

  it('defaults to 5000ms when unset', () => {
    window.MessagingConfig = { ...baseConfig }
    expect(getStatsPollingInterval()).toBe(5000)
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
