import { useAuthStore } from '@/stores/authStore';
import { useAlertStore } from '@/stores/alertStore';
import axios, { AxiosError, CanceledError, type AxiosInstance, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { getApiBaseUrl } from '@/utilities/pathResolver';

const axiosInstance: AxiosInstance = axios.create({
  baseURL: getApiBaseUrl(),
});

// Request Interceptor: Set Authorization header
axiosInstance.interceptors.request.use(
  async (config: InternalAxiosRequestConfig) => {
    // An expired session must not reach the server with its stale credential.
    if (useAuthStore().enforceSessionTimeout()) {
      // Imported lazily: the router module imports the auth store, and this module loads before the app is wired.
      const { default: router } = await import('@/router');
      const current = router.currentRoute.value;
      if (current.name !== 'Login') {
        await router.push({ name: 'Login', query: { redirect: current.fullPath } });
      }
      throw new CanceledError('Session expired; request canceled', undefined, config);
    }

    // Get auth headers from localStorage (using correct keys)
    const apiKey = localStorage.getItem('jobs_api_key');
    const basicAuth = localStorage.getItem('jobs_basic_auth');
    const hostAccessKey = localStorage.getItem('jobs_host_access_key');
    const customCredential = localStorage.getItem('jobs_custom_credential');
    
    if (apiKey) {
      config.headers = config.headers || {};
      if (typeof config.headers.set === 'function') {
        config.headers.set('Authorization', `Bearer ${apiKey}`);
      } else {
        config.headers['Authorization'] = `Bearer ${apiKey}`;
      }
    } else if (basicAuth) {
      config.headers = config.headers || {};
      if (typeof config.headers.set === 'function') {
        config.headers.set('Authorization', `Basic ${basicAuth}`);
      } else {
        config.headers['Authorization'] = `Basic ${basicAuth}`;
      }
    } else if (hostAccessKey) {
      config.headers = config.headers || {};
      if (typeof config.headers.set === 'function') {
        config.headers.set('Authorization', hostAccessKey);
      } else {
        config.headers['Authorization'] = hostAccessKey;
      }
    } else if (customCredential) {
      // Custom mode sends the credential verbatim; the host's validator receives exactly this header value.
      config.headers = config.headers || {};
      if (typeof config.headers.set === 'function') {
        config.headers.set('Authorization', customCredential);
      } else {
        config.headers['Authorization'] = customCredential;
      }
    }

    return config;
  },
  (error: AxiosError) => {
    return Promise.reject(error);
  }
);

// Response Interceptor: Handle errors and auth
axiosInstance.interceptors.response.use(
  (response: AxiosResponse) => response,
  async (error: AxiosError) => {
    try {
      // Handle 401 authentication errors
      if (error.response?.status === 401) {
        console.log('401 Unauthorized - handling authentication failure');
        
        // Get auth store and handle 401
        const authStore = useAuthStore();
        await authStore.handle401Error();
        
        // Don't show alert for 401 errors - let the auth system handle it
        return Promise.reject(error);
      }

      // Show error alert for other HTTP errors
      if (!axios.isCancel(error) && !error.message?.includes('canceled')) {
        const alertStore = useAlertStore();
        alertStore.showHttpError(error);
      }
    } catch (alertError) {
      // Prevent infinite loops if alert system has issues
      console.error('Error in response interceptor:', alertError);
    }

    return Promise.reject(error);
  }
);

export default axiosInstance;