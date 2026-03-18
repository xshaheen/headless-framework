import { computed, ref, watch, readonly } from 'vue'
import { useAuthStore } from '../stores/authStore'

export interface AuthCredentials {
  username: string
  password: string
}

export interface AuthResult {
  success: boolean
  error?: string
}

export function useAuth() {
  const isLoading = ref(false)
  const error = ref('')

  const authStore = useAuthStore()

  const isAuthenticated = computed(() => {
    try {
      return authStore.isLoggedIn
    } catch {
      return false
    }
  })

  const username = computed(() => {
    try {
      return authStore.username
    } catch {
      return ''
    }
  })

  const authToken = computed(() => {
    try {
      const apiKey = localStorage.getItem('messaging_api_key')
      const basicAuth = localStorage.getItem('messaging_basic_auth')
      const hostAccessKey = localStorage.getItem('messaging_host_access_key')
      return apiKey || basicAuth || hostAccessKey || ''
    } catch {
      return ''
    }
  })

  const errorMessage = computed(() => {
    try {
      return authStore.errorMessage || error.value
    } catch {
      return error.value
    }
  })

  const login = async (credentials: AuthCredentials): Promise<AuthResult> => {
    isLoading.value = true
    error.value = ''

    try {
      authStore.credentials.username = credentials.username
      authStore.credentials.password = credentials.password

      const success = await authStore.login()

      if (!success) {
        error.value = authStore.errorMessage || 'Invalid username or password'
        return { success: false, error: error.value }
      } else {
        error.value = ''
        return { success: true }
      }
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : 'An unexpected error occurred'
      error.value = errorMsg
      return { success: false, error: errorMsg }
    } finally {
      isLoading.value = false
    }
  }

  const logout = async (): Promise<void> => {
    isLoading.value = true
    error.value = ''

    try {
      authStore.logout()
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : 'Logout failed'
      error.value = errorMsg
      throw err
    } finally {
      isLoading.value = false
    }
  }

  const clearError = (): void => {
    error.value = ''
    try {
      authStore.clearError()
    } catch {
      // Store not available
    }
  }

  const autoClearError = (): void => {
    if (error.value) {
      setTimeout(() => {
        clearError()
      }, 5000)
    }
  }

  watch(error, autoClearError)
  watch(errorMessage, autoClearError)

  return {
    isLoading: readonly(isLoading),
    error: readonly(error),
    isAuthenticated,
    username,
    authToken,
    errorMessage,
    login,
    logout,
    clearError,
  }
}
