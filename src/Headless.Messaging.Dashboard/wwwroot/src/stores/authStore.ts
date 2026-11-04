import { defineStore } from 'pinia'
import { ref, computed, reactive } from 'vue'
import { authService, type AuthStatus, type LoginCredentials } from '@/services/auth'

const SESSION_ENDED_MESSAGE = 'Your session ended. Please sign in again.'

export const useAuthStore = defineStore('auth', () => {
  // State
  const isInitialized = ref(false)
  const authStatus = ref<AuthStatus>({
    authenticated: false,
    username: '',
    message: '',
    canOperate: false,
  })
  const errorMessage = ref('')
  const isLoading = ref(false)

  const credentials = reactive<LoginCredentials>({
    username: '',
    password: '',
    apiKey: '',
  })

  // Computed properties
  const isLoggedIn = computed(() => isInitialized.value && authStatus.value.authenticated)
  const username = computed(() => authStatus.value.username || '')
  const canOperate = computed(
    () =>
      !window.MessagingConfig?.auth?.enabled || (isLoggedIn.value && authStatus.value.canOperate),
  )

  const clearForm = () => {
    credentials.username = ''
    credentials.password = ''
    credentials.apiKey = ''
  }

  // Actions
  const initializeAuth = async () => {
    try {
      isLoading.value = true
      await authService.initialize()
      authStatus.value = authService.getStatus()
    } catch (error) {
      console.error('Auth initialization failed:', error)
      authStatus.value = {
        authenticated: false,
        username: '',
        message: 'Authentication service unavailable',
        canOperate: false,
      }
      errorMessage.value = 'Failed to initialize authentication'
    } finally {
      isInitialized.value = true
      isLoading.value = false
    }
  }

  const login = async (): Promise<boolean> => {
    try {
      isLoading.value = true
      errorMessage.value = ''

      const success = await authService.login(credentials)
      authStatus.value = authService.getStatus()

      if (success) {
        clearForm()
        return true
      }

      errorMessage.value = authStatus.value.message || 'Login failed'
      return false
    } catch (error) {
      console.error('Login error:', error)
      errorMessage.value = 'Login failed'
      authStatus.value = {
        authenticated: false,
        username: '',
        message: 'Login failed',
        canOperate: false,
      }
      return false
    } finally {
      isLoading.value = false
    }
  }

  /** Throws, leaving the session in place, when the server did not end it. */
  const logout = async () => {
    await authService.signOut()
    authStatus.value = authService.getStatus()
    clearForm()
    errorMessage.value = ''
  }

  const revalidate = async () => {
    try {
      await authService.validateStoredCredentials()
      authStatus.value = authService.getStatus()
    } catch (error) {
      console.error('Revalidation failed:', error)
      authStatus.value = {
        authenticated: false,
        username: '',
        message: 'Revalidation failed',
        canOperate: false,
      }
    }
  }

  /** The server answered 401, so the session is already gone; this only tells the user why they are back. */
  const handle401Error = () => {
    authService.clearSession()
    clearForm()

    // Only someone who had a session can lose it; a first visit lands on the sign-in page without that message, and
    // a later 401 of the same burst leaves the first one's message in place.
    if (authStatus.value.authenticated) {
      authStatus.value = {
        authenticated: false,
        username: '',
        message: SESSION_ENDED_MESSAGE,
        canOperate: false,
      }
      errorMessage.value = SESSION_ENDED_MESSAGE
    }
  }

  const signInUrl = (returnPath?: string) => authService.getSignInUrl(returnPath)

  const clearError = () => {
    errorMessage.value = ''
  }

  return {
    // State
    isInitialized,
    authStatus,
    errorMessage,
    isLoading,
    credentials,

    // Computed
    isLoggedIn,
    username,
    canOperate,

    // Actions
    initializeAuth,
    login,
    logout,
    revalidate,
    handle401Error,
    signInUrl,
    clearError,
  }
})
