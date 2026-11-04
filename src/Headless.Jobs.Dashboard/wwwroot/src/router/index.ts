import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/authStore'
import { getBasePath, requiresAuthentication } from '@/utilities/pathResolver'

const router = createRouter({
  history: createWebHistory(getBasePath()),
  routes: [
    {
      path: '/login',
      name: 'Login',
      component: () => import('../views/Login.vue'),
      meta: { requiresAuth: false, hideForAuthenticated: true }
    },
    {
      path: '/',
      name: 'Dashboard',
      component: () => import("../views/Dashboard.vue"),
      meta: { requiresAuth: true }
    },
    {
      path: '/cron-jobs',
      name: 'CronJob',
      component: () => import('../views/CronJob.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/time-jobs',
      name: 'TimeJob',
      component: () => import('../views/TimeJob.vue'),
      meta: { requiresAuth: true }
    },
  ],
})

// Navigation guard for authentication
router.beforeEach(async (to) => {
  // If no authentication is required globally, allow all routes
  if (!requiresAuthentication()) {
    return true;
  }

  // The first navigation learns the session (in host mode, from the server) before any route renders, so the login
  // page can also send a signed-in user on.
  const authStore = useAuthStore();
  if (!authStore.isInitialized) {
    await authStore.initializeAuth();
  }

  if (to.name === 'Login' || authStore.isLoggedIn) {
    return true;
  }

  return { name: 'Login', query: { redirect: to.fullPath } };
})

export default router
