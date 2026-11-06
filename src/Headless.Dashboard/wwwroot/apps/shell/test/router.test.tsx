import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { chunkUrl, entryUrl, moduleFiles, moduleManifest, shellState, twentyModules } from './fixtures/modules.tsx'
import { renderShell } from './harness.tsx'

describe('router', () => {
  it('builds every route of a 20-module dashboard without importing module code it does not show', async () => {
    const modules = twentyModules()
    const user = userEvent.setup()

    const shell = renderShell(shellState(modules), { path: '/missing', files: moduleFiles(modules) })

    await screen.findByRole('heading', { name: 'Nothing lives at this address' })
    expect(shell.imported).toEqual([])

    await shell.router.navigate('/mod-01/settings')
    const sidebar = await screen.findByRole('navigation', { name: 'Jobs' })
    expect(within(sidebar).getAllByRole('link', { name: 'Runs' })).toHaveLength(20)

    await user.click(within(sidebar).getAllByRole('link', { name: 'Runs' })[6]!)

    expect(await screen.findByRole('heading', { name: 'Module mod-07 runs view' })).toBeTruthy()
    expect(shell.imported).toContain(chunkUrl('mod-07', 'runs'))
    expect(shell.imported).toContain(entryUrl('mod-07'))
    expect(shell.imported.some((url) => url.includes('/m/mod-08/'))).toBe(false)
  })

  it('opens the first product’s home at /', async () => {
    const modules = [moduleManifest('jobs')]
    const shell = renderShell(shellState(modules), { files: moduleFiles(modules) })

    expect(await screen.findByRole('heading', { name: 'Module jobs runs view' })).toBeTruthy()
    expect(shell.router.state.location.pathname).toBe('/jobs/runs')
    expect(screen.getByRole('link', { name: /^Jobs/ }).getAttribute('aria-current')).toBe('page')
  })

  it('shows why a refused module did not load, marks its product off, and the other modules still work', async () => {
    const good = moduleManifest('good')
    const refused = moduleManifest('old', {
      entry: undefined,
      routes: [],
      commands: [],
      refusal: { builtAgainst: '2.1.0', shellSdkVersion: '1.0.0', message: 'refused' },
    })
    const state = shellState([refused, good])
    state.products[0]!.modules = ['old', 'good']
    const shell = renderShell(state, { path: '/old/anything', files: moduleFiles([good]) })

    const error = await screen.findByRole('alert')
    expect(error.textContent).toContain('SDK 2.1.0')
    expect(error.textContent).toContain('SDK 1.0.0')
    expect(screen.getByRole('link', { name: 'Jobs did not load' })).toBeTruthy()
    expect(shell.imported.some((url) => url.includes('/m/old/'))).toBe(false)

    await shell.router.navigate('/good/runs')
    expect(await screen.findByRole('heading', { name: 'Module good runs view' })).toBeTruthy()
  })

  it('shows Dashboard updated with Reload when a module file is gone after a deploy', async () => {
    const module = moduleManifest('gone')
    renderShell(shellState([module]), { path: '/gone/runs', files: {} })

    const notices = await screen.findAllByText(/Dashboard updated/)
    expect(notices.length).toBeGreaterThan(0)
    expect(screen.getAllByRole('button', { name: 'Reload' }).length).toBeGreaterThan(0)
  })
})
