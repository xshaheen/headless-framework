import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

// Source-level guard rather than a mounted-component test (this SPA deliberately does
// not mount components in unit tests). Every value this dashboard renders — job request
// payloads, function example data — is server-supplied, and the dashboard adds no markup
// of its own, so `v-html` and manual innerHTML writes have no legitimate use here.
// Stored XSS via a job's request data was live until these sinks were removed.

// Vitest runs with the SPA root as cwd (vitest.config.ts lives there).
const srcDir = join(process.cwd(), 'src')

function sourceFiles(): string[] {
  return readdirSync(srcDir, { recursive: true, encoding: 'utf8' })
    .filter((entry) => entry.endsWith('.vue') || entry.endsWith('.ts'))
    .filter((entry) => !entry.includes('__tests__'))
}

describe('raw HTML sinks', () => {
  it('finds source files to scan', () => {
    expect(sourceFiles().length).toBeGreaterThan(10)
  })

  it.each([
    ['v-html', /v-html\s*=/],
    ['innerHTML', /\binnerHTML\s*=/],
    ['insertAdjacentHTML', /insertAdjacentHTML\s*\(/],
  ])('has no %s usage', (_name, pattern) => {
    const offenders = sourceFiles().filter((file) =>
      pattern.test(readFileSync(join(srcDir, file), 'utf8')),
    )

    expect(offenders).toEqual([])
  })
})
