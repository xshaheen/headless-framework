import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { httpService } from '@/services/http'
import {
  getProviderCapabilitiesDisplayState,
  normalizeProviderCapabilities,
  useMessagingStore,
} from '@/stores/messagingStore'

function createDeferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })

  return { promise, resolve, reject }
}

describe('provider capability normalization', () => {
  it('defaults non-array input to an empty capability list', () => {
    expect(normalizeProviderCapabilities(undefined)).toEqual([])
    expect(normalizeProviderCapabilities({ provider: 'Redis' })).toEqual([])
  })

  it('drops malformed rows and safely defaults partial rows', () => {
    expect(normalizeProviderCapabilities([null, 'invalid', 42, {}])).toEqual([
      {
        provider: 'Unknown provider',
        role: 'Unknown',
        lanes: [],
        supportsIndependentLaneTopology: false,
        supportsDelayedScheduling: false,
      },
    ])
  })

  it('preserves unknown string roles and lanes for forward compatibility', () => {
    expect(
      normalizeProviderCapabilities([
        {
          provider: 'Future Broker',
          role: 'Archive',
          lanes: ['Bus', 'Replay', 42, null],
          supportsIndependentLaneTopology: true,
          supportsDelayedScheduling: 'true',
        },
      ]),
    ).toEqual([
      {
        provider: 'Future Broker',
        role: 'Archive',
        lanes: ['Bus', 'Replay'],
        supportsIndependentLaneTopology: true,
        supportsDelayedScheduling: false,
      },
    ])
  })
})

describe('provider capability display state', () => {
  it.each([
    [false, false, null, 0, 'loading'],
    [true, true, null, 0, 'loading'],
    [true, true, null, 2, 'refreshing'],
    [true, false, 'failed', 0, 'error'],
    [true, false, 'failed', 2, 'stale'],
    [true, false, null, 0, 'empty'],
    [true, false, null, 2, 'content'],
  ] as const)(
    'maps loaded=%s loading=%s error=%s count=%s to %s',
    (isLoaded, isLoading, error, count, expected) => {
      expect(getProviderCapabilitiesDisplayState(isLoaded, isLoading, error, count)).toBe(expected)
    },
  )
})

describe('messaging metadata state', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.spyOn(console, 'error').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('marks the initial request in flight until normalized metadata is loaded', async () => {
    const request = createDeferred<unknown>()
    vi.spyOn(httpService, 'get').mockReturnValueOnce(request.promise)
    const store = useMessagingStore()

    const fetch = store.fetchMeta()

    expect(store.isMetaLoading).toBe(true)
    expect(store.isMetaLoaded).toBe(false)
    expect(store.metaError).toBeNull()

    request.resolve({ providerCapabilities: [{}] })
    await fetch

    expect(store.isMetaLoading).toBe(false)
    expect(store.isMetaLoaded).toBe(true)
    expect(store.meta.providerCapabilities[0]?.lanes).toEqual([])
  })

  it('renders an empty retry as in flight instead of as a false empty result', async () => {
    const firstError = new Error('initial failure')
    const retryRequest = createDeferred<unknown>()
    vi.spyOn(httpService, 'get')
      .mockRejectedValueOnce(firstError)
      .mockReturnValueOnce(retryRequest.promise)
    const store = useMessagingStore()

    await store.fetchMeta()
    expect(store.metaError).not.toBeNull()

    const retry = store.fetchMeta()

    expect(store.isMetaLoaded).toBe(true)
    expect(store.isMetaLoading).toBe(true)
    expect(store.metaError).toBeNull()
    expect(
      getProviderCapabilitiesDisplayState(
        store.isMetaLoaded,
        store.isMetaLoading,
        store.metaError,
        store.meta.providerCapabilities.length,
      ),
    ).toBe('loading')

    retryRequest.resolve({ providerCapabilities: [] })
    await retry

    expect(store.isMetaLoading).toBe(false)
    expect(store.meta.providerCapabilities).toEqual([])
  })

  it('keeps prior capability rows visible and labels failed refreshes as stale', async () => {
    const retryRequest = createDeferred<unknown>()
    vi.spyOn(httpService, 'get')
      .mockResolvedValueOnce({
        providerCapabilities: [{ provider: 'Redis', role: 'Transport', lanes: ['Bus'] }],
      })
      .mockReturnValueOnce(retryRequest.promise)
    const store = useMessagingStore()

    await store.fetchMeta()
    const retry = store.fetchMeta()

    expect(store.isMetaLoading).toBe(true)
    expect(store.meta.providerCapabilities).toHaveLength(1)
    expect(
      getProviderCapabilitiesDisplayState(
        store.isMetaLoaded,
        store.isMetaLoading,
        store.metaError,
        store.meta.providerCapabilities.length,
      ),
    ).toBe('refreshing')

    retryRequest.reject(new Error('refresh failed'))
    await retry

    expect(store.isMetaLoading).toBe(false)
    expect(store.meta.providerCapabilities[0]?.provider).toBe('Redis')
    expect(
      getProviderCapabilitiesDisplayState(
        store.isMetaLoaded,
        store.isMetaLoading,
        store.metaError,
        store.meta.providerCapabilities.length,
      ),
    ).toBe('stale')
  })

  it('treats a malformed metadata response as an empty normalized response', async () => {
    vi.spyOn(httpService, 'get').mockResolvedValueOnce(null)
    const store = useMessagingStore()

    await store.fetchMeta()

    expect(store.metaError).toBeNull()
    expect(store.meta).toMatchObject({
      messaging: null,
      broker: null,
      storage: null,
      providerCapabilities: [],
    })
  })
})
