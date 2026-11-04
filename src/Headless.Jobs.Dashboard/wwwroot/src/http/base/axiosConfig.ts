import { useAuthStore } from '@/stores/authStore';
import { useAlertStore } from '@/stores/alertStore';
import axios, { AxiosError, type AxiosInstance, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { getApiBaseUrl } from '@/utilities/pathResolver';
import { getAuthorizationHeader } from '@/services/auth';

const axiosInstance: AxiosInstance = axios.create({
  baseURL: getApiBaseUrl(),
});

// Request Interceptor: mark the request and attach the stored credential
axiosInstance.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    // The server refuses a state-changing request without this header: a cross-site form cannot send it.
    config.headers.set('X-Requested-With', 'XMLHttpRequest');

    // Basic and API key send their stored credential. Host mode sends none: the browser carries the session cookie.
    const authorization = getAuthorizationHeader();
    if (authorization) {
      config.headers.set('Authorization', authorization);
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
        // The session or credential is gone: back to the login page, which returns here after signing in.
        await useAuthStore().handleUnauthorized();

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