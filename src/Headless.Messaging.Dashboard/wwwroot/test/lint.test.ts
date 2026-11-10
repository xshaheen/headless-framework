import { resolve } from 'node:path'
import { ESLint } from 'eslint'
import { describe, expect, it } from 'vitest'

const moduleRoot = resolve(import.meta.dirname, '..')
const eslint = new ESLint({ cwd: moduleRoot })

/** Lints `code` as if it were a view under `src/`, where the module rules apply. */
async function lintView(code: string) {
  const [result] = await eslint.lintText(code, { filePath: resolve(moduleRoot, 'src/probe.tsx') })

  return result!.messages.map((message) => ({ ruleId: message.ruleId, message: message.message }))
}

const refusal = {
  ruleId: 'no-restricted-syntax',
  message: expect.stringMatching(/sets no className/),
}

describe('module lint rules', () => {
  it.each([
    ['an SDK class string', `export const Probe = () => <div className="hd-banner">Stale</div>\n`],
    ['a Tailwind utility', `export const Probe = () => <div className="ms-auto flex gap-2">Stale</div>\n`],
    ['a class expression', `export const Probe = () => <div className={'hd-hint'}>Stale</div>\n`],
    ['a class template', 'export const Probe = ({ on }: { on: boolean }) => <div className={`hd-tone-${on}`}>Stale</div>\n'],
  ])('refuses a className with %s', async (_, code) => {
    expect(await lintView(code)).toEqual([refusal])
  })

  it('passes a view that composes SDK components without a className', async () => {
    expect(await lintView(`export const Probe = () => <Banner tone="warning">Stale</Banner>\n`)).toEqual([])
  })
})
