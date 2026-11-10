import type { ModuleDefinition } from '@headless/dashboard-sdk'
import type { ComponentType } from 'react'
import type { BootstrapModule, BootstrapRoute } from './bootstrap.ts'

/** Imports one module file by its absolute URL: `import()` in a browser, a fake in tests. */
export type ModuleImporter = (url: string) => Promise<unknown>

/**
 * A module file failed to load. During a rolling deploy the page can name a file the server no longer has, so the
 * fix is a reload, which fetches the new page and its new file names.
 */
export class ChunkLoadError extends Error {
  readonly url: string

  constructor(url: string, cause: unknown) {
    super(`The dashboard could not load ${url}.`, { cause })
    this.name = 'ChunkLoadError'
    this.url = url
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function defaultExport(url: string, value: unknown): unknown {
  if (!isRecord(value) || !('default' in value)) {
    throw new ChunkLoadError(url, new Error(`${url} has no default export.`))
  }

  return value.default
}

export interface ModuleLoader {
  /** Loads a module's entry, its `defineModule` result, once; later calls share the first load. */
  loadEntry(module: BootstrapModule): Promise<ModuleDefinition>
  /** Loads a route's view, and the module's entry and stylesheets beside it. */
  loadView(module: BootstrapModule, route: BootstrapRoute): Promise<ComponentType>
  /** Calls `listener` when any module file fails to load, at once when one already failed. */
  onError(listener: (error: ChunkLoadError) => void): () => void
}

export interface ModuleLoaderOptions {
  importer?: ModuleImporter
  /** Adds a module's stylesheet; the document head in a browser. */
  addStylesheet?: (href: string) => void
}

function browserImporter(url: string) {
  return import(/* @vite-ignore */ url) as Promise<unknown>
}

function browserStylesheet(href: string) {
  const link = document.createElement('link')
  link.rel = 'stylesheet'
  link.href = href
  document.head.append(link)
}

/**
 * Loads module code on first use only: a route visit, a contribution render, or a command run. Nothing here runs
 * while the shell draws its rail, routes, and palette, which come from the inlined manifest.
 */
export function createModuleLoader(options: ModuleLoaderOptions = {}): ModuleLoader {
  const importer = options.importer ?? browserImporter
  const addStylesheet = options.addStylesheet ?? browserStylesheet
  const files = new Map<string, Promise<unknown>>()
  const styled = new Set<string>()
  const listeners = new Set<(error: ChunkLoadError) => void>()
  let lastError: ChunkLoadError | undefined

  function load(url: string) {
    let file = files.get(url)

    if (!file) {
      file = importer(url).then(
        (value) => defaultExport(url, value),
        (cause: unknown) => {
          // A failed file is not cached, so Retry after a transient failure fetches it again.
          files.delete(url)
          throw cause instanceof ChunkLoadError ? cause : new ChunkLoadError(url, cause)
        },
      )
      files.set(url, file)
      file.catch((error: unknown) => {
        if (error instanceof ChunkLoadError) {
          lastError = error
          listeners.forEach((listener) => listener(error))
        }
      })
    }

    return file
  }

  function loadEntry(module: BootstrapModule) {
    if (!module.entry || module.refusal) {
      return Promise.reject(new Error(`${module.displayName} has no entry to load.`))
    }

    if (!styled.has(module.id)) {
      styled.add(module.id)
      module.css.forEach(addStylesheet)
    }

    return load(module.entry) as Promise<ModuleDefinition>
  }

  return {
    loadEntry,
    async loadView(module, route) {
      if (!route.chunk) {
        throw new Error(`${module.displayName} has no view for ${route.path || 'its home'}.`)
      }

      const [view] = await Promise.all([load(route.chunk), loadEntry(module)])

      return view as ComponentType
    },
    onError(listener) {
      listeners.add(listener)

      if (lastError) {
        listener(lastError)
      }

      return () => {
        listeners.delete(listener)
      }
    },
  }
}
