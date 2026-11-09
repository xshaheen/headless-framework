import { mkdtempSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { build } from 'vite'
import { beforeAll, describe, expect, it } from 'vitest'
import { dashboardShell } from '../src/shell.ts'

const require = createRequire(import.meta.url)
const sdkStylesheet = join(dirname(require.resolve('@headless/dashboard-sdk/package.json')), 'src/styles/sdk.css')

/**
 * Compiles the SDK stylesheet with the plugins the shell build uses, plus one `dark:` class no SDK source writes yet,
 * so the theme variant's compiled selector can be read. Returns the CSS as written, unminified.
 */
async function compileSdkStylesheet() {
  const root = mkdtempSync(join(tmpdir(), 'dashboard-sdk-styles-'))
  writeFileSync(join(root, 'entry.css'), `@import ${JSON.stringify(sdkStylesheet)};\n@source inline("dark:bg-muted");\n`)
  writeFileSync(join(root, 'main.js'), `import './entry.css'\n`)

  const shell = dashboardShell({ root: fileURLToPath(new URL('../../../apps/shell', import.meta.url)), outDir: 'unused' })
  const tailwind = (shell.plugins ?? []).flat().filter((plugin) => plugin && 'name' in plugin && plugin.name.startsWith('@tailwindcss/'))

  const result = await build({
    configFile: false,
    logLevel: 'silent',
    root,
    plugins: tailwind,
    build: { write: false, minify: false, cssMinify: false, rolldownOptions: { input: join(root, 'main.js') } },
  })

  const outputs = Array.isArray(result) ? result : [result]

  return outputs
    .flatMap((output) => ('output' in output ? output.output : []))
    .filter((file) => file.type === 'asset' && file.fileName.endsWith('.css'))
    .map((file) => (file.type === 'asset' ? String(file.source) : ''))
    .join('\n')
}

describe('SDK stylesheet', () => {
  let css = ''

  beforeAll(async () => {
    css = await compileSdkStylesheet()
  }, 60_000)

  it('renders dark with no stored theme and light only under data-theme="light"', () => {
    const root = /:root\s*\{([^}]*)\}/.exec(css)?.[1] ?? ''
    const light = /:root\[data-theme=['"]?light['"]?\]\s*\{([^}]*)\}/.exec(css)?.[1] ?? ''

    expect(root).toContain('color-scheme: dark')
    // The compiler writes oklch lightness as a percentage.
    expect(root).toMatch(/--ground:\s*oklch\(15\.5%/)
    expect(light).toContain('color-scheme: light')
    expect(light).toMatch(/--ground:\s*oklch\(97\.5%/)
  })

  it('applies a dark: class everywhere except under the light theme', () => {
    const rule = /\.dark\\:bg-muted([^{]*)\{([^}]*)\}/.exec(css)

    expect(rule?.[1]?.replace(/\s+/g, '').replace(/"/g, '')).toBe(':not(:where([data-theme=light],[data-theme=light]*))')
    expect(rule?.[2]).toContain('background-color: var(--raised)')
  })

  it("maps shadcn's semantic classes onto the dashboard's tokens", () => {
    expect(css).toMatch(/\.bg-card\s*\{\s*background-color: var\(--panel\)/)
    expect(css).toMatch(/\.text-muted-foreground\s*\{\s*color: var\(--muted\)/)
    expect(css).toMatch(/\.border-border\s*\{\s*border-color: var\(--line\)/)
  })

  it('pads and places with logical properties only, so a right-to-left page mirrors', () => {
    expect(css).toMatch(/padding-inline-start/)
    expect(css).not.toMatch(/\b(?:padding|margin|border)-(?:left|right)\b/)
    expect(css).not.toMatch(/[{;\s](?:left|right)\s*:/)
  })
})
