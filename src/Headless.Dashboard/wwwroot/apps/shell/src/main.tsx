import { createDashboardClient, DashboardDataProvider, setObject } from '@headless/dashboard-sdk'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { readBootstrapState } from './bootstrap.ts'
import { Shell } from './shell.tsx'

const state = readBootstrapState(document)
const client = createDashboardClient()

// Modules read the session from the shared data client, the same instance the shell fills.
setObject(client, 'session', 'current', state.session)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <DashboardDataProvider client={client}>
      <Shell state={state} pathname={window.location.pathname} />
    </DashboardDataProvider>
  </StrictMode>,
)
