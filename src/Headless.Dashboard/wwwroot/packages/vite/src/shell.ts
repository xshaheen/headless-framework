import { createRequire } from 'node:module'
import { resolve } from 'node:path'
import type { Plugin, Rolldown, UserConfig } from 'vite'
import { reactPlugins } from './react.ts'
import { reactSpecifiers, sdkPackage } from './shared.ts'

export interface DashboardShellOptions {
  /** The shell app's folder, holding `index.html`. */
  root: string
  /** The build output the .NET package embeds: `index.html`, `shell.json`, and `assets/`. */
  outDir: string
}

/** The placeholder the server replaces with the import map, preloads, page state, and the nonced entry script. */
export const headPlaceholder = '<!--headless-dashboard:head-->'

const virtualPrefix = '\0headless-dashboard-shared:'

/** Turns a specifier into an entry name: `react-dom/client` → `react-dom-client`. */
function entryName(specifier: string) {
  return specifier.replace(/^@headless\//, '').replace(/[^a-zA-Z0-9-]/g, '-')
}

/**
 * The SDK's import-map specifiers: its root and each subpath export. `/testing` is left out, because it mounts a
 * module in tests and never loads in the browser.
 */
export function sdkSpecifiers(root: string): string[] {
  const require = createRequire(resolve(root, 'package.json'))
  const manifest = require(`${sdkPackage}/package.json`) as { exports?: Record<string, unknown> }

  return Object.keys(manifest.exports ?? { '.': null })
    .filter((key) => key !== './package.json' && key !== './testing')
    .map((key) => (key === '.' ? sdkPackage : `${sdkPackage}/${key.slice(2)}`))
}

/**
 * Builds the specifiers the import map shares as ES module entries of the shell's own build, so the shell and every
 * module run the same instances. React ships only CommonJS, so each React entry re-exports the names the CommonJS
 * module defines, read from the installed package at build time.
 */
export function sharedEntriesPlugin(root: string, specifiers: readonly string[]): Plugin {
  const require = createRequire(resolve(root, 'package.json'))

  return {
    name: 'headless-dashboard-shared-entries',

    resolveId(id) {
      return id.startsWith(virtualPrefix) ? id : null
    },

    load(id) {
      if (!id.startsWith(virtualPrefix)) {
        return null
      }

      const specifier = id.slice(virtualPrefix.length)

      if (!(reactSpecifiers as readonly string[]).includes(specifier)) {
        return `export * from ${JSON.stringify(specifier)}\n`
      }

      const names = Object.keys(require(specifier) as object).filter(
        (name) => name !== 'default' && name !== '__esModule' && /^[A-Za-z_$][\w$]*$/.test(name),
      )

      return [
        `import shared from ${JSON.stringify(specifier)}`,
        'export default shared',
        `export const { ${names.join(', ')} } = shared`,
        '',
      ].join('\n')
    },

    options(input) {
      const entries: Record<string, string> = {}

      for (const specifier of specifiers) {
        entries[entryName(specifier)] = virtualPrefix + specifier
      }

      const current = input.input
      const existing =
        typeof current === 'string' ? { index: current } : Array.isArray(current) ? { ...current } : (current ?? {})

      return { ...input, input: { ...existing, ...entries } }
    },
  }
}

function staticClosure(start: Rolldown.OutputChunk, bundle: Rolldown.OutputBundle) {
  const seen = new Set<string>()
  const pending = [...start.imports]

  while (pending.length > 0) {
    const fileName = pending.pop()!

    if (seen.has(fileName)) {
      continue
    }

    seen.add(fileName)
    const chunk = bundle[fileName]

    if (chunk?.type === 'chunk') {
      pending.push(...chunk.imports)
    }
  }

  return [...seen]
}

/**
 * Writes `shell.json` (entry, stylesheets, preloads, and each import-map specifier's file) and strips the tags Vite
 * injected into `index.html`, leaving only the placeholder: the server renders every script with its nonce.
 */
export function shellDocumentPlugin(root: string, specifiers: readonly string[]): Plugin {
  const html = resolve(root, 'index.html')

  return {
    name: 'headless-dashboard-shell-document',
    apply: 'build',
    enforce: 'post',

    generateBundle(_, bundle) {
      const chunks = Object.values(bundle).filter((file): file is Rolldown.OutputChunk => file.type === 'chunk')
      const entry = chunks.find((chunk) => chunk.isEntry && chunk.facadeModuleId === html)

      if (!entry) {
        this.error(`The shell build has no entry chunk for ${html}.`)
      }

      const imports: Record<string, string> = {}

      for (const specifier of specifiers) {
        const chunk = chunks.find((candidate) => candidate.isEntry && candidate.name === entryName(specifier))

        if (!chunk) {
          this.error(`The shell build has no entry for the shared specifier '${specifier}'.`)
        }

        imports[specifier] = chunk.fileName
      }

      const preload = staticClosure(entry, bundle)
      const css = new Set<string>()

      for (const fileName of [entry.fileName, ...preload, ...Object.values(imports)]) {
        const chunk = bundle[fileName]

        if (chunk?.type === 'chunk') {
          for (const file of chunk.viteMetadata?.importedCss ?? []) {
            css.add(file)
          }
        }
      }

      const document = bundle['index.html']

      if (document?.type !== 'asset') {
        this.error('The shell build wrote no index.html.')
      }

      const stripped = String(document.source)
        .replace(/[ \t]*<script\b[^>]*>[\s\S]*?<\/script>\n?/gi, '')
        .replace(/[ \t]*<link\b[^>]*\brel="(?:stylesheet|modulepreload)"[^>]*>\n?/gi, '')

      if (stripped.split(headPlaceholder).length !== 2) {
        this.error(`The shell's index.html must hold exactly one ${headPlaceholder} placeholder.`)
      }

      if (/<script\b/i.test(stripped) || /<base\b/i.test(stripped)) {
        this.error(`The shell's index.html must hold no <script> or <base> element.`)
      }

      document.source = stripped

      const manifest = { entry: entry.fileName, css: [...css], preload, imports }

      this.emitFile({ type: 'asset', fileName: 'shell.json', source: `${JSON.stringify(manifest, null, 2)}\n` })
    },
  }
}

/** A React Aria string table for one locale: `react-aria/dist/private/intl/table/fr-FR.mjs`. */
const reactAriaLocaleModule = /[\\/]react-aria(?:-components)?[\\/]dist[\\/].*[\\/]intl[\\/](?:.*[\\/])?([a-z]{2,3}-[A-Z]{2})\.mjs$/

/**
 * Empties React Aria's string tables for every locale but `en-US`. The dashboard ships in English, and React Aria
 * bundles the strings of 34 locales for each component, so the other 33 are dead weight in every entry. An emptied
 * table exports `undefined`, which React Aria's string dictionary drops and then falls back to `en-US`, the same
 * contract its own `@react-aria/optimize-locales-plugin` relies on.
 */
export function englishOnlyReactAriaPlugin(): Plugin {
  return {
    name: 'headless-dashboard-react-aria-locales',
    load(id) {
      const locale = reactAriaLocaleModule.exec(id)?.[1]

      return locale && locale !== 'en-US' ? 'export default undefined\n' : null
    },
  }
}

/**
 * The Vite configuration of the dashboard shell: one build holding the shell app and an ES module entry for each
 * import-map specifier (React, React DOM, the compiler runtime, and the SDK), so one copy of each serves every module.
 */
export function dashboardShell(options: DashboardShellOptions): UserConfig {
  const specifiers = [...reactSpecifiers, ...sdkSpecifiers(options.root)]

  return {
    root: options.root,
    // Every URL in the output is relative, because the server serves the build under any PathBase and base path.
    base: './',
    plugins: [
      ...reactPlugins(),
      englishOnlyReactAriaPlugin(),
      sharedEntriesPlugin(options.root, specifiers),
      shellDocumentPlugin(options.root, specifiers),
    ],
    build: {
      outDir: resolve(options.root, options.outDir),
      emptyOutDir: true,
      assetsDir: 'assets',
      // The polyfill is an inline script, which the document's Content Security Policy refuses.
      modulePreload: { polyfill: false },
      rolldownOptions: {
        // Keeps each shared entry's exports, which only modules outside this build import.
        preserveEntrySignatures: 'exports-only',
      },
    },
  }
}
