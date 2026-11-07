import { defineModule } from '@headless/dashboard-sdk'

function patchRun(payload: unknown) {
  return payload
}

export default defineModule({
  id: 'fixture',
  nav: [
    { id: 'fixture.nav.runs', label: 'Runs', to: 'runs', requires: 'view' },
    { id: 'fixture.nav.cron', label: 'Cron jobs', to: 'cron' },
  ],
  routes: [
    { path: 'runs', lazy: () => import('./views/runs.tsx') },
    { path: 'cron', lazy: () => import('./views/cron.tsx') },
  ],
  objectKinds: { run: { route: (id) => `runs/${id}` } },
  commands: [{ id: 'fixture.add-run', label: 'Add run', requires: 'operate', run: (context) => context.navigate('runs') }],
  events: { 'run.updated': patchRun, 'run.progress': patchRun },
})
