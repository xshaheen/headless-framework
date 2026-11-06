import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'leaky',
  product: 'diagnostics',
  routes: [{ path: '', lazy: () => import('./views/home.tsx') }],
})
