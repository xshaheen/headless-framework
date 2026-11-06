import { matchPath } from '@headless/dashboard-sdk/router'
import { moduleSidebarEntries, type BootstrapModule, type BootstrapRoute, type BootstrapState } from './bootstrap.ts'

/** What the document bar shows for a route: its breadcrumb, and the module it belongs to. */
export interface ShellRouteHandle {
  crumb: string[]
  module?: string
}

export function joinPath(moduleId: string, path: string) {
  const rest = path.replace(/^\/+|\/+$/g, '')

  return rest ? `${moduleId}/${rest}` : moduleId
}

const trim = (path: string) => path.replace(/^\/+|\/+$/g, '')

/**
 * The module, the sidebar entry the route sits under, and for an object page (`functions/:name`) the object from the
 * path: `Jobs › Functions › billing.charge`.
 */
function crumbFor(state: BootstrapState, module: BootstrapModule, route: BootstrapRoute, path: string): string[] {
  const own = trim(route.path)
  const entry = moduleSidebarEntries(state, module.id).find((candidate) => {
    const to = trim(candidate.route)

    return to === own || (to !== '' && own.startsWith(`${to}/`))
  })
  const crumb = entry && entry.label !== module.displayName ? [module.displayName, entry.label] : [module.displayName]

  return own.includes(':') ? [...crumb, decodeURIComponent(path.slice(path.lastIndexOf('/') + 1))] : crumb
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
        return { crumb: crumbFor(state, module, route, path), module: module.id }
      }
    }
  }

  return { crumb: ['Not found'] }
}
