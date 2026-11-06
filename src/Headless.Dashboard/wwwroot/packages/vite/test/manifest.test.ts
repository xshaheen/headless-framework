import { existsSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { ModuleDefinitionError, readModuleDefinition } from '../src/definition.ts'
import { buildFixture } from './build-fixture.ts'

const sdkVersion = (
  JSON.parse(readFileSync(new URL('../../sdk/package.json', import.meta.url), 'utf8')) as { version: string }
).version

function source(definition: string) {
  return `import { defineModule } from '@headless/dashboard-sdk'\nexport default defineModule(${definition})\n`
}

describe('module manifest', () => {
  it('writes manifest.json from the defineModule literal', async () => {
    const outDir = await buildFixture('basic')
    const manifest = JSON.parse(readFileSync(join(outDir, 'manifest.json'), 'utf8'))

    expect(manifest).toMatchObject({
      id: 'fixture',
      sdkVersion,
      entry: 'index.js',
      css: [],
      product: 'jobs',
      sidebar: [
        { entries: [{ id: 'fixture.nav.runs', label: 'Runs', route: 'runs', icon: 'runs', requires: 'view' }] },
        {
          label: 'States',
          entries: [
            { id: 'fixture.nav.failed', label: 'Failed', route: 'runs?status=Failed', state: 'fail', countKey: 'Failed' },
            { id: 'fixture.nav.cron', label: 'Cron jobs', route: 'cron', icon: 'cron' },
          ],
        },
      ],
      objectKinds: { run: {} },
      commands: [{ id: 'fixture.add-run', label: 'Add run', requires: 'operate' }],
      events: ['run.updated', 'run.progress'],
    })
    expect(manifest.routes.map((route: { path: string }) => route.path)).toEqual(['runs', 'cron'])

    for (const route of manifest.routes as { chunk: string }[]) {
      expect(route.chunk).toMatch(/^chunks\/.+\.js$/)
      expect(existsSync(join(outDir, route.chunk))).toBe(true)
    }

    expect(manifest.routes[0].chunk).not.toBe(manifest.routes[1].chunk)
  })

  it('keeps React and the SDK out of the module bundle', async () => {
    const outDir = await buildFixture('basic')
    const manifest = JSON.parse(readFileSync(join(outDir, 'manifest.json'), 'utf8'))
    const entry = readFileSync(join(outDir, 'index.js'), 'utf8')
    const view = readFileSync(join(outDir, manifest.routes[0].chunk), 'utf8')

    expect(entry).toMatch(/from\s*"@headless\/dashboard-sdk"/)
    expect(view).toMatch(/from\s*"react\/jsx-runtime"/)
    expect(view).toMatch(/from\s*"react\/compiler-runtime"/)
    expect(entry + view).not.toContain('process.env.NODE_ENV')
  })

  const entry = (fields: string) => `{ id: 'm', product: 'jobs', sidebar: [{ entries: [${fields}] }] }`

  it.each([
    ['a computed entry label', entry(`{ id: 'm.nav', label: t('Runs'), route: 'runs', icon: 'runs' }`), /sidebar\[0\]\.entries\[0\]\.label must be a literal/],
    ['a variable id', `{ id: moduleId, product: 'jobs' }`, /id must be a literal/],
    ['a shorthand label', entry(`{ id: 'n', label, route: '', icon: 'runs' }`), /sidebar\[0\]\.entries\[0\]\.label must be a literal/],
    ['a template with an expression', `{ id: \`m-\${x}\`, product: 'jobs' }`, /id must be a literal/],
    ['a sidebar held in a variable', `{ id: 'm', product: 'jobs', sidebar: sections }`, /sidebar must be an array literal/],
    ['a spread sidebar', `{ id: 'm', product: 'jobs', sidebar: [...sections] }`, /sidebar\[0\] must be written out/],
    ['spread entries', `{ id: 'm', product: 'jobs', sidebar: [{ entries: [...entries] }] }`, /sidebar\[0\]\.entries\[0\] must be written out/],
    ['an entry with both icon and state', entry(`{ id: 'n', label: 'L', route: '', icon: 'runs', state: 'fail' }`), /exactly one of icon or state/],
    ['an unknown state role', entry(`{ id: 'n', label: 'L', route: '', state: 'needs' }`), /state must be one of/],
    ['an unknown product', `{ id: 'm', product: 'billing' }`, /product must be one of 'jobs', .*not 'billing'/],
    ['a computed product', `{ id: 'm', product: products.jobs }`, /product must be a literal/],
    ['a missing product', `{ id: 'm' }`, /needs a product/],
    ['a computed route import', `{ id: 'm', product: 'jobs', routes: [{ path: '', lazy: () => import(view) }] }`, /routes\[0\]\.lazy must be written as/],
    ['an unknown permission', `{ id: 'm', product: 'jobs', commands: [{ id: 'c', label: 'C', requires: 'admin', run() {} }] }`, /'view' or 'operate'/],
    ['an unknown field', `{ id: 'm', product: 'jobs', slots: [] }`, /no field 'slots'/],
    ['the retired nav field', `{ id: 'm', product: 'jobs', nav: [] }`, /no field 'nav'/],
  ])('fails on %s', (_, definition, message) => {
    const read = () => readModuleDefinition(source(definition), 'module.ts')

    expect(read).toThrow(ModuleDefinitionError)
    expect(read).toThrow(message)
  })

  it('reads literals wrapped in as const and satisfies', () => {
    const definition = readModuleDefinition(
      source(
        `{ id: 'm' as const, product: 'jobs' as const, sidebar: [{ entries: [{ id: \`n\`, label: 'L', route: '', icon: 'runs' } satisfies object] }] }`,
      ),
      'module.ts',
    )

    expect(definition).toEqual({
      id: 'm',
      product: 'jobs',
      sidebar: [{ entries: [{ id: 'n', label: 'L', route: '', icon: 'runs' }] }],
      routes: [],
      objectKinds: {},
      commands: [],
      events: [],
    })
  })

  it('fails when the entry does not export a defineModule call', () => {
    expect(() => readModuleDefinition(`export default {}`, 'module.ts')).toThrow(/must export default a defineModule/)
  })
})
