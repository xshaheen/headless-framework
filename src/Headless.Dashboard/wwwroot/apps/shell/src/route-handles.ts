import type { BootstrapProduct, BootstrapState } from './bootstrap.ts'

const trim = (path: string) => path.replace(/^\/+|\/+$/g, '')

export function joinPath(moduleId: string, path: string) {
  const rest = trim(path)

  return rest ? `${moduleId}/${rest}` : moduleId
}

/**
 * The shell path a module route opens: `/jobs/cron-jobs`. A route that is only a query (`?status=Failed`) stays on the
 * module's home path, so a state entry opens its filtered list.
 */
export function modulePath(moduleId: string, route: string) {
  const rest = trim(route)

  return rest.startsWith('?') ? `/${moduleId}${rest}` : rest ? `/${moduleId}/${rest}` : `/${moduleId}`
}

/** A product's home: its first module's home route, where its sidebar's first entry leads. */
export function productHome(product: BootstrapProduct) {
  const section = product.sections[0]
  const entry = section?.entries[0]

  return entry ? modulePath(section.module, entry.route) : `/${product.modules[0] ?? ''}`
}

/** The product whose module owns `pathname`, a path inside the router's basename; `undefined` off every module. */
export function productOf(state: BootstrapState, pathname: string): BootstrapProduct | undefined {
  const moduleId = pathname.split('/')[1]

  return moduleId ? state.products.find((product) => product.modules.includes(moduleId)) : undefined
}
