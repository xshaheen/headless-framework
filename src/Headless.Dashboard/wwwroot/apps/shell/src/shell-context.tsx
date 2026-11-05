import { createContext, useContext, type ReactNode } from 'react'
import { useNavigate } from '@headless/dashboard-sdk/router'
import type { BootstrapModule, BootstrapState } from './bootstrap.ts'
import type { ModuleLoader } from './module-loader.ts'

/** One object of any module, as the shell's aggregation endpoints name it. */
export interface ObjectRef {
  module: string
  kind: string
  id: string
}

interface ShellContextValue {
  state: BootstrapState
  loader: ModuleLoader
  /** Ends the session on the server, then signs every tab of this browser out. Rejects when the server refused. */
  signOut: () => Promise<void>
}

const ShellContext = createContext<ShellContextValue | null>(null)

export function ShellProvider({ value, children }: { value: ShellContextValue; children: ReactNode }) {
  return <ShellContext.Provider value={value}>{children}</ShellContext.Provider>
}

export function useShell(): ShellContextValue {
  const value = useContext(ShellContext)

  if (!value) {
    throw new Error('useShell must be called inside <ShellProvider>; the shell app provides it.')
  }

  return value
}

/** The module `id` names, when the user may view it and the shell loaded it. */
export function findModule(state: BootstrapState, id: string): BootstrapModule | undefined {
  return state.modules.find((module) => module.id === id && !module.refusal)
}

/**
 * Opens an object in its module: loads the module's entry, which knows the object kind's route, then navigates
 * there. The entry loads only now, on first use.
 */
export function useOpenObject() {
  const { state, loader } = useShell()
  const navigate = useNavigate()

  return async (target: ObjectRef) => {
    const module = findModule(state, target.module)

    if (!module) {
      throw new Error(`The ${target.module} module is not available to you.`)
    }

    const definition = await loader.loadEntry(module)
    const route = definition.objectKinds?.[target.kind]?.route(target.id)

    await navigate(route === undefined ? `/${module.id}` : `/${module.id}/${route}`)
  }
}
