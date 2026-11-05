import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { firstLoadFiles, formatBudgetResult, gzipSize, measureBudgets, type Budget } from '../src/index.ts'

const cli = fileURLToPath(new URL('../src/cli.ts', import.meta.url))

// Random bytes do not compress, so a file's gzip size stays close to its length.
function incompressible(bytes: number) {
  return Buffer.from(Array.from({ length: bytes }, () => Math.floor(Math.random() * 256)))
}

function dist(files: Record<string, Buffer | string>) {
  const root = mkdtempSync(join(tmpdir(), 'dashboard-budget-'))

  for (const [name, content] of Object.entries(files)) {
    mkdirSync(dirname(join(root, name)), { recursive: true })
    writeFileSync(join(root, name), content)
  }

  return root
}

const viewChunk: Budget = { name: 'View chunk', files: ['modules/*/chunks/**/*.js'], measure: 'each', maxBytes: 2000 }

describe('size budgets', () => {
  it('passes files within their budget', () => {
    const root = dist({ 'modules/jobs/chunks/runs.js': 'export const x = 1\n'.repeat(100) })
    const [result] = measureBudgets(root, [viewChunk])

    expect(result).toMatchObject({ subject: 'modules/jobs/chunks/runs.js', ok: true })
  })

  it('fails a chunk raised over its budget, naming the file and both sizes', () => {
    const big = incompressible(3000)
    const root = dist({
      'modules/jobs/chunks/runs.js': big,
      'modules/jobs/chunks/cron.js': 'export {}\n',
    })
    const over = measureBudgets(root, [viewChunk]).filter((result) => !result.ok)

    expect(over).toHaveLength(1)
    expect(over[0]!.bytes).toBe(gzipSize(big))
    expect(formatBudgetResult(over[0]!)).toBe(
      `View chunk: modules/jobs/chunks/runs.js is ${(gzipSize(big) / 1000).toFixed(1)} kB gzip, over its 2.0 kB budget.`,
    )
  })

  it('holds a total budget to the sum of its files and ignores precompressed siblings', () => {
    const root = dist({
      'assets/a.js': incompressible(800),
      'assets/b.css': incompressible(800),
      'assets/a.js.br': incompressible(5000),
      'assets/a.js.gz': incompressible(5000),
    })
    const [result] = measureBudgets(root, [
      { name: 'Shell', files: ['assets/**/*.js', 'assets/**/*.css'], measure: 'total', maxBytes: 1500 },
    ])

    expect(result!.ok).toBe(false)
    expect(result!.bytes).toBeGreaterThan(1500)
    expect(result!.bytes).toBeLessThan(2000)
  })

  it('holds the first load to the entry, its preloads, and its stylesheets, and leaves on-demand chunks out', () => {
    const root = dist({
      'shell.json': JSON.stringify({
        entry: 'assets/index.js',
        preload: ['assets/react-dom.js'],
        css: ['assets/shell.css'],
        imports: { '@headless/dashboard-sdk/router': 'assets/router.js' },
      }),
      'assets/index.js': incompressible(400),
      'assets/react-dom.js': incompressible(400),
      'assets/shell.css': incompressible(400),
      'assets/router.js': incompressible(5000),
    })
    const [result] = measureBudgets(root, [{ name: 'First load', files: [], measure: 'first-load', maxBytes: 1500 }])

    expect(firstLoadFiles(root)).toEqual(['assets/index.js', 'assets/react-dom.js', 'assets/shell.css'])
    expect(result).toMatchObject({ subject: 'First load', ok: true })
    expect(result!.bytes).toBeLessThan(1500)
    expect(formatBudgetResult(result!)).toMatch(/^First load \(entry, preloads, and stylesheets in shell\.json\) is /)
  })

  it('refuses a shell.json without its entry or lists', () => {
    const root = dist({ 'shell.json': JSON.stringify({ entry: 'assets/index.js' }) })

    expect(() => firstLoadFiles(root)).toThrow(/must hold an entry and its preload and css lists/)
  })

  it('leaves excluded files out', () => {
    const root = dist({ 'assets/lazy.js': incompressible(3000) })
    const results = measureBudgets(root, [
      { name: 'Shell', files: ['assets/*.js'], exclude: ['assets/lazy.js'], measure: 'each', maxBytes: 100 },
    ])

    expect(results).toEqual([])
  })

  it('fails the build script over budget', () => {
    const root = dist({ 'index.html': '<!doctype html>', 'modules/jobs/chunks/runs.js': incompressible(3000) })
    const budgetFile = join(root, 'budget.json')
    writeFileSync(budgetFile, JSON.stringify({ budgets: [viewChunk] }))

    const run = spawnSync(process.execPath, [cli, 'finalize', root, budgetFile], { encoding: 'utf8' })

    expect(run.status).toBe(3)
    expect(run.stderr).toMatch(/View chunk: modules\/jobs\/chunks\/runs\.js is \d+\.\d kB gzip, over its 2\.0 kB budget/)
  })

  it('writes precompressed siblings when the build is within budget', () => {
    const root = dist({ 'index.html': '<!doctype html>', 'assets/a.js': 'export const x = 1\n'.repeat(200) })
    const budgetFile = join(root, 'budget.json')
    writeFileSync(budgetFile, JSON.stringify({ budgets: [] }))

    const run = spawnSync(process.execPath, [cli, 'finalize', root, budgetFile], { encoding: 'utf8' })

    expect(run.status).toBe(0)
    expect(run.stdout).toContain('[compress] wrote 2 precompressed file(s).')
  })
})
