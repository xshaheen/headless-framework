import { defineModule } from '@headless/dashboard-sdk'

export default defineModule({
  id: 'dev-inbox',
  product: 'dev-inbox',
  sidebar: [
    {
      label: 'Inbox',
      entries: [
        { id: 'dev-inbox.nav.emails', label: 'Emails', route: 'emails', icon: 'mail', countKey: 'emails' },
        { id: 'dev-inbox.nav.sms', label: 'SMS', route: 'sms', icon: 'message', countKey: 'sms' },
        { id: 'dev-inbox.nav.push', label: 'Push', route: 'push', icon: 'bell', countKey: 'push' },
      ],
    },
  ],
  routes: [
    { path: 'emails', lazy: () => import('./views/emails-view.tsx') },
    { path: 'sms', lazy: () => import('./views/sms-view.tsx') },
    { path: 'push', lazy: () => import('./views/push-view.tsx') },
  ],
  objectKinds: {
    email: { route: (id: string) => `emails?open=${encodeURIComponent(id)}` },
    sms: { route: (id: string) => `sms?open=${encodeURIComponent(id)}` },
    push: { route: (id: string) => `push?open=${encodeURIComponent(id)}` },
  },
})
