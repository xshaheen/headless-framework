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
            <div class="stat-icon-wrapper warning">
              <v-icon>mdi-clock-outline</v-icon>
            </div>
            <div class="stat-info">
              <div class="stat-value">{{ stats.publishedDelayed }}</div>
              <div class="stat-label">Published Delayed</div>
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
            <div class="stat-icon-wrapper error">
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
            <div class="stat-icon-wrapper accent">
              <v-icon>mdi-account-multiple</v-icon>
            </div>
            <div class="stat-info">
              <div class="stat-value">{{ stats.subscribers }}</div>
              <div class="stat-label">Subscribers</div>
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

      <!-- Charts -->
      <div v-if="isLoading" class="loading-state">
        <v-progress-circular indeterminate size="24" color="primary" />
        <span class="ml-3">Loading metrics...</span>
      </div>
      <MessagingCharts v-else />
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useMessagingStore } from '@/stores/messagingStore'
import MessagingCharts from '@/components/MessagingCharts.vue'

const store = useMessagingStore()
const { stats, isLoading } = storeToRefs(store)

onMounted(async () => {
  await store.startPolling()
})

onUnmounted(() => {
  store.stopPolling()
})
</script>

<style scoped>
.dashboard-page {
  padding: 20px 12px;
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

.stat-icon-wrapper.accent {
  background: rgba(0, 188, 212, 0.15);
  color: #00bcd4;
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

.loading-state {
  display: flex;
  align-items: center;
  padding: 24px;
  color: #9e9e9e;
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
