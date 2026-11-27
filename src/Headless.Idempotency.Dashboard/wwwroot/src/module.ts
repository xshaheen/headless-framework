import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'idempotency',
  product: 'platform',
  // No count: keys cannot be listed, so the screen looks one up and counts nothing.
  sidebar: [
    {
      label: 'Operations',
      entries: [
        {
          id: 'idempotency.nav.idempotency',
          label: 'Idempotency',
          route: '',
          icon: 'repeat',
        },
      ],
    },
  ],
  routes: [{ path: '', lazy: () => import('./views/idempotency.tsx') }],
})
