import { defineModule } from '@headless/dashboard-sdk'
import type { BootstrapModule, BootstrapSidebarEntry, BootstrapState } from '../../src/bootstrap.ts'

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
    routes: [
      { path: 'runs', chunk: chunkUrl(id, 'runs') },
      { path: 'settings', chunk: chunkUrl(id, 'settings') },
    ],
    commands: [{ id: `${id}.add`, label: `Add ${id} run`, requires: 'operate' }],
    events: [],
    ...overrides,
  }
}

/** The sidebar entries every fixture module declares unless a test names its own. */
export function moduleSidebar(id: string): BootstrapSidebarEntry[] {
  return [
    { id: `${id}.nav.runs`, label: 'Runs', route: 'runs', icon: 'runs' },
    { id: `${id}.nav.settings`, label: 'Settings', route: 'settings', icon: 'settings', requires: 'operate' },
  ]
}

/**
 * The page state for `modules`, as the server inlines it: every module joins one product, each with one sidebar
 * section, `sidebars[id]` when given, else its default entries; a refused module declares none.
 */
export function shellState(
  modules: BootstrapModule[],
  overrides: Partial<BootstrapState> = {},
  sidebars: Record<string, BootstrapSidebarEntry[]> = {},
): BootstrapState {
  const sections = modules
    .filter((module) => !module.refusal)
    .map((module) => ({ module: module.id, entries: sidebars[module.id] ?? moduleSidebar(module.id) }))

  return {
    basePath: '/headless',
    apiBase: '/headless/api',
    streamUrl: '/headless/api/stream',
    sdkVersion: '1.0.0',
    authMode: 'host',
    session: { authenticated: true, name: 'Mona Haddad' },
    modules,
    products:
      modules.length === 0
        ? []
        : [{ id: 'jobs', label: 'Jobs', modules: modules.map((module) => module.id), sections }],
    ...overrides,
  }
}

/** The 20-module dashboard: manifests only, whose files a test can watch being imported. */
export function twentyModules() {
  return Array.from({ length: 20 }, (_, index) => moduleManifest(`mod-${String(index + 1).padStart(2, '0')}`))
}

export function jobsWithDashboard() {
  const jobs = moduleManifest('jobs', {
    routes: [
      { path: '', chunk: chunkUrl('jobs', 'dashboard') },
      { path: 'runs', chunk: chunkUrl('jobs', 'runs') },
      { path: 'settings', chunk: chunkUrl('jobs', 'settings') },
    ],
  })
  const sidebar: BootstrapSidebarEntry[] = [
    { id: 'jobs.nav.dashboard', label: 'Dashboard', route: '', icon: 'dashboard' },
    { id: 'jobs.nav.failed', label: 'Failed', route: 'runs?status=Failed', state: 'fail', countKey: 'Failed' },
    { id: 'jobs.nav.runs', label: 'All runs', route: 'runs', icon: 'runs' },
  ]

  return { modules: [jobs], state: shellState([jobs], {}, { jobs: sidebar }) }
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
        product: 'jobs',
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
