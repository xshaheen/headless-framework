import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'sequences',
  product: 'platform',
  // `counters` is the number of stored counters the module's counts answer serves, capped at 10,000.
  sidebar: [
    {
      label: 'Data',
      entries: [{ id: 'sequences.nav.sequences', label: 'Sequences', route: '', icon: 'hash', countKey: 'counters' }],
    },
  ],
  routes: [{ path: '', lazy: () => import('./views/sequences.tsx') }],
  objectKinds: {
    'sequence-counter': { route: (id: string) => `?open=${encodeURIComponent(id)}` },
  },
})
