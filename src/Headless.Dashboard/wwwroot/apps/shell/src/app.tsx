import { DashboardProvider, type DashboardEnvironment } from '@headless/dashboard-sdk'
import { DashboardApiError, type DashboardApi } from '@headless/dashboard-sdk/data'
import type { QueryClient } from '@headless/dashboard-sdk/query'
import { RouterProvider, type createBrowserRouter } from '@headless/dashboard-sdk/router'
import type { DashboardStream } from '@headless/dashboard-sdk/stream'
import type { BootstrapState } from './bootstrap.ts'
import type { ModuleLoader } from './module-loader.ts'
import { ShellProvider } from './shell-context.tsx'

/**
 * Wraps the API client so an answer of `401` ends the page's session: the cookie expired or was revoked, and the
 * operator lands on the sign-in page once instead of seeing each view fail.
 */
export function withSessionEnd(api: DashboardApi, onSessionEnded: () => void): DashboardApi {
  let ended = false

  return {
    async request<T>(request: Parameters<DashboardApi['request']>[0]) {
      try {
        return await api.request<T>(request)
      } catch (error) {
        if (error instanceof DashboardApiError && error.status === 401 && !ended) {
          ended = true
          onSessionEnded()
        }

        throw error
      }
    },
  }
}

/** The permissions the inlined state grants: every listed module may be viewed, and some operated. */
export function permissionsOf(state: BootstrapState): DashboardEnvironment['permissions'] {
  return Object.fromEntries(state.modules.map((module) => [module.id, { canView: true, canOperate: module.canOperate }]))
}

export interface ShellAppProps {
  state: BootstrapState
  router: ReturnType<typeof createBrowserRouter>
  client: QueryClient
  api: DashboardApi
  stream: DashboardStream
  loader: ModuleLoader
  signOut: () => Promise<void>
}

/** The signed-in shell: the dashboard environment every module reads, and the router built from the manifest. */
export function ShellApp({ state, router, client, api, stream, loader, signOut }: ShellAppProps) {
  return (
    <DashboardProvider
      client={client}
      environment={{
        basePath: state.basePath,
        apiBase: state.apiBase,
        session: { authenticated: state.session.authenticated, name: state.session.name ?? undefined },
        permissions: permissionsOf(state),
        stream,
        api,
        traceLinkTemplate: state.traceLinkTemplate,
      }}
    >
      <ShellProvider value={{ state, loader, signOut }}>
        <RouterProvider router={router} />
      </ShellProvider>
    </DashboardProvider>
  )
}
