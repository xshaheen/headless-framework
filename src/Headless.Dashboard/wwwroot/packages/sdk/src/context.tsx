import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { KeymapProvider } from './components/keymap.tsx'
import type { DashboardApi } from './data/api.ts'
import { bindStreamToClient } from './data/bind.ts'
import type { DashboardStream } from './stream/types.ts'
import { createToastStore, type ToastStore } from './toast-store.ts'

export interface DashboardSession {
  authenticated: boolean
  name?: string
}

/** What the signed-in user may do in one module; the server enforces both. */
export interface ModulePermission {
  canView: boolean
  canOperate: boolean
}

/** What the shell hands every module: URLs, the session, permissions, the stream, and the API client. */
export interface DashboardEnvironment {
  basePath: string
  apiBase: string
  session: DashboardSession
  permissions: Readonly<Record<string, ModulePermission>>
  stream: DashboardStream
  api: DashboardApi
  traceLinkTemplate?: string
}

interface DashboardContextValue extends DashboardEnvironment {
  toasts: ToastStore
}

const DashboardContext = createContext<DashboardContextValue | null>(null)

export interface DashboardProviderProps {
  environment: DashboardEnvironment
  client: QueryClient
  children: ReactNode
}

/**
 * Provides the dashboard to the shell and every module, keeps the data client current from the stream, and owns the
 * one keymap every shortcut dispatches through.
 */
export function DashboardProvider({ environment, client, children }: DashboardProviderProps) {
  const [toasts] = useState(createToastStore)
  const { stream } = environment

  useEffect(() => bindStreamToClient(stream, client), [stream, client])

  return (
    <QueryClientProvider client={client}>
      <DashboardContext.Provider value={{ ...environment, toasts }}>
        <KeymapProvider>{children}</KeymapProvider>
      </DashboardContext.Provider>
    </QueryClientProvider>
  )
}

/** The dashboard the calling component runs in. */
export function useDashboard(): DashboardContextValue {
  const value = useContext(DashboardContext)

  if (!value) {
    throw new Error('useDashboard must be called inside <DashboardProvider>; the shell and renderModule provide it.')
  }

  return value
}

export interface ModuleScopeValue extends ModulePermission {
  id: string
}

const ModuleContext = createContext<ModuleScopeValue | null>(null)

/** Marks the subtree as one module's views, so SDK pieces know whose commands and permissions apply. */
export function ModuleScope({ id, children }: { id: string; children: ReactNode }) {
  const { permissions } = useDashboard()
  const permission = permissions[id] ?? { canView: false, canOperate: false }

  return <ModuleContext.Provider value={{ id, ...permission }}>{children}</ModuleContext.Provider>
}

/** The module the calling view belongs to, and what the user may do in it. */
export function useModule(): ModuleScopeValue {
  const value = useContext(ModuleContext)

  if (!value) {
    throw new Error('useModule must be called inside a module view; the shell wraps each one in <ModuleScope>.')
  }

  return value
}
