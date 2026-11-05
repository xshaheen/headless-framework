import { lazy, Suspense, type ComponentType, type LazyExoticComponent } from 'react'
import type { BootstrapModule, BootstrapState } from './bootstrap.ts'

const views = new Map<string, LazyExoticComponent<ComponentType>>()

// One lazy component per chunk URL, so revisiting a route reuses the loaded view instead of remounting a new one.
function viewFor(chunk: string) {
  let view = views.get(chunk)

  if (!view) {
    view = lazy(() => import(/* @vite-ignore */ chunk) as Promise<{ default: ComponentType }>)
    views.set(chunk, view)
  }

  return view
}

function matches(pattern: string, path: string) {
  const expected = pattern.split('/').filter(Boolean)
  const actual = path.split('/').filter(Boolean)

  return expected.length === actual.length && expected.every((part, at) => part.startsWith(':') || part === actual[at])
}

/** Finds the module route `pathname` opens: `{basePath}/{moduleId}/{route path}`. */
export function findRoute(state: BootstrapState, pathname: string) {
  if (!pathname.startsWith(state.basePath)) {
    return undefined
  }

  const [moduleId = '', ...rest] = pathname.slice(state.basePath.length).split('/').filter(Boolean)
  const module = state.modules.find((candidate) => candidate.id === moduleId && !candidate.refusal)
  const route = module?.routes.find((candidate) => matches(candidate.path, rest.join('/')))

  return module && route?.chunk ? { module, chunk: route.chunk } : undefined
}

function ModuleNav({ state, module }: { state: BootstrapState; module: BootstrapModule }) {
  return (
    <li>
      <strong>{module.displayName}</strong>
      {module.refusal ? (
        <p role="alert">{module.refusal.message}</p>
      ) : (
        <ul>
          {module.nav.map((entry) => (
            <li key={entry.id}>
              <a href={`${state.basePath}/${module.id}/${entry.to}`}>{entry.label}</a>
            </li>
          ))}
        </ul>
      )}
    </li>
  )
}

/**
 * The shell's placeholder: the rail drawn from the inlined manifest alone, and the current route's view, whose chunk
 * loads only when its route is visited.
 */
export function Shell({ state, pathname }: { state: BootstrapState; pathname: string }) {
  const current = findRoute(state, pathname)
  const View = current ? viewFor(current.chunk) : undefined

  return (
    <>
      <nav aria-label="Modules">
        <ul>
          {state.modules.map((module) => (
            <ModuleNav key={module.id} state={state} module={module} />
          ))}
        </ul>
      </nav>
      <main>
        {View ? (
          <Suspense fallback={null}>
            <View />
          </Suspense>
        ) : (
          <p>{state.session.authenticated ? 'Choose a module.' : 'Sign in to continue.'}</p>
        )}
      </main>
    </>
  )
}
