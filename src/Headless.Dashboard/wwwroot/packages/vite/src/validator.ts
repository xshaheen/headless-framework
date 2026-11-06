import {
  dashboardProducts,
  stateRoles,
  type DashboardProduct,
  type DefinitionSidebarSection,
} from './definition.ts'
import { isolatedLibraries } from './shared.ts'

/** A module's `manifest.json`, the file the dashboard's server reads beside the module's entry. */
export interface ModuleManifest {
  id: string
  /** The SDK version the module was built against; the shell refuses a different major or a newer version. */
  sdkVersion: string
  entry: string
  css: string[]
  product: DashboardProduct
  sidebar: DefinitionSidebarSection[]
  routes: { path: string; chunk: string }[]
  objectKinds: Record<string, unknown>
  commands: { id: string; label: string; requires?: 'view' | 'operate' }[]
  events: string[]
}

/** One built JavaScript file, as the checks below see it. */
export interface BuiltChunk {
  fileName: string
  code: string
  /** The source modules bundled into the file. */
  moduleIds: readonly string[]
}

const semver = /^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.+-]+)?$/

/**
 * Checks a module's manifest against its build output: unique ids, routes that resolve to built chunks, an entry
 * that exists, and a readable built-against SDK version. Returns one message per problem; empty means valid.
 */
export function validateManifest(manifest: ModuleManifest, files: ReadonlySet<string>): string[] {
  const errors: string[] = []

  if (!manifest.id) {
    errors.push('The manifest has no module id.')
  }

  if (!semver.test(manifest.sdkVersion)) {
    errors.push(`The built-against SDK version '${manifest.sdkVersion}' is not a major.minor.patch version.`)
  }

  if (!files.has(manifest.entry)) {
    errors.push(`The entry '${manifest.entry}' is not in the build output.`)
  }

  for (const css of manifest.css) {
    if (!files.has(css)) {
      errors.push(`The stylesheet '${css}' is not in the build output.`)
    }
  }

  if (!(dashboardProducts as readonly string[]).includes(manifest.product)) {
    errors.push(`The product '${manifest.product}' is not one of ${dashboardProducts.join(', ')}.`)
  }

  // Sidebar entries and commands share the command palette, so one id names one item across both.
  const seen = new Map<string, string>()
  const items: { at: string; id: string }[] = [
    ...manifest.sidebar.flatMap((section, sectionIndex) =>
      section.entries.map((entry, index) => ({ at: `sidebar[${sectionIndex}].entries[${index}]`, id: entry.id })),
    ),
    ...manifest.commands.map((command, index) => ({ at: `commands[${index}]`, id: command.id })),
  ]

  for (const { at, id } of items) {
    const first = seen.get(id)

    if (first) {
      errors.push(`${at} reuses the id '${id}' of ${first}; every sidebar entry and command needs its own id.`)
    } else {
      seen.set(id, at)
    }
  }

  manifest.sidebar.forEach((section, sectionIndex) => {
    section.entries.forEach((entry, index) => {
      const at = `sidebar[${sectionIndex}].entries[${index}]`

      if (!entry.icon === !entry.state) {
        errors.push(`${at} needs exactly one of icon or state.`)
      }

      if (entry.state && !(stateRoles as readonly string[]).includes(entry.state)) {
        errors.push(`${at}.state '${entry.state}' is not one of ${stateRoles.join(', ')}.`)
      }
    })
  })

  const paths = new Map<string, number>()

  manifest.routes.forEach((route, index) => {
    const first = paths.get(route.path)

    if (first !== undefined) {
      errors.push(`routes[${index}] repeats the path '${route.path}' of routes[${first}].`)
    } else {
      paths.set(route.path, index)
    }

    if (!route.chunk || !files.has(route.chunk)) {
      errors.push(`routes[${index}] ('${route.path}') names the chunk '${route.chunk}', which is not in the build output.`)
    }
  })

  return errors
}

// `eval(` and `new Function(` compile strings at run time, which the dashboard's policy blocks; a `<script` in the
// output builds an inline script the policy blocks too. Either works in development and fails in production.
const dynamicCode: readonly { pattern: RegExp; what: string }[] = [
  { pattern: /(?<![\w$.])eval\s*\(/, what: 'eval()' },
  { pattern: /\bnew\s+Function\s*\(/, what: 'new Function()' },
  { pattern: /<script[\s>]/i, what: 'an inline <script>' },
]

/** Finds code the dashboard's Content Security Policy would refuse at run time: `eval`, `new Function`, inline script. */
export function checkDynamicCode(chunks: readonly BuiltChunk[]): string[] {
  const errors: string[] = []

  for (const chunk of chunks) {
    for (const { pattern, what } of dynamicCode) {
      if (pattern.test(chunk.code)) {
        errors.push(`${chunk.fileName} uses ${what}, which the dashboard's Content Security Policy refuses.`)
      }
    }
  }

  return errors
}

function packageOf(moduleId: string) {
  const at = moduleId.lastIndexOf('/node_modules/')

  if (at < 0) {
    return null
  }

  const rest = moduleId.slice(at + '/node_modules/'.length).replace(/^\0/, '')
  const parts = rest.split('/')

  return parts[0]!.startsWith('@') ? `${parts[0]}/${parts[1]}` : parts[0]!
}

/**
 * Fails when a module's bundle holds React, React Aria, TanStack Query, or React Router code. Each must come from
 * the shell's single copy through the import map; a bundled copy breaks hooks or loses the shell's context.
 */
export function checkBundleIsolation(chunks: readonly BuiltChunk[]): string[] {
  const errors: string[] = []

  for (const chunk of chunks) {
    const found = new Map<string, string>()

    for (const moduleId of chunk.moduleIds) {
      const name = packageOf(moduleId.replaceAll('\\', '/'))

      if (!name) {
        continue
      }

      for (const library of isolatedLibraries) {
        const matches = library.packages.some((pkg) => (pkg.endsWith('/') ? name.startsWith(pkg) : name === pkg))

        if (matches && !found.has(library.name)) {
          found.set(library.name, name)
        }
      }
    }

    for (const [library, pkg] of found) {
      errors.push(
        `${chunk.fileName} bundles ${library} code (${pkg}); a module must import it through @headless/dashboard-sdk or the shared react specifiers.`,
      )
    }
  }

  return errors
}
