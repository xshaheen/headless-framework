import { defineStore } from 'pinia'
import { ref } from 'vue'

export interface AlertOptions {
  id?: string
  message: string
  type?: 'error' | 'warning' | 'info' | 'success'
  title?: string
  timeout?: number
  persistent?: boolean
  closable?: boolean
  actions?: Array<{
    text: string
    color?: string
    action: () => void
  }>
}

export interface Alert extends Required<Omit<AlertOptions, 'actions'>> {
  id: string
  actions: AlertOptions['actions']
  visible: boolean
  createdAt: number
}

export const useAlertStore = defineStore('alert', () => {
  const alerts = ref<Alert[]>([])

  const generateId = (): string => {
    return `alert_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`
  }

  const addAlert = (options: AlertOptions): string => {
    try {
      const id = options.id || generateId()

      const alert: Alert = {
        id,
        message: options.message,
        title: options.title || '',
        type: options.type || 'info',
        timeout: options.timeout ?? (options.type === 'error' ? 6000 : 4000),
        persistent: options.persistent ?? false,
        closable: options.closable ?? true,
        actions: options.actions,
        visible: true,
        createdAt: Date.now(),
      }

      alerts.value.push(alert)

      if (!alert.persistent && alert.timeout > 0) {
        setTimeout(() => {
          dismissAlert(id)
        }, alert.timeout)
      }

      return id
    } catch (error) {
      console.error('Error adding alert:', error)
      return ''
    }
  }

  const dismissAlert = (id: string) => {
    try {
      const index = alerts.value.findIndex((alert) => alert.id === id)
      if (index > -1) {
        alerts.value[index].visible = false
        setTimeout(() => {
          const currentIndex = alerts.value.findIndex((alert) => alert.id === id)
          if (currentIndex > -1) {
            alerts.value.splice(currentIndex, 1)
          }
        }, 300)
      }
    } catch (error) {
      console.error('Error dismissing alert:', error)
    }
  }

  const clearAllAlerts = () => {
    alerts.value.forEach((alert) => {
      alert.visible = false
    })
    setTimeout(() => {
      alerts.value.length = 0
    }, 300)
  }

  const showError = (message: string, options?: Omit<AlertOptions, 'message' | 'type'>) => {
    return addAlert({ ...options, message, type: 'error' })
  }

  const showWarning = (message: string, options?: Omit<AlertOptions, 'message' | 'type'>) => {
    return addAlert({ ...options, message, type: 'warning' })
  }

  const showInfo = (message: string, options?: Omit<AlertOptions, 'message' | 'type'>) => {
    return addAlert({ ...options, message, type: 'info' })
  }

  const showSuccess = (message: string, options?: Omit<AlertOptions, 'message' | 'type'>) => {
    return addAlert({ ...options, message, type: 'success' })
  }

  const showHttpError = (error: unknown, customMessage?: string) => {
    let message = customMessage || 'An error occurred'
    let title = 'HTTP Error'

    const err = error as { response?: { status?: number; statusText?: string; data?: unknown }; request?: unknown; message?: string }

    if (err?.response) {
      const status = err.response.status
      const statusText = err.response.statusText || 'Unknown Error'

      title = `HTTP ${status} Error`

      const data = err.response.data as Record<string, unknown> | string | undefined
      if (data) {
        if (typeof data === 'string') {
          message = data
        } else if (data.message) {
          message = data.message as string
        } else if (data.error) {
          message = data.error as string
        } else if (data.title) {
          message = data.title as string
        } else {
          message = statusText
        }
      } else {
        message = statusText
      }

      switch (status) {
        case 401:
          message = 'Authentication failed. Please log in again.'
          break
        case 403:
          message = 'You do not have permission to perform this action.'
          break
        case 404:
          message = 'The requested resource was not found.'
          break
        case 500:
          message = 'Internal server error. Please try again later.'
          break
        case 503:
          message = 'Service is temporarily unavailable. Please try again later.'
          break
      }
    } else if (err?.request) {
      title = 'Network Error'
      message = 'Unable to connect to the server. Please check your internet connection.'
    } else if (err?.message) {
      message = err.message
    }

    return showError(message, { title, timeout: 6000, actions: [] })
  }

  return {
    alerts,
    addAlert,
    dismissAlert,
    clearAllAlerts,
    showError,
    showWarning,
    showInfo,
    showSuccess,
    showHttpError,
  }
})
