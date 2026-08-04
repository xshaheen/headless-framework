import { defineStore } from 'pinia'
import { ref, reactive } from 'vue'
import { httpService } from '@/services/http'
import { useAlertStore } from '@/stores/alertStore'

// --- Types ---

export interface Stats {
  publishedSucceeded: number
  publishedFailed: number
  publishedDelayed: number
  receivedSucceeded: number
  receivedFailed: number
  subscribers: number
  servers: number
}

export interface MetaInfo {
  messaging: { name: string; version: string } | null
  broker: { name: string } | null
  storage: { name: string } | null
  providerCapabilities: ProviderCapability[]
}

export interface ProviderCapability {
  provider: string
  role: string
  lanes: string[]
  supportsIndependentLaneTopology: boolean
  supportsDelayedScheduling: boolean
}

export type ProviderCapabilitiesDisplayState =
  'loading' | 'refreshing' | 'error' | 'stale' | 'empty' | 'content'

export interface MetricsHistory {
  dayHour: number[]
  publishSucceeded: number[]
  publishFailed: number[]
  subscribeSucceeded: number[]
  subscribeFailed: number[]
}

// CircularBuffer<int?>[4]: [timestamps[], publishedPerSec[], subscribedPerSec[], latencyMs[]]
export type RealtimeMetrics = Array<Array<number | null>>

const POLLING_INTERVAL_MS = 2000

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function normalizeProviderName(value: unknown, fallback: string): string {
  return typeof value === 'string' && value.length > 0 ? value : fallback
}

export function normalizeProviderCapabilities(value: unknown): ProviderCapability[] {
  if (!Array.isArray(value)) return []

  return value.flatMap((row): ProviderCapability[] => {
    if (!isRecord(row)) return []

    return [
      {
        provider: normalizeProviderName(row.provider, 'Unknown provider'),
        role: normalizeProviderName(row.role, 'Unknown'),
        lanes: Array.isArray(row.lanes)
          ? row.lanes.filter((lane): lane is string => typeof lane === 'string')
          : [],
        supportsIndependentLaneTopology: row.supportsIndependentLaneTopology === true,
        supportsDelayedScheduling: row.supportsDelayedScheduling === true,
      },
    ]
  })
}

export function getProviderCapabilitiesDisplayState(
  isLoaded: boolean,
  isLoading: boolean,
  error: string | null,
  capabilityCount: number,
): ProviderCapabilitiesDisplayState {
  if (isLoading) return capabilityCount > 0 ? 'refreshing' : 'loading'
  if (error) return capabilityCount > 0 ? 'stale' : 'error'
  if (!isLoaded) return 'loading'
  return capabilityCount > 0 ? 'content' : 'empty'
}

function normalizeMetaInfo(value: unknown): MetaInfo {
  const data = isRecord(value) ? value : {}
  const messaging = isRecord(data.messaging) ? data.messaging : null
  const broker = isRecord(data.broker) ? data.broker : null
  const storage = isRecord(data.storage) ? data.storage : null

  return {
    messaging: messaging
      ? {
          name: normalizeProviderName(messaging.name, ''),
          version: normalizeProviderName(messaging.version, ''),
        }
      : null,
    broker: broker ? { name: normalizeProviderName(broker.name, '') } : null,
    storage: storage ? { name: normalizeProviderName(storage.name, '') } : null,
    providerCapabilities: normalizeProviderCapabilities(data.providerCapabilities),
  }
}

