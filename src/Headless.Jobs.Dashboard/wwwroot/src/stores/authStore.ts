import { defineStore } from 'pinia';
import { ref, computed, reactive } from 'vue';
import { authService, SESSION_EXPIRED_MESSAGE, type AuthStatus, type LoginCredentials } from '@/services/auth';
import { requiresAuthentication } from '@/utilities/pathResolver';

export const useAuthStore = defineStore('auth', () => {
  // State
  const isInitialized = ref(false);
  const authStatus = ref<AuthStatus>({ authenticated: false, username: '', message: '', canOperate: false });
  const errorMessage = ref('');
  const isLoading = ref(false);
  // Set by an explicit sign-out, so the login page says so instead of asking to sign in as if for the first time.
  const signedOut = ref(false);

  const credentials = reactive<LoginCredentials>({
    username: '',
    password: '',
    apiKey: '',
  });

  // Computed properties
  const isLoggedIn = computed(() => isInitialized.value && authStatus.value.authenticated);

  const username = computed(() => authStatus.value.username || '');

  const canOperate = computed(
    () => !requiresAuthentication() || (isLoggedIn.value && authStatus.value.canOperate),
  );

  const clearForm = () => {
    credentials.username = '';
    credentials.password = '';
    credentials.apiKey = '';
  };

  // Actions
  const initializeAuth = async () => {
    try {
      isLoading.value = true;
      await authService.initialize();
      authStatus.value = authService.getStatus();
    } catch (error) {
      console.error('Auth initialization failed:', error);
      authStatus.value = {
        authenticated: false,
        username: '',
        message: 'Authentication service unavailable',
        canOperate: false,
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

      const success = await authService.login(credentials);
      authStatus.value = authService.getStatus();

      if (success) {
        signedOut.value = false;
        clearForm();
        return true;
      }

      errorMessage.value = authStatus.value.message || 'Login failed';
      return false;
    } catch (error) {
      console.error('Login error:', error);
      errorMessage.value = 'Login failed';
      authStatus.value = { authenticated: false, username: '', message: 'Login failed', canOperate: false };
      return false;
    } finally {
      isLoading.value = false;
    }
  };

  /** Throws, leaving the session in place, when the server did not end it. */
  const logout = async () => {
    await authService.logout();

    authStatus.value = authService.getStatus();
    signedOut.value = true;
    clearForm();
    errorMessage.value = '';
  };

  /**
   * The server answered 401 (an API call, or the notification hub reconnecting): the session is gone. Forgets it and
   * sends the user to the login page, which comes back to the current page after signing in.
   */
  // Requests in flight when the session ends all answer 401; only the first may change state or navigate.
  let redirecting: Promise<unknown> | null = null;

  const handleUnauthorized = async () => {
    authService.expire();
    clearForm();

    // Only someone who had a session can lose it; a first visit lands on the sign-in page without that message, and
    // a later 401 of the same burst leaves the first one's message in place.
    if (authStatus.value.authenticated) {
      authStatus.value = authService.getStatus();
      errorMessage.value = SESSION_EXPIRED_MESSAGE;
    }

    // Imported lazily: the router module imports this store.
    const { default: router } = await import('@/router');
    if (redirecting) {
      await redirecting;
      return;
    }

    const current = router.currentRoute.value;
    if (current.name !== 'Login') {
      redirecting = (async () => {
        try {
          await router.push({ name: 'Login', query: { redirect: current.fullPath } });
        } finally {
          redirecting = null;
        }
      })();
      await redirecting;
    }
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
    signedOut,

    // Computed
    isLoggedIn,
    username,
    canOperate,

    // Actions
    initializeAuth,
    login,
    logout,
    handleUnauthorized,
    clearError,
  };
});
