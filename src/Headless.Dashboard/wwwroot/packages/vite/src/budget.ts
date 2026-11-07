import { readdirSync, readFileSync } from 'node:fs'
import { join, relative, sep } from 'node:path'
import { gzipSync } from 'node:zlib'

/** One size budget: a set of build files, measured one by one or together, against a gzip size. */
export interface Budget {
  name: string
  /**
   * Globs relative to the build output: `*` matches within one folder, `**` across folders. A `first-load` budget
   * takes no globs: it reads its files from `shell.json`.
   */
  files: string[]
  exclude?: string[]
  /**
   * `each` holds every matched file to the budget; `total` holds their sum; `first-load` holds the sum of what the
   * shell document loads before anything is imported on demand: its entry, the entry's preloaded static imports, and
   * its stylesheets, as `shell.json` lists them.
   */
  measure: 'each' | 'total' | 'first-load'
  maxBytes: number
}

export interface BudgetFile {
  budgets: Budget[]
}

export interface BudgetResult {
  budget: Budget
  /** The file measured, or the budget's name for a total. */
  subject: string
  bytes: number
  ok: boolean
}

function globToRegExp(glob: string) {
  let pattern = ''

  for (let at = 0; at < glob.length; at++) {
    const char = glob[at]!

    if (char === '*' && glob[at + 1] === '*') {
      // `**/` matches zero or more folders.
      pattern += glob[at + 2] === '/' ? '(?:.*/)?' : '.*'
      at += glob[at + 2] === '/' ? 2 : 1
    } else if (char === '*') {
      pattern += '[^/]*'
    } else if (char === '?') {
      pattern += '[^/]'
    } else {
      pattern += char.replace(/[.+^${}()|[\]\\]/g, '\\$&')
    }
  }

  return new RegExp(`^${pattern}$`)
}

function listFiles(root: string): string[] {
  return readdirSync(root, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile())
    .map((entry) => relative(root, join(entry.parentPath, entry.name)).split(sep).join('/'))
    .sort()
}

/** The gzip size the budgets measure: maximum compression, as the build precompresses it. */
export function gzipSize(content: Uint8Array) {
  return gzipSync(content, { level: 9 }).length
}

/** Measures every budget against the build output in `dist`. */
export function measureBudgets(dist: string, budgets: readonly Budget[]): BudgetResult[] {
  // Precompressed siblings are copies of a measured file, not more code.
  const files = listFiles(dist).filter((file) => !file.endsWith('.br') && !file.endsWith('.gz'))
  const results: BudgetResult[] = []

  for (const budget of budgets) {
    const include = budget.files.map(globToRegExp)
    const exclude = (budget.exclude ?? []).map(globToRegExp)
    const matched = files.filter(
      (file) => include.some((re) => re.test(file)) && !exclude.some((re) => re.test(file)),
    )
    const sizes = matched.map((file) => ({ file, bytes: gzipSize(readFileSync(join(dist, file))) }))

    if (budget.measure === 'first-load') {
      const bytes = firstLoadFiles(dist).reduce((sum, file) => sum + gzipSize(readFileSync(join(dist, file))), 0)

      results.push({ budget, subject: budget.name, bytes, ok: bytes <= budget.maxBytes })
    } else if (budget.measure === 'total') {
      const bytes = sizes.reduce((sum, size) => sum + size.bytes, 0)

      results.push({ budget, subject: budget.name, bytes, ok: bytes <= budget.maxBytes })
    } else {
      for (const { file, bytes } of sizes) {
        results.push({ budget, subject: file, bytes, ok: bytes <= budget.maxBytes })
      }
    }
  }

  return results
}

/**
 * The files the shell document loads before anything is imported on demand. The entry imports its chunks by relative
 * path, and the build lists that static closure as the preloads, so the three lists are the whole first load.
 */
export function firstLoadFiles(dist: string): string[] {
  const shell: unknown = JSON.parse(readFileSync(join(dist, 'shell.json'), 'utf8'))
  const strings = (value: unknown) => Array.isArray(value) && value.every((item) => typeof item === 'string')

  if (
    typeof shell !== 'object' ||
    shell === null ||
    !('entry' in shell) ||
    typeof shell.entry !== 'string' ||
    !('preload' in shell) ||
    !strings(shell.preload) ||
    !('css' in shell) ||
    !strings(shell.css)
  ) {
    throw new Error(`${join(dist, 'shell.json')} must hold an entry and its preload and css lists.`)
  }

  return [...new Set([shell.entry, ...(shell.preload as string[]), ...(shell.css as string[])])]
}

function kb(bytes: number) {
  return `${(bytes / 1000).toFixed(1)} kB`
}

/** Describes one result in the build log, naming the file and both sizes. */
export function formatBudgetResult(result: BudgetResult) {
  const { budget, subject, bytes, ok } = result
  const what =
    budget.measure === 'first-load'
      ? `${subject} (entry, preloads, and stylesheets in shell.json)`
      : budget.measure === 'total'
        ? `${subject} (total of ${budget.files.join(', ')})`
        : `${budget.name}: ${subject}`

  return ok
    ? `${what} is ${kb(bytes)} gzip, within its ${kb(budget.maxBytes)} budget.`
    : `${what} is ${kb(bytes)} gzip, over its ${kb(budget.maxBytes)} budget.`
}
