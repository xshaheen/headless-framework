import { useEffect } from 'react'
import { create } from 'zustand'
import { useDashboard } from '../context.tsx'

/** What the server last said about one module's reads: out of date, and the sources they could not reach. */
export interface ModuleStale {
  isStale: boolean
  /** The sources the module's reads could not reach while it stays live; empty when every source answered. */
  unavailable: readonly string[]
}

interface StaleState {
  modules: Readonly<Record<string, ModuleStale>>
  /** The nodes of each module whose data is out of date. */
  nodes: Readonly<Record<string, Readonly<Record<string, true>>>>
}

const initial: StaleState = { modules: {}, nodes: {} }

// A shared constant, so a module with no report selects the same object each render instead of a new one.
const fresh: ModuleStale = Object.freeze({ isStale: false, unavailable: Object.freeze([]) })

/**
 * The page's stale state, kept apart from any view: a view that mounts after the server reported a module or node
 * out of date must read that report, and the stream does not repeat it.
 */
const useStaleStore = create<StaleState>(() => initial)

/**
 * Feeds the stale state from the stream for the whole page. Mount it once, inside `<DashboardProvider>`: the shell
 * does, and so does `renderModule`. Only `stale: false` clears a report; `reset` and `node.reset` make views refetch
 * and say nothing about whether the module or node is still out of date.
 */
export function StaleFeed() {
  const { stream } = useDashboard()

  useEffect(() => {
    const stops = [
      stream.on('module.stale', ({ module, stale, unavailable }) => {
        useStaleStore.setState((state) => ({
          modules: { ...state.modules, [module]: { isStale: stale, unavailable: unavailable ?? [] } },
        }))
      }),
      stream.on('node.stale', ({ module, node, stale }) => {
        useStaleStore.setState((state) => {
          const nodes: Record<string, true> = { ...state.nodes[module] }

          if (stale) {
            nodes[node] = true
          } else {
            delete nodes[node]
          }

          return { nodes: { ...state.nodes, [module]: nodes } }
        })
      }),
    ]

    return () => {
      stops.forEach((stop) => stop())
      // Another stream, or the next test, starts from no reports.
      useStaleStore.setState(initial, true)
    }
  }, [stream])

  return null
}

/** Whether the server reports `moduleId`'s data out of date, and the sources its reads could not reach. */
export function useModuleStale(moduleId: string): ModuleStale {
  return useStaleStore((state) => state.modules[moduleId] ?? fresh)
}

/** Whether the server reports the data of `moduleId` on `node` out of date. */
export function useNodeStale(moduleId: string, node: string): boolean {
  return useStaleStore((state) => state.nodes[moduleId]?.[node] === true)
}
