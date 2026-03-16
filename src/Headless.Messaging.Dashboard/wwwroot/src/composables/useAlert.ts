import { useAlertStore, type AlertOptions } from '@/stores/alertStore'

/**
 * Composable for managing alerts throughout the application
 */
export function useAlert() {
  const alertStore = useAlertStore()

  const showAlert = (options: AlertOptions) => {
    return alertStore.addAlert(options)
  }

  const showError = (message: string, options?: Omit<AlertOptions, 'message' | 'type'>) => {
    return alertStore.showError(message, options)
  }

  const showWarning = (message: string, options?: Omit<AlertOptions, 'message' | 'type'>) => {
    return alertStore.showWarning(message, options)
  }

  const showInfo = (message: string, options?: Omit<AlertOptions, 'message' | 'type'>) => {
    return alertStore.showInfo(message, options)
  }

  const showSuccess = (message: string, options?: Omit<AlertOptions, 'message' | 'type'>) => {
    return alertStore.showSuccess(message, options)
  }

  const showHttpError = (error: unknown, customMessage?: string) => {
    return alertStore.showHttpError(error, customMessage)
  }

  const dismissAlert = (id: string) => {
    alertStore.dismissAlert(id)
  }

  const clearAllAlerts = () => {
    alertStore.clearAllAlerts()
  }

  const alerts = alertStore.alerts

  return {
    showAlert,
    showError,
    showWarning,
    showInfo,
    showSuccess,
    showHttpError,
    dismissAlert,
    clearAllAlerts,
    alerts,
  }
}