export const useMessagingStore = defineStore('messaging', () => {
  const alertStore = useAlertStore()

  // --- State ---

  const isLoading = ref(false)
  const isMetaLoaded = ref(false)
  const isMetaLoading = ref(false)
  const metaError = ref<string | null>(null)
  const isHistoryLoaded = ref(false)

  const stats = reactive<Stats>({
    publishedSucceeded: 0,
    publishedFailed: 0,
    publishedDelayed: 0,
    receivedSucceeded: 0,
    receivedFailed: 0,
    subscribers: 0,
    servers: 0,
  })

  const meta = reactive<MetaInfo>({
    messaging: null,
    broker: null,
    storage: null,
    providerCapabilities: [],
  })

  const realtimeMetrics = ref<RealtimeMetrics | null>(null)

  const metricsHistory = reactive<MetricsHistory>({
    dayHour: [],
    publishSucceeded: [],
    publishFailed: [],
    subscribeSucceeded: [],
    subscribeFailed: [],
  })

  let pollTimer: ReturnType<typeof setInterval> | null = null
  let isStarting = false
  let isPollRunning = false
  let metaRequestsInFlight = 0

  // --- Fetch actions ---

  async function fetchStats(): Promise<void> {
    try {
      const data = await httpService.get<Record<string, number>>('/stats')
      stats.publishedSucceeded = data.publishedSucceeded ?? 0
      stats.publishedFailed = data.publishedFailed ?? 0
      stats.publishedDelayed = data.publishedDelayed ?? 0
      stats.receivedSucceeded = data.receivedSucceeded ?? 0
      stats.receivedFailed = data.receivedFailed ?? 0
      stats.subscribers = data.subscribers ?? 0
      stats.servers = data.servers ?? 0
    } catch (error) {
      console.error('Failed to fetch stats:', error)
    }
  }

  async function fetchMeta(): Promise<void> {
    metaRequestsInFlight += 1
    isMetaLoading.value = true
    metaError.value = null

    try {
      const data = normalizeMetaInfo(await httpService.get<unknown>('/meta'))

      meta.messaging = data.messaging
      meta.broker = data.broker
      meta.storage = data.storage
      meta.providerCapabilities = data.providerCapabilities
      metaError.value = null
    } catch (error) {
      console.error('Failed to fetch meta:', error)
      metaError.value = 'Provider capabilities could not be loaded.'
    } finally {
      metaRequestsInFlight -= 1
      isMetaLoading.value = metaRequestsInFlight > 0
      isMetaLoaded.value = true
    }
  }

  async function fetchRealtimeMetrics(): Promise<void> {
    try {
      const data = await httpService.get<RealtimeMetrics>('/metrics-realtime')
      realtimeMetrics.value = data
    } catch (error) {
      console.error('Failed to fetch realtime metrics:', error)
    }
  }

  async function fetchMetricsHistory(): Promise<void> {
    try {
      const data = await httpService.get<Record<string, unknown[]>>('/metrics-history')
      metricsHistory.dayHour = (data.DayHour ?? data.dayHour ?? []) as number[]
      metricsHistory.publishSucceeded = (data.PublishSuccessed ??
        data.publishSuccessed ??
        []) as number[]
      metricsHistory.publishFailed = (data.PublishFailed ?? data.publishFailed ?? []) as number[]
      metricsHistory.subscribeSucceeded = (data.SubscribeSuccessed ??
        data.subscribeSuccessed ??
        []) as number[]
      metricsHistory.subscribeFailed = (data.SubscribeFailed ??
        data.subscribeFailed ??
        []) as number[]
    } catch (error) {
      console.error('Failed to fetch metrics history:', error)
    } finally {
      isHistoryLoaded.value = true
    }
  }

  // --- Lifecycle ---

  async function startPolling(): Promise<void> {
    if (pollTimer !== null || isStarting) return
    isStarting = true

    try {
      isLoading.value = true
      try {
        const initialFetches: Promise<void>[] = [fetchStats(), fetchRealtimeMetrics()]

        if (!isMetaLoaded.value) {
          initialFetches.push(fetchMeta())
        }

        if (!isHistoryLoaded.value) {
          initialFetches.push(fetchMetricsHistory())
        }

        await Promise.all(initialFetches)
      } catch {
        alertStore.showError('Failed to load dashboard data')
      } finally {
        isLoading.value = false
      }

      pollTimer = setInterval(async () => {
        if (isPollRunning) return
        isPollRunning = true
        try {
          await Promise.all([fetchStats(), fetchRealtimeMetrics()])
        } finally {
          isPollRunning = false
        }
      }, POLLING_INTERVAL_MS)
    } finally {
      isStarting = false
    }
  }

  function stopPolling(): void {
    if (pollTimer !== null) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  }

  return {
    // State
    isLoading,
    isMetaLoaded,
    isMetaLoading,
    metaError,
    isHistoryLoaded,
    stats,
    meta,
    realtimeMetrics,
    metricsHistory,

    // Actions
    fetchStats,
    fetchMeta,
    fetchRealtimeMetrics,
    fetchMetricsHistory,
    startPolling,
    stopPolling,
  }
})
