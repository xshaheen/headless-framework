import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'jobs',
  nav: [
    { id: 'jobs.nav.runs', label: 'Runs', to: '' },
    { id: 'jobs.nav.cron-jobs', label: 'Cron jobs', to: 'cron-jobs' },
    { id: 'jobs.nav.functions', label: 'Functions', to: 'functions' },
    { id: 'jobs.nav.nodes', label: 'Nodes', to: 'nodes' },
  ],
  routes: [
    { path: '', lazy: () => import('./views/runs.tsx') },
    { path: 'runs/:runId', lazy: () => import('./views/run-page.tsx') },
    { path: 'cron-jobs', lazy: () => import('./views/cron-jobs.tsx') },
    { path: 'cron-jobs/:cronJobId', lazy: () => import('./views/cron-job.tsx') },
    { path: 'functions', lazy: () => import('./views/functions.tsx') },
    { path: 'functions/:name', lazy: () => import('./views/function-page.tsx') },
    { path: 'nodes', lazy: () => import('./views/nodes.tsx') },
  ],
  objectKinds: {
    run: { route: (id: string) => `runs/${encodeURIComponent(id)}` },
    'cron-job': { route: (id: string) => `cron-jobs/${encodeURIComponent(id)}` },
    function: { route: (id: string) => `functions/${encodeURIComponent(id)}` },
    node: { route: (id: string) => `nodes?selected=${encodeURIComponent(id)}` },
  },
  commands: [
    {
      id: 'jobs.go-runs',
      label: 'Go to runs',
      run: ({ navigate }) => navigate(''),
    },
    {
      id: 'jobs.requeue-failed',
      label: 'Requeue failed runs',
      requires: 'operate',
      // The runs view starts the Operation from this search parameter, so the palette and the view share one flow.
      run: ({ navigate }) => navigate('?status=Failed&operation=requeue'),
    },
    {
      id: 'jobs.add-time-job',
      label: 'Add time job',
      requires: 'operate',
      run: ({ navigate }) => navigate('?create=time-job'),
    },
    {
      id: 'jobs.add-chain',
      label: 'Add chain',
      requires: 'operate',
      run: ({ navigate }) => navigate('?create=chain'),
    },
    {
      id: 'jobs.add-cron-job',
      label: 'Add cron job',
      requires: 'operate',
      run: ({ navigate }) => navigate('cron-jobs?create=cron-job'),
    },
  ],
})
