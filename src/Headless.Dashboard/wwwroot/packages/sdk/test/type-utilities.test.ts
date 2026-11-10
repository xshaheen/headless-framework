import { readdirSync, readFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const sourceRoot = resolve(import.meta.dirname, '../src')
const stringLiteral = /'[^'\n]*'|"[^"\n]*"|`[^`]*`/g
const typeUtility = /(?:^|[\s:`'"])type-[a-z-]+(?=\s|$|[`'"])/
// A variant-prefixed font utility (`aria-pressed:font-medium`) is emitted after the plain utilities and still wins,
// so only a plain one is lost.
const fontUtility = /(?:^|[\s`'"])font-(?:sans|mono|thin|light|normal|medium|semibold|bold|extrabold|black)(?=\s|$|[`'"])/

describe('type utilities', () => {
  // A `type-*` utility is a `font` shorthand that Tailwind emits after the `font-*` utilities, so pairing them in one
  // class list silently drops the family or weight.
  it('are never paired with a font family or weight utility', () => {
    const pairs = readdirSync(sourceRoot, { recursive: true, encoding: 'utf8' })
      .filter((file) => /\.(?:ts|tsx)$/.test(file))
      .flatMap((file) =>
        (readFileSync(join(sourceRoot, file), 'utf8').match(stringLiteral) ?? [])
          .filter((literal) => typeUtility.test(literal) && fontUtility.test(literal))
          .map((literal) => `${file}: ${literal}`),
      )

    expect(pairs).toEqual([])
  })
})
