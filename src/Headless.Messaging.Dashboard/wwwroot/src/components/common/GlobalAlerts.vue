<script setup lang="ts">
import { useAlertStore } from '@/stores/alertStore'
import { computed } from 'vue'

const alertStore = useAlertStore()

const visibleAlerts = computed(() => {
  return alertStore.alerts.filter((alert) => alert.visible)
})

const getAlertIcon = (type: string) => {
  switch (type) {
    case 'error':
      return 'mdi-alert-circle'
    case 'warning':
      return 'mdi-alert'
    case 'success':
      return 'mdi-check-circle'
    case 'info':
    default:
      return 'mdi-information'
  }
}

const handleClose = (alertId: string) => {
  alertStore.dismissAlert(alertId)
}
</script>

<template>
  <div class="global-alerts">
    <div>
      <div
        v-for="alert in visibleAlerts"
        :key="alert.id"
        :class="['alert-item', `alert-${alert.type}`]"
        @click.stop
      >
        <div class="alert-content">
          <div class="alert-icon">
            <v-icon :icon="getAlertIcon(alert.type)" size="18" />
          </div>

          <div class="alert-text">
            <div v-if="alert.title" class="alert-title">
              {{ alert.title }}
            </div>
            <div class="alert-message">
              {{ alert.message }}
            </div>
          </div>

          <button
            v-if="alert.closable"
            class="alert-close-btn"
            aria-label="Close alert"
            @click="handleClose(alert.id)"
          >
            <v-icon icon="mdi-close" size="16" />
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.global-alerts {
  position: fixed;
  top: 16px;
  right: 16px;
  z-index: 9999;
  pointer-events: none;
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-width: 350px;
}

.alert-item {
  pointer-events: auto;
  min-width: 280px;
  background: rgba(66, 66, 66, 0.95);
  backdrop-filter: blur(10px);
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3);
  overflow: hidden;
  border-left: 4px solid;
  transition: all 0.2s ease;
}

.alert-item:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
}

.alert-content {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 12px 14px;
}

.alert-icon {
  flex-shrink: 0;
  margin-top: 1px;
}

.alert-text {
  flex: 1;
  min-width: 0;
}

.alert-title {
  font-weight: 600;
  font-size: 0.75rem;
  line-height: 1.2;
  margin-bottom: 3px;
  color: rgba(255, 255, 255, 0.95);
}

.alert-message {
  font-size: 0.75rem;
  line-height: 1.3;
  color: rgba(255, 255, 255, 0.85);
  word-wrap: break-word;
  overflow-wrap: break-word;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.alert-close-btn {
  background: none;
  border: none;
  color: rgba(255, 255, 255, 0.7);
  cursor: pointer;
  padding: 2px;
  border-radius: 4px;
  transition: all 0.2s ease;
  display: flex;
  align-items: center;
  justify-content: center;
}

.alert-close-btn:hover {
  background: rgba(255, 255, 255, 0.1);
  color: rgba(255, 255, 255, 0.9);
}

.alert-error {
  border-left-color: #f44336;
  background: linear-gradient(135deg, rgba(244, 67, 54, 0.1) 0%, rgba(211, 47, 47, 0.1) 100%);
}

.alert-error .alert-icon {
  color: #f44336;
}

.alert-success {
  border-left-color: #4caf50;
  background: linear-gradient(135deg, rgba(76, 175, 80, 0.1) 0%, rgba(56, 142, 60, 0.1) 100%);
}

.alert-success .alert-icon {
  color: #4caf50;
}

.alert-warning {
  border-left-color: #ff9800;
  background: linear-gradient(135deg, rgba(255, 152, 0, 0.1) 0%, rgba(245, 124, 0, 0.1) 100%);
}

.alert-warning .alert-icon {
  color: #ff9800;
}

.alert-info {
  border-left-color: #2196f3;
  background: linear-gradient(135deg, rgba(33, 150, 243, 0.1) 0%, rgba(25, 118, 210, 0.1) 100%);
}

.alert-info .alert-icon {
  color: #2196f3;
}

@media (max-width: 600px) {
  .global-alerts {
    left: 8px;
    right: 8px;
    top: 8px;
    max-width: none;
  }

  .alert-item {
    min-width: auto;
  }
}
</style>
