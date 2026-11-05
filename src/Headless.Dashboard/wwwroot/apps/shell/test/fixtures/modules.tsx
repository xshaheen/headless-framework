import { defineModule } from '@headless/dashboard-sdk'
import type { BootstrapModule, BootstrapState } from '../../src/bootstrap.ts'

/** A module's entry URL, as the server inlines it. */
export function entryUrl(id: string) {
  return `/headless/m/${id}/index.js?v=1`
}

export function chunkUrl(id: string, name: string) {
  return `/headless/m/${id}/chunks/${name}.js?v=1`
}

/** A module manifest as the server inlines it: a home route and a runs route, each with its own chunk. */
export function moduleManifest(id: string, overrides: Partial<BootstrapModule> = {}): BootstrapModule {
  const name = `Module ${id}`

  return {
    id,
    displayName: name,
    canOperate: true,
    entry: entryUrl(id),
    css: [],
    nav: [
      { id: `${id}.nav.runs`, label: 'Runs', to: 'runs' },
      { id: `${id}.nav.settings`, label: 'Settings', to: 'settings', requires: 'operate' },
    ],
    routes: [
      { path: 'runs', chunk: chunkUrl(id, 'runs') },
      { path: 'settings', chunk: chunkUrl(id, 'settings') },
    ],
    commands: [{ id: `${id}.add`, label: `Add ${id} run`, requires: 'operate' }],
    events: [],
    ...overrides,
  }
}

export function shellState(modules: BootstrapModule[], overrides: Partial<BootstrapState> = {}): BootstrapState {
  return {
    basePath: '/headless',
    apiBase: '/headless/api',
    streamUrl: '/headless/api/stream',
    sdkVersion: '1.0.0',
    authMode: 'host',
    session: { authenticated: true, name: 'Mona Haddad' },
    modules,
    ...overrides,
  }
}

/** The 20-module dashboard: manifests only, whose files a test can watch being imported. */
export function twentyModules() {
  return Array.from({ length: 20 }, (_, index) => moduleManifest(`mod-${String(index + 1).padStart(2, '0')}`))
}

/**
 * The files every fixture module serves: its entry, a `defineModule` result, and a view per route that names the
 * module and route it belongs to.
 */
export function moduleFiles(modules: readonly BootstrapModule[]): Record<string, unknown> {
  const files: Record<string, unknown> = {}

  for (const module of modules) {
    files[entryUrl(module.id)] = {
      default: defineModule({
        id: module.id,
        objectKinds: { run: { route: (id: string) => `runs?selected=${id}` } },
        commands: [],
      }),
    }

    for (const route of module.routes) {
      files[route.chunk!] = {
        default: function View() {
          return <h2>{`${module.displayName} ${route.path} view`}</h2>
        },
      }
    }
  }

  return files
}
