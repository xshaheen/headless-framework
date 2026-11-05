import { describe, expect, it } from 'vitest'
import { checkBundleIsolation, checkDynamicCode, validateManifest, type ModuleManifest } from '../src/index.ts'
import { buildFixture } from './build-fixture.ts'

function manifest(overrides: Partial<ModuleManifest> = {}): ModuleManifest {
  return {
    id: 'jobs',
    sdkVersion: '1.0.0',
    entry: 'index.js',
    css: [],
    nav: [{ id: 'jobs.nav.runs', label: 'Runs', to: 'runs' }],
    routes: [{ path: 'runs', chunk: 'chunks/runs.js' }],
    objectKinds: {},
    commands: [],
    events: [],
    ...overrides,
  }
}

const files = new Set(['index.js', 'chunks/runs.js'])

describe('manifest validator', () => {
  it('accepts a valid manifest', () => {
    expect(validateManifest(manifest(), files)).toEqual([])
  })

  it('fails two nav entries with one id', () => {
    const errors = validateManifest(
      manifest({
        nav: [
          { id: 'jobs.nav.runs', label: 'Runs', to: 'runs' },
          { id: 'jobs.nav.runs', label: 'Cron', to: 'cron' },
        ],
      }),
      files,
    )

    expect(errors).toEqual([expect.stringMatching(/nav\[1\] reuses the id 'jobs\.nav\.runs' of nav\[0\]/)])
  })

  it('fails a command that reuses a nav id', () => {
    const errors = validateManifest(
      manifest({ commands: [{ id: 'jobs.nav.runs', label: 'Runs' }] }),
      files,
    )

    expect(errors).toEqual([expect.stringMatching(/commands\[0\] reuses the id/)])
  })

  it('fails a route whose chunk is missing', () => {
    const errors = validateManifest(manifest({ routes: [{ path: 'runs', chunk: 'chunks/gone.js' }] }), files)

    expect(errors).toEqual([expect.stringMatching(/routes\[0\] \('runs'\) names the chunk 'chunks\/gone\.js'/)])
  })

  it('fails a repeated route path', () => {
    const errors = validateManifest(
      manifest({
        routes: [
          { path: 'runs', chunk: 'chunks/runs.js' },
          { path: 'runs', chunk: 'chunks/runs.js' },
        ],
      }),
      files,
    )

    expect(errors).toEqual([expect.stringMatching(/routes\[1\] repeats the path 'runs'/)])
  })

  it.each(['', '1.0', 'latest'])('fails the built-against SDK version %j', (sdkVersion) => {
    expect(validateManifest(manifest({ sdkVersion }), files)).toEqual([
      expect.stringMatching(/is not a major\.minor\.patch version/),
    ])
  })

  it('fails an entry missing from the output', () => {
    expect(validateManifest(manifest({ entry: 'main.js' }), files)).toEqual([
      expect.stringMatching(/entry 'main\.js' is not in the build output/),
    ])
  })
})

describe('bundle check', () => {
  const chunk = (moduleIds: string[]) => ({ fileName: 'chunks/view.js', code: '', moduleIds })

  it('passes a module that bundles only its own code', () => {
    expect(checkBundleIsolation([chunk(['/repo/modules/jobs/src/views/runs.tsx'])])).toEqual([])
  })

  it.each([
    ['React', '/repo/node_modules/react/cjs/react.production.js'],
    ['React', '/repo/node_modules/react-dom/cjs/react-dom-client.production.js'],
    ['React Aria', '/repo/node_modules/react-aria-components/dist/Button.mjs'],
    ['React Aria', '/repo/node_modules/@react-aria/utils/dist/main.mjs'],
    ['TanStack Query', '/repo/node_modules/@tanstack/query-core/build/modern/queryClient.js'],
    ['React Router', '/repo/node_modules/react-router/dist/development/index.mjs'],
  ])('fails a module bundling %s code (%s)', (library, moduleId) => {
    expect(checkBundleIsolation([chunk(['/repo/modules/jobs/src/index.ts', moduleId])])).toEqual([
      expect.stringContaining(`chunks/view.js bundles ${library} code`),
    ])
  })

  it('does not mistake a package whose name starts like React', () => {
    expect(checkBundleIsolation([chunk(['/repo/node_modules/react-is-not-react/index.js'])])).toEqual([])
  })

  it('fails the build of a module that imports TanStack Query directly', async () => {
    await expect(buildFixture('leaky')).rejects.toThrow(/bundles TanStack Query code \(@tanstack\/react-query\)/)
  })
})

describe('dynamic code check', () => {
  const chunk = (code: string) => ({ fileName: 'index.js', code, moduleIds: [] })

  it.each([
    ['eval()', 'const x = eval("1")'],
    ['new Function()', 'const f = new Function("return 1")'],
    ['an inline <script>', 'el.innerHTML = "<script>alert(1)</script>"'],
  ])('fails code that uses %s', (what, code) => {
    expect(checkDynamicCode([chunk(code)])).toEqual([expect.stringContaining(`index.js uses ${what}`)])
  })

  it('passes a method named eval on an object', () => {
    expect(checkDynamicCode([chunk('parser.eval(tree); const medieval = 1')])).toEqual([])
  })
})
