import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { build } from 'vite'
import { dashboardModule } from '../src/module.ts'

/** Builds the fixture module `name` with the module preset into a fresh folder and returns that folder. */
export async function buildFixture(name: string) {
  const outDir = mkdtempSync(join(tmpdir(), `dashboard-module-${name}-`))
  // Vitest sets NODE_ENV=test, which makes the React plugin emit react/jsx-dev-runtime; `vite build` runs in production.
  const nodeEnv = process.env.NODE_ENV
  process.env.NODE_ENV = 'production'

  try {
    await build({
      ...dashboardModule({
        root: fileURLToPath(new URL(`./fixtures/${name}`, import.meta.url)),
        entry: 'src/index.ts',
        outDir,
      }),
      configFile: false,
      logLevel: 'silent',
    })
  } finally {
    process.env.NODE_ENV = nodeEnv
  }

  return outDir
}
