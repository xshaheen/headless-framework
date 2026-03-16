<template>
  <div class="dashboard-page">
    <div class="page-content">
      <!-- Stats Cards -->
      <div class="stats-grid">
        <v-card class="stat-card">
          <v-card-text class="stat-content">
            <div class="stat-icon-wrapper success">
              <v-icon>mdi-check-circle</v-icon>
            </div>
            <div class="stat-info">
              <div class="stat-value">{{ stats.publishedSucceeded }}</div>
              <div class="stat-label">Published Succeeded</div>
            </div>
          </v-card-text>
        </v-card>

        <v-card class="stat-card">
          <v-card-text class="stat-content">
            <div class="stat-icon-wrapper error">
              <v-icon>mdi-alert-circle</v-icon>
            </div>
            <div class="stat-info">
              <div class="stat-value">{{ stats.publishedFailed }}</div>
              <div class="stat-label">Published Failed</div>
            </div>
          </v-card-text>
        </v-card>

        <v-card class="stat-card">
          <v-card-text class="stat-content">
            <div class="stat-icon-wrapper info">
              <v-icon>mdi-inbox-arrow-down</v-icon>
            </div>
            <div class="stat-info">
              <div class="stat-value">{{ stats.receivedSucceeded }}</div>
              <div class="stat-label">Received Succeeded</div>
            </div>
          </v-card-text>
        </v-card>

        <v-card class="stat-card">
          <v-card-text class="stat-content">
            <div class="stat-icon-wrapper warning">
              <v-icon>mdi-message-alert</v-icon>
            </div>
            <div class="stat-info">
              <div class="stat-value">{{ stats.receivedFailed }}</div>
              <div class="stat-label">Received Failed</div>
            </div>
          </v-card-text>
        </v-card>

        <v-card class="stat-card">
          <v-card-text class="stat-content">
            <div class="stat-icon-wrapper primary">
              <v-icon>mdi-server</v-icon>
            </div>
            <div class="stat-info">
              <div class="stat-value">{{ stats.servers }}</div>
              <div class="stat-label">Servers</div>
            </div>
          </v-card-text>
        </v-card>
      </div>

      <!-- Meta Info -->
      <div class="meta-section">
        <v-card class="meta-card">
          <v-card-title class="meta-title">
            <v-icon class="mr-2">mdi-information</v-icon>
            System Information
          </v-card-title>
          <v-card-text>
            <div v-if="isLoading" class="loading-state">
              <v-progress-circular indeterminate size="24" color="primary" />
              <span class="ml-3">Loading...</span>
            </div>
            <div v-else class="meta-grid">
              <div v-if="meta.messaging" class="meta-item">
                <div class="meta-item-label">Messaging</div>
                <div class="meta-item-value">{{ meta.messaging }}</div>
              </div>
              <div v-if="meta.broker" class="meta-item">
                <div class="meta-item-label">Broker</div>
                <div class="meta-item-value">{{ meta.broker }}</div>
              </div>
              <div v-if="meta.storage" class="meta-item">
                <div class="meta-item-label">Storage</div>
                <div class="meta-item-value">{{ meta.storage }}</div>
              </div>
            </div>
          </v-card-text>
        </v-card>
      </div>

      <!-- Real-time Metrics -->
      <div class="metrics-section">
        <v-card class="metrics-card">
          <v-card-title class="metrics-title">
            <v-icon class="mr-2">mdi-chart-line</v-icon>
            Real-time Metrics
            <v-spacer />
            <v-chip size="x-small" color="primary" variant="tonal">
              {{ pollingInterval / 1000 }}s refresh
            </v-chip>
          </v-card-title>
          <v-card-text>
            <div v-if="isLoading" class="loading-state">
              <v-progress-circular indeterminate size="24" color="primary" />
              <span class="ml-3">Loading metrics...</span>
            </div>
            <div v-else-if="realtimeMetrics" class="realtime-content">
              <pre class="metrics-json">{{ JSON.stringify(realtimeMetrics, null, 2) }}</pre>
            </div>
            <div v-else class="no-data">No metrics data available</div>
          </v-card-text>
        </v-card>
      </div>

      <!-- Hourly History -->
      <div class="history-section">
        <v-card class="history-card">
          <v-card-title class="history-title">
            <v-icon class="mr-2">mdi-history</v-icon>
            Hourly History (24h)
          </v-card-title>
          <v-card-text>
            <div v-if="isLoading" class="loading-state">
              <v-progress-circular indeterminate size="24" color="primary" />
              <span class="ml-3">Loading history...</span>
            </div>
            <v-table v-else-if="metricsHistory.dayHour.length > 0" density="compact" class="history-table">
              <thead>
                <tr>
                  <th>Hour</th>
                  <th class="text-right">Publish OK</th>
                  <th class="text-right">Publish Fail</th>
                  <th class="text-right">Subscribe OK</th>
                  <th class="text-right">Subscribe Fail</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(hour, index) in metricsHistory.dayHour" :key="index">
                  <td>{{ hour }}</td>
                  <td class="text-right text-success">
                    {{ metricsHistory.publishSucceeded[index] || 0 }}
                  </td>
                  <td class="text-right text-error">
                    {{ metricsHistory.publishFailed[index] || 0 }}
                  </td>
                  <td class="text-right text-info">
                    {{ metricsHistory.subscribeSucceeded[index] || 0 }}
                  </td>
                  <td class="text-right text-warning">
                    {{ metricsHistory.subscribeFailed[index] || 0 }}
                  </td>
                </tr>
              </tbody>
            </v-table>
            <div v-else class="no-data">No history data available</div>
          </v-card-text>
        </v-card>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'
