import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'jobs',
  product: 'jobs',
  // Each state's countKey is its JobStatus name, the key the module's counts answer uses.
  sidebar: [
    { entries: [{ id: 'jobs.nav.dashboard', label: 'Dashboard', route: '', icon: 'dashboard' }] },
    {
      label: 'States',
      entries: [
        { id: 'jobs.nav.idle', label: 'Idle', route: 'runs?status=Idle', state: 'sched', countKey: 'Idle' },
        { id: 'jobs.nav.queued', label: 'Queued', route: 'runs?status=Queued', state: 'enq', countKey: 'Queued' },
        { id: 'jobs.nav.in-progress', label: 'In progress', route: 'runs?status=InProgress', state: 'proc', countKey: 'InProgress' },
        { id: 'jobs.nav.succeeded', label: 'Succeeded', route: 'runs?status=Succeeded', state: 'ok', countKey: 'Succeeded' },
        { id: 'jobs.nav.due-done', label: 'Due done', route: 'runs?status=DueDone', state: 'ok', countKey: 'DueDone' },
        { id: 'jobs.nav.failed', label: 'Failed', route: 'runs?status=Failed', state: 'fail', countKey: 'Failed' },
        { id: 'jobs.nav.cancelled', label: 'Cancelled', route: 'runs?status=Cancelled', state: 'off', countKey: 'Cancelled' },
        { id: 'jobs.nav.skipped', label: 'Skipped', route: 'runs?status=Skipped', state: 'off', countKey: 'Skipped' },
        { id: 'jobs.nav.runs', label: 'All runs', route: 'runs', icon: 'runs' },
      ],
    },
    {
      label: 'Definitions',
      entries: [
        { id: 'jobs.nav.cron-jobs', label: 'Recurring jobs', route: 'cron-jobs', icon: 'recurring' },
        { id: 'jobs.nav.functions', label: 'Functions', route: 'functions', icon: 'function' },
      ],
    },
    { label: 'Hosts', entries: [{ id: 'jobs.nav.nodes', label: 'Servers', route: 'nodes', icon: 'server' }] },
  ],
  routes: [
    { path: '', lazy: () => import('./views/dashboard.tsx') },
    { path: 'runs', lazy: () => import('./views/runs.tsx') },
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
    node: { route: (id: string) => `nodes?open=${encodeURIComponent(id)}` },
  },
  commands: [
    {
      id: 'jobs.go-runs',
      label: 'Go to runs',
      run: ({ navigate }) => navigate('runs'),
    },
    {
      id: 'jobs.requeue-failed',
      label: 'Requeue failed runs',
      requires: 'operate',
      // The runs view starts the Operation from this search parameter, so the palette and the view share one flow.
      run: ({ navigate }) => navigate('runs?status=Failed&operation=requeue'),
    },
    {
      id: 'jobs.add-time-job',
      label: 'Add time job',
      requires: 'operate',
      run: ({ navigate }) => navigate('runs?create=time-job'),
    },
    {
      id: 'jobs.add-chain',
      label: 'Add chain',
      requires: 'operate',
      run: ({ navigate }) => navigate('runs?create=chain'),
    },
    {
      id: 'jobs.add-cron-job',
      label: 'Add cron job',
      requires: 'operate',
      run: ({ navigate }) => navigate('cron-jobs?create=cron-job'),
    },
  ],
})
