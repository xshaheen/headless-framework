import type { ComponentType } from 'react'

/** Who may use an entry: a viewer, or an operator who may also act. */
export type DashboardPermission = 'view' | 'operate'

/**
 * The product a module belongs to. The shell shows one tab per product a host registered, in this order: `jobs`,
 * `messaging`, `cluster`, `platform`, `dev-inbox`. Modules that name one product share its tab.
 */
export type DashboardProduct = 'jobs' | 'messaging' | 'cluster' | 'platform' | 'dev-inbox'

/** The colour role a state dot paints: scheduled, enqueued, processing, ok, failed, or off. */
export type DashboardStateRole = 'sched' | 'enq' | 'proc' | 'ok' | 'fail' | 'off'

export interface ModuleSidebarEntry {
  /** Unique across the module, such as `jobs.nav.failed`. A host disables or orders the entry by this id. */
  id: string
  label: string
  /** The route the entry opens, relative to the module, with an optional query such as `?status=Failed`. */
  route: string
  /** The icon the entry leads with. An entry leads with an icon or a state dot, never both. */
  icon?: string
  /** The colour role of the dot the entry leads with, for an entry that lists the objects in one state. */
  state?: DashboardStateRole
  /** The key of the entry's count in the module's counts answer; an entry without one shows no count. */
  countKey?: string
  requires?: DashboardPermission
}

export interface ModuleSidebarSection {
  /** The section's heading; a section without one, such as the one holding Dashboard, draws no heading. */
  label?: string
  entries: readonly ModuleSidebarEntry[]
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
  product: DashboardProduct
  /** The module's sections in its product's sidebar, in display order. */
  sidebar?: readonly ModuleSidebarSection[]
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