import { httpService } from '@/services/http'
import { getStatsPollingInterval } from '@/utilities/pathResolver'
import { useAlertStore } from '@/stores/alertStore'

const alertStore = useAlertStore()
const isLoading = ref(true)
const pollingInterval = getStatsPollingInterval()
let pollTimer: ReturnType<typeof setInterval> | null = null

const stats = reactive({
  publishedSucceeded: 0,
  publishedFailed: 0,
  receivedSucceeded: 0,
  receivedFailed: 0,
  servers: 0,
})

const meta = reactive({
  messaging: '',
  broker: '',
  storage: '',
})

const realtimeMetrics = ref<Record<string, unknown> | null>(null)

const metricsHistory = reactive({
  dayHour: [] as string[],
  publishSucceeded: [] as number[],
  publishFailed: [] as number[],
  subscribeSucceeded: [] as number[],
  subscribeFailed: [] as number[],
})

async function fetchStats() {
  try {
    const data = await httpService.get<Record<string, unknown>>('/stats')
    stats.publishedSucceeded = (data.publishedSucceeded as number) || 0
    stats.publishedFailed = (data.publishedFailed as number) || 0
    stats.receivedSucceeded = (data.receivedSucceeded as number) || 0
    stats.receivedFailed = (data.receivedFailed as number) || 0
    stats.servers = (data.servers as number) || 0
  } catch (error) {
    console.error('Failed to fetch stats:', error)
  }
}

async function fetchMeta() {
  try {
    const data = await httpService.get<Record<string, string>>('/meta')
    meta.messaging = data.messaging || ''
    meta.broker = data.broker || ''
    meta.storage = data.storage || ''
  } catch (error) {
    console.error('Failed to fetch meta:', error)
  }
}

async function fetchRealtimeMetrics() {
  try {
    const data = await httpService.get<Record<string, unknown>>('/metrics-realtime')
    realtimeMetrics.value = data
  } catch (error) {
    console.error('Failed to fetch realtime metrics:', error)
  }
}

async function fetchMetricsHistory() {
  try {
    const data = await httpService.get<Record<string, unknown[]>>('/metrics-history')
    metricsHistory.dayHour = (data.DayHour || data.dayHour || []) as string[]
    metricsHistory.publishSucceeded = (data.PublishSuccessed || data.publishSuccessed || []) as number[]
    metricsHistory.publishFailed = (data.PublishFailed || data.publishFailed || []) as number[]
    metricsHistory.subscribeSucceeded = (data.SubscribeSuccessed || data.subscribeSuccessed || []) as number[]
    metricsHistory.subscribeFailed = (data.SubscribeFailed || data.subscribeFailed || []) as number[]
  } catch (error) {
    console.error('Failed to fetch metrics history:', error)
  }
}

