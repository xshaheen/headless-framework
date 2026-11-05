import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'diagnostics',
  nav: [{ id: 'diagnostics.nav.home', label: 'Diagnostics', to: '' }],
  routes: [{ path: '', lazy: () => import('./views/home.tsx') }],
})
