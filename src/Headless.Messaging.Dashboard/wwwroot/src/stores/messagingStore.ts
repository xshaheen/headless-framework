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
      const data = await httpService.get<{
        messaging?: { name?: string; version?: string } | null
        broker?: { name?: string } | null
        storage?: { name?: string } | null
      }>('/meta')

      meta.messaging = data.messaging
        ? { name: data.messaging.name ?? '', version: data.messaging.version ?? '' }
        : null
      meta.broker = data.broker ? { name: data.broker.name ?? '' } : null
      meta.storage = data.storage ? { name: data.storage.name ?? '' } : null
    } catch (error) {
      console.error('Failed to fetch meta:', error)
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
      metricsHistory.publishSucceeded = (data.PublishSuccessed ?? data.publishSuccessed ?? []) as number[]
      metricsHistory.publishFailed = (data.PublishFailed ?? data.publishFailed ?? []) as number[]
      metricsHistory.subscribeSucceeded = (data.SubscribeSuccessed ?? data.subscribeSuccessed ?? []) as number[]
      metricsHistory.subscribeFailed = (data.SubscribeFailed ?? data.subscribeFailed ?? []) as number[]
    } catch (error) {
      console.error('Failed to fetch metrics history:', error)
    } finally {
      isHistoryLoaded.value = true
    }
  }

  // --- Lifecycle ---

  async function startPolling(): Promise<void> {
    if (pollTimer !== null) return

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
    } catch (error) {
      alertStore.showError('Failed to load dashboard data')
    } finally {
      isLoading.value = false
    }

    pollTimer = setInterval(async () => {
      await Promise.all([fetchStats(), fetchRealtimeMetrics()])
    }, POLLING_INTERVAL_MS)
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
