import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'cluster',
  product: 'cluster',
  // Each countKey is a key the module's counts answer serves; a host that does not serve a half answers none of its
  // keys, so those entries read as unknown rather than zero.
  sidebar: [
    {
      label: 'Nodes',
      entries: [
        { id: 'cluster.nav.nodes', label: 'All', route: '', icon: 'nodes', countKey: 'nodes' },
        { id: 'cluster.nav.alive', label: 'Alive', route: '?state=Alive', state: 'ok', countKey: 'Alive' },
        {
          id: 'cluster.nav.suspected',
          label: 'Suspected',
          route: '?state=Suspected',
          state: 'proc',
          countKey: 'Suspected',
        },
        { id: 'cluster.nav.dead', label: 'Dead', route: '?state=Dead', state: 'fail', countKey: 'Dead' },
      ],
    },
    {
      label: 'Health',
      entries: [
        { id: 'cluster.nav.checks', label: 'Checks', route: 'health', icon: 'heart-pulse', countKey: 'checks' },
        {
          id: 'cluster.nav.degraded',
          label: 'Degraded',
          route: 'health?status=Degraded',
          state: 'proc',
          countKey: 'Degraded',
        },
        {
          id: 'cluster.nav.unhealthy',
          label: 'Unhealthy',
          route: 'health?status=Unhealthy',
          state: 'fail',
          countKey: 'Unhealthy',
        },
      ],
    },
  ],
  routes: [
    { path: '', lazy: () => import('./views/cluster.tsx') },
    { path: 'health', lazy: () => import('./views/health.tsx') },
  ],
  objectKinds: {
    node: { route: (id: string) => `?open=${encodeURIComponent(id)}` },
    check: { route: (id: string) => `health?open=${encodeURIComponent(id)}` },
    health: { route: () => 'health' },
  },
})
