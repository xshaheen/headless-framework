import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { resolve } from 'node:path'
import type { Plugin, Rolldown, UserConfig } from 'vite'
import { readModuleDefinition, type ModuleDefinitionSource } from './definition.ts'
import { reactPlugins } from './react.ts'
import { isSharedSpecifier, sdkPackage } from './shared.ts'
import { checkBundleIsolation, checkDynamicCode, validateManifest, type ModuleManifest } from './validator.ts'

export interface DashboardModuleOptions {
  /** The module's folder; `entry` and `outDir` resolve against it. */
  root: string
  /** The source file that exports default the `defineModule` call. */
  entry: string
  /** Where the module's build goes: the folder its .NET package embeds and serves at `{base}/m/{id}/`. */
  outDir: string
}

/** Reads the SDK version installed for `root`: the version a module built there is built against. */
export function installedSdkVersion(root: string): string {
  const require = createRequire(resolve(root, 'package.json'))
  const manifest = require(`${sdkPackage}/package.json`) as { version: string }

  return manifest.version
}

export interface ModuleManifestPluginOptions {
  entry: string
  sdkVersion: string
}

/**
 * Writes `manifest.json` beside the module's entry from its `defineModule` literal, then validates the manifest and
 * checks the bundle: no React, React Aria, TanStack Query, or React Router code, and no `eval` or inline script.
 * Any problem fails the build.
 */
export function moduleManifestPlugin(options: ModuleManifestPluginOptions): Plugin {
  const entry = resolve(options.entry)
  let definition: ModuleDefinitionSource
  const routeModules = new Map<number, string>()

  return {
    name: 'headless-dashboard-module-manifest',
    apply: 'build',

    async buildStart() {
      definition = readModuleDefinition(readFileSync(entry, 'utf8'), entry)
      routeModules.clear()

      for (const [index, route] of definition.routes.entries()) {
        const resolved = await this.resolve(route.import, entry)

        if (!resolved) {
          this.error(`${entry}: routes[${index}] imports '${route.import}', which does not resolve.`)
        }

        routeModules.set(index, resolved.id)
      }
    },

    generateBundle(_, bundle) {
      const chunks = Object.values(bundle).filter((file): file is Rolldown.OutputChunk => file.type === 'chunk')
      const assets = Object.values(bundle).filter((file): file is Rolldown.OutputAsset => file.type === 'asset')
      const entryChunk = chunks.find((chunk) => chunk.isEntry && chunk.facadeModuleId === entry)

      const manifest: ModuleManifest = {
        id: definition.id,
        sdkVersion: options.sdkVersion,
        entry: entryChunk?.fileName ?? '',
        css: assets.filter((asset) => asset.fileName.endsWith('.css')).map((asset) => asset.fileName),
        nav: definition.nav,
        routes: definition.routes.map((route, index) => ({
          path: route.path,
          chunk:
            chunks.find((chunk) => chunk.isDynamicEntry && chunk.facadeModuleId === routeModules.get(index))
              ?.fileName ?? '',
        })),
        objectKinds: definition.objectKinds,
        commands: definition.commands,
        events: definition.events,
      }

      const errors = [
        ...validateManifest(manifest, new Set(Object.keys(bundle))),
        ...checkBundleIsolation(chunks),
        ...checkDynamicCode(chunks),
      ]

      if (errors.length > 0) {
        this.error(`Module '${definition.id}' failed the dashboard's build checks:\n  ${errors.join('\n  ')}`)
      }

      this.emitFile({ type: 'asset', fileName: 'manifest.json', source: `${JSON.stringify(manifest, null, 2)}\n` })
    },
  }
}

/**
 * The Vite configuration of a dashboard module: an ES module library whose entry is the `defineModule` call and
 * whose routes are lazy chunks. React and the SDK stay external, resolved by the shell's import map at run time.
 */
export function dashboardModule(options: DashboardModuleOptions): UserConfig {
  const entry = resolve(options.root, options.entry)

  return {
    root: options.root,
    // A library build keeps process.env.NODE_ENV for its consumer to replace; a module has no later bundler.
    define: { 'process.env.NODE_ENV': JSON.stringify('production') },
    plugins: [
      ...reactPlugins(),
      moduleManifestPlugin({ entry, sdkVersion: installedSdkVersion(options.root) }),
    ],
    build: {
      outDir: resolve(options.root, options.outDir),
      emptyOutDir: true,
      minify: true,
      lib: {
        entry,
        formats: ['es'],
        // Fixed names: the server appends each file's content version to its URL.
        fileName: () => 'index.js',
        cssFileName: 'index',
      },
      rolldownOptions: {
        external: isSharedSpecifier,
        // A library build keeps ES output readable; a module is served as is, so it is minified like the shell.
        output: { chunkFileNames: 'chunks/[name]-[hash].js', minify: true },
      },
    },
  }
}
