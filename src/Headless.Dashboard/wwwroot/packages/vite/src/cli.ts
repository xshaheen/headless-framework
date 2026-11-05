import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { formatBudgetResult, measureBudgets, type BudgetFile } from './budget.ts'
import { precompress } from './compress.ts'

// Runs after every Vite build of the workspace: holds the output to its size budgets, writes the precompressed siblings
// the server serves, then writes `.build-complete`. Exits 3 over budget and 2 on a usage error.
// The .NET build treats `.build-complete` as the SPA's output, so it is written last and removed first: a run that
// fails at any step leaves no stamp, and the next build reruns npm instead of embedding a partial dist.
const [command, distArgument, budgetArgument] = process.argv.slice(2)

if (command !== 'finalize' || !distArgument) {
  console.error('Usage: node packages/vite/src/cli.ts finalize <dist> [budget.json]')
  process.exit(2)
}

const dist = resolve(distArgument)
const stamp = join(dist, '.build-complete')
rmSync(stamp, { force: true })

const budgetPath = budgetArgument ? resolve(budgetArgument) : fileURLToPath(new URL('../budget.json', import.meta.url))

if (!existsSync(join(dist, 'index.html'))) {
  console.error(`No build output at ${dist}; run the Vite builds first.`)
  process.exit(2)
}

const { budgets } = JSON.parse(readFileSync(budgetPath, 'utf8')) as BudgetFile
const results = measureBudgets(dist, budgets)
const over = results.filter((result) => !result.ok)

for (const result of results) {
  console.log(`[budget] ${formatBudgetResult(result)}`)
}

if (over.length > 0) {
  console.error(`[budget] ${over.length} file(s) over budget (${budgetPath}):`)

  for (const result of over) {
    console.error(`  ${formatBudgetResult(result)}`)
  }

  process.exit(3)
}

console.log(`[compress] wrote ${precompress([join(dist, 'assets'), join(dist, 'modules')].filter((dir) => existsSync(dir)))} precompressed file(s).`)
writeFileSync(stamp, `${new Date().toISOString()}\n`)
