import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'messaging',
  product: 'messaging',
  // Each countKey is a key the module's counts answer serves; the routes carry the filter that count counts.
  sidebar: [
    { entries: [{ id: 'messaging.nav.dashboard', label: 'Dashboard', route: '', icon: 'dashboard' }] },
    {
      label: 'Published',
      entries: [
        { id: 'messaging.nav.published', label: 'All published', route: 'published', icon: 'runs', countKey: 'published.all' },
        { id: 'messaging.nav.published-succeeded', label: 'Succeeded', route: 'published?status=Succeeded', state: 'ok', countKey: 'published.succeeded' },
        {
          id: 'messaging.nav.published-failed',
          label: 'Failed',
          route: 'published?status=Failed&terminal=true',
          state: 'fail',
          countKey: 'published.failed',
        },
        { id: 'messaging.nav.published-retrying', label: 'Retrying', route: 'published?retrying=true', state: 'proc', countKey: 'published.retrying' },
        { id: 'messaging.nav.published-delayed', label: 'Delayed', route: 'published?status=Delayed', state: 'sched', countKey: 'published.delayed' },
        { id: 'messaging.nav.published-scheduled', label: 'Scheduled', route: 'published?status=Scheduled', state: 'sched', countKey: 'published.scheduled' },
        { id: 'messaging.nav.published-queued', label: 'Queued', route: 'published?status=Queued', state: 'enq', countKey: 'published.queued' },
        { id: 'messaging.nav.scheduled', label: 'Scheduled deliveries', route: 'scheduled', icon: 'recurring' },
      ],
    },
    {
      label: 'Received',
      entries: [
        { id: 'messaging.nav.received', label: 'All received', route: 'received', icon: 'runs', countKey: 'received.all' },
        { id: 'messaging.nav.received-succeeded', label: 'Succeeded', route: 'received?status=Succeeded', state: 'ok', countKey: 'received.succeeded' },
        {
          id: 'messaging.nav.received-failed',
          label: 'Failed',
          route: 'received?status=Failed&terminal=true',
          state: 'fail',
          countKey: 'received.failed',
        },
        { id: 'messaging.nav.received-retrying', label: 'Retrying', route: 'received?retrying=true', state: 'proc', countKey: 'received.retrying' },
        { id: 'messaging.nav.received-held', label: 'Held', route: 'received?held=true', state: 'off', countKey: 'received.held' },
      ],
    },
    {
      label: 'Definitions',
      entries: [
        { id: 'messaging.nav.consumers', label: 'Consumers', route: 'consumers', icon: 'function' },
        { id: 'messaging.nav.contracts', label: 'Contracts', route: 'contracts', icon: 'runs' },
      ],
    },
    {
      label: 'Hosts',
      entries: [
        { id: 'messaging.nav.nodes', label: 'Nodes', route: 'nodes', icon: 'server' },
        { id: 'messaging.nav.unknown-lanes', label: 'Unknown lanes', route: 'unknown-lanes', state: 'fail', countKey: 'hosts.unknown-lanes' },
        { id: 'messaging.nav.operator-log', label: 'Operator log', route: 'operator-log', icon: 'runs' },
      ],
    },
  ],
  routes: [
    { path: '', lazy: () => import('./views/dashboard.tsx') },
    { path: 'published', lazy: () => import('./views/published.tsx') },
    { path: 'received', lazy: () => import('./views/received.tsx') },
    { path: 'scheduled', lazy: () => import('./views/scheduled.tsx') },
    { path: 'consumers', lazy: () => import('./views/consumers.tsx') },
    { path: 'contracts', lazy: () => import('./views/contracts.tsx') },
    { path: 'nodes', lazy: () => import('./views/nodes.tsx') },
    { path: 'unknown-lanes', lazy: () => import('./views/unknown-lanes.tsx') },
    { path: 'operator-log', lazy: () => import('./views/operator-log.tsx') },
  ],
  // A message id names its node, so the shell's links open it on the node that serves it.
  objectKinds: {
    published: { route: (id: string) => `published?open=${encodeURIComponent(id)}${nodeParam(id)}` },
    received: { route: (id: string) => `received?open=${encodeURIComponent(id)}${nodeParam(id)}` },
    circuit: { route: (id: string) => `consumers?open=${encodeURIComponent(id)}${nodeParam(id)}` },
    consumer: { route: (id: string) => `consumers?open=${encodeURIComponent(id)}` },
    node: { route: (id: string) => `nodes?open=${encodeURIComponent(id)}` },
  },
  commands: [
    { id: 'messaging.go-received-failed', label: 'Go to failed received messages', run: ({ navigate }) => navigate('received?status=Failed&terminal=true') },
    { id: 'messaging.go-published-failed', label: 'Go to failed published messages', run: ({ navigate }) => navigate('published?status=Failed&terminal=true') },
    { id: 'messaging.go-operator-log', label: 'Go to the operator log', run: ({ navigate }) => navigate('operator-log') },
  ],
})

/**
 * The `node` parameter an object's link carries: the node its id starts with. The list then reads that node; the local
 * node's name reads as the local node, since the view drops it.
 */
function nodeParam(id: string) {
  const separator = id.indexOf('~')

  return separator > 0 ? `&node=${encodeURIComponent(id.slice(0, separator))}` : ''
}
