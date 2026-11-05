import { createDashboardClient } from '@headless/dashboard-sdk'
import type { ApiRequest, DashboardApi } from '@headless/dashboard-sdk/data'
import { createMemoryRouter } from '@headless/dashboard-sdk/router'
import { createFakeStream } from '@headless/dashboard-sdk/testing'
import { render } from '@testing-library/react'
import { vi } from 'vitest'
import { ShellApp } from '../src/app.tsx'
import type { BootstrapState } from '../src/bootstrap.ts'
import { createModuleLoader, type ModuleImporter } from '../src/module-loader.ts'
import { buildRoutes } from '../src/router.tsx'

/** Answers the shell's API calls in a test; throw a `DashboardApiError` to answer with its status. */
export type ApiHandler = (request: ApiRequest) => unknown

export interface RenderShellOptions {
  api?: ApiHandler
  /** The path inside the base path to open first. */
  path?: string
  /** The module files by URL; a URL not listed fails to load as a missing file would. */
  files?: Record<string, unknown>
  signOut?: () => Promise<void>
}

export const emptyAggregates: ApiHandler = (request) => {
  if (request.path === 'needs-you' || request.path === 'overview' || request.path === 'resolve') {
    return { modules: [] }
  }

  throw new Error(`No API handler for ${request.method} ${request.path}.`)
}

/** Mounts the signed-in shell on a memory router, with a fake stream and API, and records every module import. */
export function renderShell(state: BootstrapState, options: RenderShellOptions = {}) {
  const stream = createFakeStream()
  const client = createDashboardClient()
  const requests: ApiRequest[] = []
  const handler = options.api ?? emptyAggregates
  const api: DashboardApi = {
    async request<T>(request: ApiRequest) {
      requests.push(request)

      return (await handler(request)) as T
    },
  }
  const files = options.files ?? {}
  const imported: string[] = []
  const importer = vi.fn<ModuleImporter>(async (url) => {
    imported.push(url)

    if (!(url in files)) {
      throw new TypeError(`Failed to fetch dynamically imported module: ${url}`)
    }

    return files[url]
  })
  const loader = createModuleLoader({ importer, addStylesheet: () => undefined })
  const router = createMemoryRouter(buildRoutes(state, loader), { initialEntries: [options.path ?? '/'] })
  const signOut = vi.fn(options.signOut ?? (async () => undefined))

  const result = render(
    <ShellApp state={state} router={router} client={client} api={api} stream={stream} loader={loader} signOut={signOut} />,
  )

  return { ...result, stream, client, requests, imported, router, signOut }
}
