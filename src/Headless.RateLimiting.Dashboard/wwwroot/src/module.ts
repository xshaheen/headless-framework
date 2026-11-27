import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'rate-limiting',
  product: 'platform',
  // The count is the number of purposes the app declares; the screen looks one subject up, so no list is counted.
  sidebar: [
    {
      label: 'Operations',
      entries: [
        {
          id: 'rate-limiting.nav.rate-limits',
          label: 'Rate limits',
          route: '',
          icon: 'gauge',
          countKey: 'purposes',
        },
      ],
    },
  ],
  routes: [{ path: '', lazy: () => import('./views/rate-limits.tsx') }],
})
