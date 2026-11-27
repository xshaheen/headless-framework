import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'leases',
  product: 'platform',
  // The count answers live fenced leases, or held locks on a host without Fencing; a host that serves neither half does
  // not start.
  sidebar: [
    {
      label: 'Operations',
      entries: [{ id: 'leases.nav.leases', label: 'Leases', route: '', icon: 'lock', countKey: 'leases' }],
    },
  ],
  routes: [
    { path: '', lazy: () => import('./views/fenced.tsx') },
    { path: 'locks', lazy: () => import('./views/locks.tsx') },
  ],
  objectKinds: {
    'fenced-lease': { route: (id: string) => `?open=${encodeURIComponent(id)}` },
    lock: { route: (id: string) => `locks?open=${encodeURIComponent(id)}` },
  },
})
