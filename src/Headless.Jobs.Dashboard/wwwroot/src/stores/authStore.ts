import { defineStore } from 'pinia';
import { ref, computed, reactive } from 'vue';
import { authService, SESSION_EXPIRED_MESSAGE, type AuthStatus, type LoginCredentials } from '@/services/auth';

export const useAuthStore = defineStore('auth', () => {
  // State
  const isInitialized = ref(false);
  const authStatus = ref<AuthStatus>({
    authenticated: false,
    username: '',
    message: ''
  });
  const errorMessage = ref('');
  const isLoading = ref(false);
  const forceUpdate = ref(0); // Force reactivity trigger

  // Form credentials
  const credentials = reactive<LoginCredentials>({
    username: '',
    password: '',
    apiKey: '',
    hostAccessKey: '',
    customCredential: ''
  });

  // Computed properties
  const isLoggedIn = computed(() => {
    void forceUpdate.value; // Force reactivity
    if (!isInitialized.value) return false;
    return authStatus.value.authenticated;
  });

  const username = computed(() => authStatus.value.username || '');

  // Actions
  const initializeAuth = async () => {
    try {
      isLoading.value = true;
      
      // Initialize the auth service
      await authService.initialize();
      
      // Get the current auth status
      authStatus.value = authService.getStatus();
      
    } catch (error) {
      console.error('Auth initialization failed:', error);
      authStatus.value = {
        authenticated: false,
        username: '',
        message: 'Authentication service unavailable'
      };
      errorMessage.value = 'Failed to initialize authentication';
    } finally {
      isInitialized.value = true;
      isLoading.value = false;
    }
  };

  const login = async (): Promise<boolean> => {
    try {
      isLoading.value = true;
      errorMessage.value = '';
      
      console.log('🔐 Attempting login...');
      
      const success = await authService.login(credentials);
      
      if (success) {
        authStatus.value = authService.getStatus();
        console.log('✅ Login successful:', authStatus.value);
        
        // Clear form
        credentials.username = '';
        credentials.password = '';
        credentials.apiKey = '';
        credentials.customCredential = '';
        
        return true;
      } else {
        authStatus.value = authService.getStatus();
        errorMessage.value = authStatus.value.message || 'Login failed';
        console.log('❌ Login failed:', authStatus.value);
        return false;
      }
    } catch (error) {
      console.error('❌ Login error:', error);
      errorMessage.value = 'Login failed';
      authStatus.value = {
        authenticated: false,
        username: '',
        message: 'Login failed'
      };
      return false;
    } finally {
      isLoading.value = false;
    }
  };

  const logout = async () => {
    try {
      console.log('🚪 Logging out...');
      
      await authService.logout();
      authStatus.value = authService.getStatus();
      
      // Clear form and errors
      credentials.username = '';
      credentials.password = '';
      credentials.apiKey = '';
      credentials.customCredential = '';
      errorMessage.value = '';
      
      console.log('✅ Logout successful');
    } catch (error) {
      console.error('❌ Logout error:', error);
    }
  };

  const revalidate = async () => {
    try {
      console.log('🔄 Revalidating authentication...');
      
      await authService.validateStoredCredentials();
      authStatus.value = authService.getStatus();
      
      console.log('✅ Revalidation complete:', authStatus.value);
    } catch (error) {
      console.error('❌ Revalidation failed:', error);
      authStatus.value = {
        authenticated: false,
        username: '',
        message: 'Revalidation failed'
      };
    }
  };

  // Signs the user out and leaves the expiry message for the login page to show.
  const markSessionExpired = () => {
    authStatus.value = {
      authenticated: false,
      username: '',
      message: SESSION_EXPIRED_MESSAGE
    };

    // Clear form
    credentials.username = '';
    credentials.password = '';
    credentials.apiKey = '';
    credentials.customCredential = '';
    errorMessage.value = SESSION_EXPIRED_MESSAGE;

    // Force reactivity update
    forceUpdate.value++;
  };

  const handle401Error = () => {
    console.log('🚨 Handling 401 error - clearing credentials');
    
    // Clear credentials and status
    authService.logout();
    markSessionExpired();
  };

  /**
   * Ends the session when the stored sign-in has outlived the configured session timeout.
   * @returns true when the session expired; the caller sends the user to the login page.
   */
  const enforceSessionTimeout = (): boolean => {
    if (!authService.expireStaleSession()) {
      return false;
    }

    markSessionExpired();
    return true;
  };

  const clearError = () => {
    errorMessage.value = '';
  };

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
    
    // Actions
    initializeAuth,
    login,
    logout,
    revalidate,
    handle401Error,
    enforceSessionTimeout,
    clearError
  };
});