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
      meta: { requiresAuth: false, hideForAuthenticated: true },
    },
    {
      path: '/',
      name: 'Dashboard',
      component: () => import('../views/Dashboard.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/published',
      name: 'Published',
      component: () => import('../views/Published.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/received',
      name: 'Received',
      component: () => import('../views/Received.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/subscribers',
      name: 'Subscribers',
      component: () => import('../views/Subscribers.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/nodes',
      name: 'Nodes',
      component: () => import('../views/Nodes.vue'),
      meta: { requiresAuth: true },
    },
  ],
})

router.beforeEach(async (to, _from, next) => {
  if (to.name === 'Login') {
    next()
    return
  }

  const authRequired = requiresAuthentication()

  if (!authRequired) {
    next()
    return
  }

  const authStore = useAuthStore()

  if (!authStore.isInitialized) {
    try {
      await authStore.initializeAuth()
    } catch (error) {
      console.error('Auth initialization failed:', error)
      next({ name: 'Login' })
      return
    }
  }

  if (authStore.isLoggedIn) {
    next()
  } else {
    next({ name: 'Login', query: { redirect: to.fullPath } })
  }
})

export default router