async function loadAll() {
  isLoading.value = true
  try {
    await Promise.all([fetchStats(), fetchMeta(), fetchRealtimeMetrics(), fetchMetricsHistory()])
  } catch (error) {
    alertStore.showError('Failed to load dashboard data')
  } finally {
    isLoading.value = false
  }
}

function startPolling() {
  pollTimer = setInterval(async () => {
    await Promise.all([fetchStats(), fetchRealtimeMetrics()])
  }, pollingInterval)
}

onMounted(async () => {
  await loadAll()
  startPolling()
})

onUnmounted(() => {
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
})
</script>

<style scoped>
.dashboard-page {
  padding: 20px;
}

.page-content {
  max-width: 1240px;
  margin: 0 auto;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
  margin-bottom: 24px;
}

.stat-card {
  background: rgba(30, 30, 30, 0.8) !important;
  border: 1px solid rgba(255, 255, 255, 0.08);
  transition: all 0.3s ease;
}

.stat-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.3);
  border-color: rgba(255, 255, 255, 0.15);
}

.stat-content {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 20px !important;
}

.stat-icon-wrapper {
  width: 48px;
  height: 48px;
  border-radius: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.stat-icon-wrapper.success {
  background: rgba(76, 175, 80, 0.15);
  color: #4caf50;
}

.stat-icon-wrapper.error {
  background: rgba(244, 67, 54, 0.15);
  color: #f44336;
}

.stat-icon-wrapper.info {
  background: rgba(33, 150, 243, 0.15);
  color: #2196f3;
}

.stat-icon-wrapper.warning {
  background: rgba(255, 152, 0, 0.15);
  color: #ff9800;
}

.stat-icon-wrapper.primary {
  background: rgba(103, 58, 183, 0.15);
  color: #7c4dff;
}

.stat-info {
  flex: 1;
}

.stat-value {
  font-size: 1.75rem;
  font-weight: 700;
  color: #e0e0e0;
  line-height: 1.2;
}

.stat-label {
  font-size: 0.8rem;
  color: #9e9e9e;
  font-weight: 500;
  margin-top: 2px;
}

.meta-section,
.metrics-section,
.history-section {
  margin-bottom: 24px;
}

.meta-card,
.metrics-card,
.history-card {
  background: rgba(30, 30, 30, 0.8) !important;
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.meta-title,
.metrics-title,
.history-title {
  display: flex;
  align-items: center;
  font-size: 1rem !important;
  font-weight: 600 !important;
  color: #e0e0e0;
}

.meta-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
}

.meta-item {
  padding: 12px 16px;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.06);
}

.meta-item-label {
  font-size: 0.75rem;
  color: #9e9e9e;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin-bottom: 4px;
}

.meta-item-value {
  font-size: 0.9rem;
  color: #e0e0e0;
  font-weight: 500;
}

.loading-state {
  display: flex;
  align-items: center;
  padding: 24px;
  color: #9e9e9e;
}

.no-data {
  padding: 24px;
  text-align: center;
  color: #757575;
  font-size: 0.875rem;
}

.realtime-content {
  max-height: 300px;
  overflow-y: auto;
}

.metrics-json {
  white-space: pre-wrap;
  word-wrap: break-word;
  background: rgba(0, 0, 0, 0.2);
  padding: 16px;
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.08);
  font-family: 'JetBrains Mono', 'Monaco', 'Consolas', monospace;
  font-size: 0.8rem;
  line-height: 1.5;
  color: #e0e0e0;
}

.history-table {
  background: transparent !important;
}

.text-success {
  color: #4caf50 !important;
}

.text-error {
  color: #f44336 !important;
}

.text-info {
  color: #2196f3 !important;
}

.text-warning {
  color: #ff9800 !important;
}

@media (max-width: 768px) {
  .dashboard-page {
    padding: 12px;
  }

  .stats-grid {
    grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
    gap: 12px;
  }
}
</style>
