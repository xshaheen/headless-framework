import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'lazy-entries',
  product: 'jobs',
  routes: [{ path: '', lazy: () => import('./views/home.tsx') }],
})
