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
  message: expect.stringMatching(/names no SDK class/),
}

describe('module lint rules', () => {
  it.each([
    ['a class string', `export const Probe = () => <div className="hd-banner">Stale</div>\n`],
    ['a class expression', `export const Probe = () => <div className={'hd-hint'}>Stale</div>\n`],
    ['a class template', 'export const Probe = ({ on }: { on: boolean }) => <div className={`hd-tone-${on}`}>Stale</div>\n'],
  ])('refuses an SDK class name in %s', async (_, code) => {
    expect(await lintView(code)).toEqual([refusal])
  })

  it('passes a view that names no SDK class', async () => {
    expect(await lintView(`export const Probe = () => <div className="probe">Stale</div>\n`)).toEqual([])
  })
})
