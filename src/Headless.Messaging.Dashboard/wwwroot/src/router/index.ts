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
      meta: { requiresAuth: false },
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
      path: '/scheduled',
      name: 'Scheduled',
      component: () => import('../views/Scheduled.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/unknown-lanes',
      name: 'UnknownLanes',
      component: () => import('../views/UnknownLanes.vue'),
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
    {
      path: '/:pathMatch(.*)*',
      redirect: '/',
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

// The server answered 401: its session ended (host mode) or it refused the stored credential. Ask to sign in again and
// come back to the page the operator was on.
window.addEventListener('auth:logout', () => {
  useAuthStore().handle401Error()

  const current = router.currentRoute.value
  if (current.name !== 'Login') {
    void router.push({ name: 'Login', query: { redirect: current.fullPath } })
  }
})

export default router
