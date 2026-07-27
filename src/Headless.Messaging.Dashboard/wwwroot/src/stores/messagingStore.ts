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
  role: 'Transport' | 'Storage' | 'Coordination'
  lanes: Array<'Bus' | 'Queue'>
  supportsIndependentLaneTopology: boolean
  supportsDelayedScheduling: boolean
}

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

export const useMessagingStore = defineStore('messaging', () => {
  const alertStore = useAlertStore()

  // --- State ---

  const isLoading = ref(false)
  const isMetaLoaded = ref(false)
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
    try {
      metaError.value = null
      const data = await httpService.get<{
        messaging?: { name?: string; version?: string } | null
        broker?: { name?: string } | null
        storage?: { name?: string } | null
        providerCapabilities?: ProviderCapability[]
      }>('/meta')

      meta.messaging = data.messaging
        ? { name: data.messaging.name ?? '', version: data.messaging.version ?? '' }
        : null
      meta.broker = data.broker ? { name: data.broker.name ?? '' } : null
      meta.storage = data.storage ? { name: data.storage.name ?? '' } : null
      meta.providerCapabilities = data.providerCapabilities ?? []
    } catch (error) {
      console.error('Failed to fetch meta:', error)
      metaError.value = 'Provider capabilities could not be loaded.'
    } finally {
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
