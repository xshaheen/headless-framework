import './tokens.css'

export { Button, type ButtonProps } from './components/button.tsx'
export {
  DashboardProvider,
  ModuleScope,
  useDashboard,
  useModule,
  type DashboardEnvironment,
  type DashboardProviderProps,
  type DashboardSession,
  type ModulePermission,
  type ModuleScopeValue,
} from './context.tsx'
export { createDashboardClient, DashboardDataProvider, objectKey, setObject, useObject } from './data/client.tsx'
export {
  defineModule,
  type DashboardPermission,
  type ModuleCommand,
  type ModuleCommandContext,
  type ModuleDefinition,
  type ModuleEventHandler,
  type ModuleNavEntry,
  type ModuleObjectKind,
  type ModuleRoute,
} from './define-module.ts'
export type { Toast, ToastStore } from './toast-store.ts'
