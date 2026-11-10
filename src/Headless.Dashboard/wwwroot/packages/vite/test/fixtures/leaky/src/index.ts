import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'leaky',
  product: 'platform',
  routes: [{ path: '', lazy: () => import('./views/home.tsx') }],
})
