import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { chunkUrl, entryUrl, moduleFiles, moduleManifest, shellState, twentyModules } from './fixtures/modules.tsx'
import { renderShell } from './harness.tsx'

describe('router', () => {
  it('builds every route of a 20-module dashboard without importing any module code, and a visit imports only its own', async () => {
    const modules = twentyModules()
    const state = shellState(modules)
    const user = userEvent.setup()

    const shell = renderShell(state, { files: moduleFiles(modules) })

    expect(await screen.findByRole('heading', { name: 'Overview' })).toBeTruthy()
    expect(within(screen.getByRole('navigation', { name: 'Dashboard' })).getAllByRole('link', { name: 'Runs' })).toHaveLength(20)
    expect(shell.imported).toEqual([])

    await user.click(within(screen.getByRole('group', { name: 'Module mod-07' })).getByRole('link', { name: 'Runs' }))

    expect(await screen.findByRole('heading', { name: 'Module mod-07 runs view' })).toBeTruthy()
    expect([...shell.imported].sort()).toEqual([chunkUrl('mod-07', 'runs'), entryUrl('mod-07')].sort())
  })

  it('shows a refused module once in its rail group with both versions, and the other modules still work', async () => {
    const good = moduleManifest('good')
    const refused = moduleManifest('old', {
      entry: undefined,
      routes: [],
      commands: [],
      refusal: { builtAgainst: '2.1.0', shellSdkVersion: '1.0.0', message: 'refused' },
    })
    const user = userEvent.setup()
    const shell = renderShell(shellState([refused, good]), { files: moduleFiles([good]) })

    const group = screen.getByRole('group', { name: 'Module old' })
    const error = within(group).getByRole('alert')
    expect(error.textContent).toContain('SDK 2.1.0')
    expect(error.textContent).toContain('SDK 1.0.0')

    await user.click(within(screen.getByRole('group', { name: 'Module good' })).getByRole('link', { name: 'Runs' }))
    expect(await screen.findByRole('heading', { name: 'Module good runs view' })).toBeTruthy()

    await shell.router.navigate('/old/anything')
    expect(await screen.findByRole('heading', { name: 'Module old is not available' })).toBeTruthy()
    expect(shell.imported.some((url) => url.includes('/m/old/'))).toBe(false)
  })

  it('shows Dashboard updated with Reload when a module file is gone after a deploy', async () => {
    const module = moduleManifest('gone')
    const shell = renderShell(shellState([module]), { files: {} })
    await screen.findByRole('heading', { name: 'Overview' })

    await shell.router.navigate('/gone/runs')

    const notices = await screen.findAllByText(/Dashboard updated/)
    expect(notices.length).toBeGreaterThan(0)
    expect(screen.getAllByRole('button', { name: 'Reload' }).length).toBeGreaterThan(0)
  })
})
