import { defineModule } from '@headless/dashboard-sdk'

function patchRun(payload: unknown) {
  return payload
}

export default defineModule({
  id: 'fixture',
  product: 'jobs',
  sidebar: [
    { entries: [{ id: 'fixture.nav.runs', label: 'Runs', route: 'runs', icon: 'runs', requires: 'view' }] },
    {
      label: 'States',
      entries: [
        { id: 'fixture.nav.failed', label: 'Failed', route: 'runs?status=Failed', state: 'fail', countKey: 'Failed' },
        { id: 'fixture.nav.cron', label: 'Cron jobs', route: 'cron', icon: 'cron' },
      ],
    },
  ],
  routes: [
    { path: 'runs', lazy: () => import('./views/runs.tsx') },
    { path: 'cron', lazy: () => import('./views/cron.tsx') },
  ],
  objectKinds: { run: { route: (id) => `runs/${id}` } },
  commands: [{ id: 'fixture.add-run', label: 'Add run', requires: 'operate', run: (context) => context.navigate('runs') }],
  events: { 'run.updated': patchRun, 'run.progress': patchRun },
})
