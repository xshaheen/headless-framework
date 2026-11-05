import { readdirSync, readFileSync } from 'node:fs'
import { join, relative, sep } from 'node:path'
import { gzipSync } from 'node:zlib'

/** One size budget: a set of build files, measured one by one or together, against a gzip size. */
export interface Budget {
  name: string
  /** Globs relative to the build output: `*` matches within one folder, `**` across folders. */
  files: string[]
  exclude?: string[]
  /** `each` holds every matched file to the budget; `total` holds their sum. */
  measure: 'each' | 'total'
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

    if (budget.measure === 'total') {
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

function kb(bytes: number) {
  return `${(bytes / 1000).toFixed(1)} kB`
}

/** Describes one result in the build log, naming the file and both sizes. */
export function formatBudgetResult(result: BudgetResult) {
  const { budget, subject, bytes, ok } = result
  const what = budget.measure === 'total' ? `${subject} (total of ${budget.files.join(', ')})` : `${budget.name}: ${subject}`

  return ok
    ? `${what} is ${kb(bytes)} gzip, within its ${kb(budget.maxBytes)} budget.`
    : `${what} is ${kb(bytes)} gzip, over its ${kb(budget.maxBytes)} budget.`
}
