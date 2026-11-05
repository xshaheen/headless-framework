import { ModuleScope } from '@headless/dashboard-sdk'
import type { RouteObject } from '@headless/dashboard-sdk/router'
import type { BootstrapModule, BootstrapState } from './bootstrap.ts'
import { ShellLayout } from './layout/shell-layout.tsx'
import type { ModuleLoader } from './module-loader.ts'
import { joinPath } from './route-handles.ts'
import { ModuleError, NotFound, RouteError } from './views/module-error.tsx'
import { Overview } from './views/overview.tsx'

function moduleRoutes(module: BootstrapModule, loader: ModuleLoader): RouteObject[] {
  if (module.refusal) {
    return [
      { path: module.id, element: <ModuleError module={module} /> },
      { path: `${module.id}/*`, element: <ModuleError module={module} /> },
    ]
  }

  return module.routes
    .filter((route) => route.chunk)
    .map((route): RouteObject => {
      return {
        path: joinPath(module.id, route.path),
        errorElement: <RouteError />,
        lazy: async () => {
          const View = await loader.loadView(module, route)

          return {
            Component: function ModuleView() {
              return (
                <ModuleScope id={module.id}>
                  <View />
                </ModuleScope>
              )
            },
          }
        },
      }
    })
}

/**
 * The shell's routes, built from the inlined manifest alone: the shell's own views, then every module's routes,
 * each loading its view and its module's entry only when visited. A refused module keeps its paths, which show why
 * it did not load.
 */
export function buildRoutes(state: BootstrapState, loader: ModuleLoader): RouteObject[] {
  return [
    {
      path: '/',
      element: <ShellLayout />,
      errorElement: <RouteError />,
      children: [
        { index: true, element: <Overview /> },
        {
          path: 'needs-you',
          errorElement: <RouteError />,
          lazy: async () => ({ Component: (await import('./views/needs-you.tsx')).NeedsYou }),
        },
        ...state.modules.flatMap((module) => moduleRoutes(module, loader)),
        { path: '*', element: <NotFound /> },
      ],
    },
  ]
}
