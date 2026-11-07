import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'diagnostics',
  nav: [{ id: 'diagnostics.nav.runs', label: 'Runs', to: '' }],
  routes: [
    { path: '', lazy: () => import('./views/runs-list.tsx') },
    { path: 'runs/:runId', lazy: () => import('./views/run-page.tsx') },
  ],
  objectKinds: {
    run: { route: (id: string) => `runs/${encodeURIComponent(id)}` },
  },
  commands: [
    {
      id: 'diagnostics.requeue-failed',
      label: 'Requeue failed runs',
      requires: 'operate',
      // The runs view starts the Operation from this search parameter, so the palette and the view share one flow.
      run: ({ navigate }) => navigate('?status=Failed&operation=requeue'),
    },
  ],
})
