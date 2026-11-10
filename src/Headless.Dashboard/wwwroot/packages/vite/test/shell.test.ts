import { build, type Plugin } from 'vite'
import { describe, expect, it } from 'vitest'
import { englishOnlyReactAriaPlugin } from '../src/shell.ts'

const entryId = '\0react-aria-table-entry'

/** Bundles React Aria's table, the component whose string tables carry the most locales, and returns the output. */
async function bundleReactAriaTable(plugins: Plugin[]) {
  const entry: Plugin = {
    name: 'entry',
    resolveId: (id) => (id === entryId ? id : null),
    load: (id) => (id === entryId ? `export { Table } from 'react-aria-components'\n` : null),
  }

  const result = await build({
    configFile: false,
    logLevel: 'silent',
    plugins: [entry, ...plugins],
    build: {
      write: false,
      minify: false,
      rolldownOptions: {
        input: entryId,
        // A Vite app build drops an entry's exports, which would tree-shake the whole table away.
        preserveEntrySignatures: 'exports-only',
        external: (id) => id === 'react' || id === 'react-dom' || id.startsWith('react/') || id.startsWith('react-dom/'),
      },
    },
  })

  const outputs = Array.isArray(result) ? result : [result]

  return outputs
    .flatMap((output) => ('output' in output ? output.output : []))
    .map((file) => (file.type === 'chunk' ? file.code : ''))
    .join('\n')
}

describe('shell build', () => {
  it("keeps React Aria's English strings and drops the other locales'", async () => {
    const plain = await bundleReactAriaTable([])
    const englishOnly = await bundleReactAriaTable([englishOnlyReactAriaPlugin()])

    // Without the plugin the German table strings ship, so their absence below is the plugin's doing.
    expect(plain).toContain('aufsteigend')
    expect(englishOnly).toContain('sorted by column')
    expect(englishOnly).not.toContain('aufsteigend')
    expect(englishOnly).not.toContain('sortiert nach Spalte')
  }, 60_000)
})
