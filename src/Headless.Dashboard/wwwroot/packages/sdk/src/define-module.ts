import type { ComponentType } from 'react'

/** Who may use an entry: a viewer, or an operator who may also act. */
export type DashboardPermission = 'view' | 'operate'

export interface ModuleNavEntry {
  /** Unique across the module, such as `jobs.nav.runs`. */
  id: string
  label: string
  /** The route path the entry opens, relative to the module. */
  to: string
  requires?: DashboardPermission
}

export interface ModuleRoute {
  /** The path relative to the module, such as `runs/:id`. The empty path is the module's home. */
  path: string
  /** Loads the route's view. The build turns each `import()` into its own chunk. */
  lazy: () => Promise<{ default: ComponentType }>
}

export interface ModuleObjectKind {
  /** The route path that shows one object of this kind. */
  route: (id: string) => string
}

export interface ModuleCommandContext {
  navigate: (to: string) => void
}

export interface ModuleCommand {
  id: string
  label: string
  requires?: DashboardPermission
  run: (context: ModuleCommandContext) => void
}

export type ModuleEventHandler = (payload: unknown) => void

export interface ModuleDefinition {
  /** The module's id, the same id its .NET registration uses. */
  id: string
  nav?: readonly ModuleNavEntry[]
  routes?: readonly ModuleRoute[]
  objectKinds?: Readonly<Record<string, ModuleObjectKind>>
  commands?: readonly ModuleCommand[]
  events?: Readonly<Record<string, ModuleEventHandler>>
}

/**
 * Declares a module. The build reads the literal passed here to write the module's `manifest.json`, so every static
 * field (ids, labels, paths, permissions, event names) must be written as a literal, not computed.
 */
export function defineModule<const T extends ModuleDefinition>(definition: T): T {
  return definition
}
