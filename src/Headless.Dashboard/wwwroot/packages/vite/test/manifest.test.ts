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
      nav: [
        { id: 'fixture.nav.runs', label: 'Runs', to: 'runs', requires: 'view' },
        { id: 'fixture.nav.cron', label: 'Cron jobs', to: 'cron' },
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

  it.each([
    ['a computed nav label', `{ id: 'm', nav: [{ id: 'm.nav', label: t('Runs'), to: 'runs' }] }`, /nav\[0\]\.label must be a literal/],
    ['a variable id', `{ id: moduleId }`, /id must be a literal/],
    ['a shorthand label', `{ id: 'm', nav: [{ id: 'n', label, to: '' }] }`, /nav\[0\]\.label must be a literal/],
    ['a template with an expression', `{ id: \`m-\${x}\` }`, /id must be a literal/],
    ['a spread nav', `{ id: 'm', nav: [...entries] }`, /nav\[0\] must be written out/],
    ['a computed route import', `{ id: 'm', routes: [{ path: '', lazy: () => import(view) }] }`, /routes\[0\]\.lazy must be written as/],
    ['an unknown permission', `{ id: 'm', commands: [{ id: 'c', label: 'C', requires: 'admin', run() {} }] }`, /'view' or 'operate'/],
    ['an unknown field', `{ id: 'm', slots: [] }`, /no field 'slots'/],
  ])('fails on %s', (_, definition, message) => {
    const read = () => readModuleDefinition(source(definition), 'module.ts')

    expect(read).toThrow(ModuleDefinitionError)
    expect(read).toThrow(message)
  })

  it('reads literals wrapped in as const and satisfies', () => {
    const definition = readModuleDefinition(
      source(`{ id: 'm' as const, nav: [{ id: \`n\`, label: 'L', to: '' } satisfies object] }`),
      'module.ts',
    )

    expect(definition).toEqual({
      id: 'm',
      nav: [{ id: 'n', label: 'L', to: '' }],
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
