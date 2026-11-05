import { matchPath } from '@headless/dashboard-sdk/router'
import type { BootstrapModule, BootstrapRoute, BootstrapState } from './bootstrap.ts'

/** What the document bar shows for a route: its breadcrumb, and the module it belongs to. */
export interface ShellRouteHandle {
  crumb: string[]
  module?: string
}

export function joinPath(moduleId: string, path: string) {
  const rest = path.replace(/^\/+|\/+$/g, '')

  return rest ? `${moduleId}/${rest}` : moduleId
}

function crumbFor(module: BootstrapModule, route: BootstrapRoute): string[] {
  const entry = module.nav.find((candidate) => candidate.to.replace(/^\/+|\/+$/g, '') === route.path.replace(/^\/+|\/+$/g, ''))

  return entry && entry.label !== module.displayName ? [module.displayName, entry.label] : [module.displayName]
}

const shellHandles: Record<string, ShellRouteHandle> = {
  '/': { crumb: ['Overview'] },
  '/needs-you': { crumb: ['Needs you'] },
}

/**
 * The breadcrumb and module of `pathname`, a path inside the router's basename. Read from the manifest, the same
 * data the routes are built from, so the document bar names a view before its code loads.
 */
export function routeHandleFor(state: BootstrapState, pathname: string): ShellRouteHandle {
  const path = pathname.replace(/\/+$/, '') || '/'
  const shell = shellHandles[path]

  if (shell) {
    return shell
  }

  for (const module of state.modules) {
    if (module.refusal) {
      if (matchPath({ path: `/${module.id}/*` }, path)) {
        return { crumb: [module.displayName], module: module.id }
      }

      continue
    }

    for (const route of module.routes) {
      if (matchPath({ path: `/${joinPath(module.id, route.path)}`, end: true }, path)) {
        return { crumb: crumbFor(module, route), module: module.id }
      }
    }
  }

  return { crumb: ['Not found'] }
}
