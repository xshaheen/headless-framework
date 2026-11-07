import { render, type RenderResult } from '@testing-library/react'
import type { QueryClient } from '@tanstack/react-query'
import type { ComponentType } from 'react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { OperationHost } from '../commands/operations.tsx'
import { ToastRegion } from '../components/live.tsx'
import { DashboardProvider, ModuleScope, type DashboardSession, type ModulePermission } from '../context.tsx'
import { DashboardApiError, type ApiRequest, type DashboardApi } from '../data/api.ts'
import { createDashboardClient, setObject } from '../data/client.tsx'
import type { ModuleDefinition } from '../define-module.ts'
import { createFakeStream, type FakeStream } from './fake-stream.ts'

/**
 * Answers the module's API calls in a test: return the response body, or throw a `DashboardApiError` to answer with
 * that status.
 */
export type ApiHandler = (request: ApiRequest) => unknown

export interface RenderModuleOptions {
  session?: DashboardSession
  /** What the user may do in the module; both default to true. */
  permissions?: Partial<ModulePermission>
  api?: ApiHandler
  /** The route path inside the module to open, such as `runs/42`; the module's home by default. */
  path?: string
  basePath?: string
  /** How long an Operation waits between chunks. */
  chunkDelayMs?: number
}

export interface RenderModuleResult extends RenderResult {
  /** The fake stream: push events into it as the server would send them. */
  stream: FakeStream
  client: QueryClient
  api: DashboardApi
  /** Every API call the module made, in order. */
  requests: ApiRequest[]
  /** Opens another route of the module, such as `runs/42`. */
  navigate: (path: string) => Promise<void>
}

/**
 * Mounts a module in a test shell, as the dashboard would: its routes, the session, its permissions, the data
 * client, an Operation host, toasts, and a fake stream the test pushes events into.
 */
export function renderModule(definition: ModuleDefinition, options: RenderModuleOptions = {}): RenderModuleResult {
  const basePath = options.basePath ?? '/dashboard'
  const session = options.session ?? { authenticated: true, name: 'operator' }
  const permission: ModulePermission = {
    canView: options.permissions?.canView ?? true,
    canOperate: options.permissions?.canOperate ?? true,
  }
  const stream = createFakeStream()
  const client = createDashboardClient()
  const requests: ApiRequest[] = []
  const handler = options.api ?? ((request: ApiRequest) => {
    throw new DashboardApiError(404, `No API handler for ${request.method} ${request.path}.`)
  })
  const api: DashboardApi = {
    async request<T>(request: ApiRequest) {
      requests.push(request)

      return (await handler(request)) as T
    },
  }

  setObject(client, 'session', 'current', session)

  const routes = (definition.routes ?? []).map((route) => ({
    path: `/${definition.id}/${route.path}`,
    lazy: async () => {
      const view = await route.lazy()

      return { Component: view.default as ComponentType }
    },
  }))
  const router = createMemoryRouter(routes, { initialEntries: [`/${definition.id}/${options.path ?? ''}`] })

  const result = render(
    <DashboardProvider
      client={client}
      environment={{
        basePath,
        apiBase: `${basePath}/api`,
        session,
        permissions: { [definition.id]: permission },
        stream,
        api,
      }}
    >
      <OperationHost chunkDelayMs={options.chunkDelayMs ?? 0}>
        <ModuleScope id={definition.id}>
          <RouterProvider router={router} />
        </ModuleScope>
      </OperationHost>
      <ToastRegion />
    </DashboardProvider>,
  )

  return {
    ...result,
    stream,
    client,
    api,
    requests,
    navigate: (path) => router.navigate(`/${definition.id}/${path}`),
  }
}

export { createFakeStream, type FakeStream } from './fake-stream.ts'
