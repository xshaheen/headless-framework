import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { moduleFiles, moduleManifest, shellState } from './fixtures/modules.tsx'
import { renderShell } from './harness.tsx'

describe('command palette', () => {
  it('finds a nav entry by its label and goes there', async () => {
    const user = userEvent.setup()
    const modules = [moduleManifest('diag', { displayName: 'Diagnostics' })]
    renderShell(shellState(modules), { files: moduleFiles(modules) })

    await screen.findByRole('heading', { name: 'Overview' })
    await user.keyboard('{Meta>}k{/Meta}')
    await user.keyboard('runs')

    const palette = await screen.findByRole('dialog', { name: 'Command palette' })
    await user.click(within(palette).getByRole('menuitem', { name: /Go to Diagnostics Runs/ }))

    expect(await screen.findByRole('heading', { name: 'Diagnostics runs view' })).toBeTruthy()
  })

  it('lists a destination once when a module command repeats a nav entry', async () => {
    const user = userEvent.setup()
    const modules = [
      moduleManifest('diag', {
        displayName: 'Diagnostics',
        commands: [
          { id: 'diag.go-runs', label: 'Go to Diagnostics runs' },
          { id: 'diag.add', label: 'Add diag run', requires: 'operate' },
        ],
      }),
    ]
    renderShell(shellState(modules), { files: moduleFiles(modules) })

    await screen.findByRole('heading', { name: 'Overview' })
    await user.keyboard('{Meta>}k{/Meta}')
    await user.keyboard('diag')

    const palette = await screen.findByRole('dialog', { name: 'Command palette' })
    expect(within(palette).getAllByRole('menuitem', { name: /Go to Diagnostics runs/i }).map((item) => item.textContent)).toEqual([
      expect.stringContaining('Go to Diagnostics Runs'),
    ])
    expect(within(palette).getByRole('menuitem', { name: /Add diag run/ })).toBeTruthy()
  })

  it('finds an object by a pasted id in any module and opens it in its module', async () => {
    const user = userEvent.setup()
    const modules = [moduleManifest('diag', { displayName: 'Diagnostics' })]
    const shell = renderShell(shellState(modules), {
      files: moduleFiles(modules),
      api: (request) => {
        if (request.path === 'resolve') {
          return {
            modules: [
              {
                id: 'diag',
                displayName: 'Diagnostics',
                stale: false,
                items: request.query?.q === 'r-8f31c2' ? [{ target: { module: 'diag', kind: 'run', id: 'r-8f31c2' }, title: 'billing.charge' }] : [],
              },
            ],
          }
        }

        return { modules: [] }
      },
    })

    await screen.findByRole('heading', { name: 'Overview' })
    await user.click(screen.getByRole('button', { name: 'Search' }))
    await user.click(await screen.findByRole('searchbox'))
    await user.paste('r-8f31c2')

    const found = await screen.findByRole('menuitem', { name: /billing\.charge r-8f31c2/ })
    expect(shell.imported).toEqual([])

    await user.click(found)

    expect(await screen.findByRole('heading', { name: 'Diagnostics runs view' })).toBeTruthy()
    expect(shell.router.state.location.search).toBe('?selected=r-8f31c2')
  })
})
