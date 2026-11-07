import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'leaky',
  routes: [{ path: '', lazy: () => import('./views/home.tsx') }],
})
