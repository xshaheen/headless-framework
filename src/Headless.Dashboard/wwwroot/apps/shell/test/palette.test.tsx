import { CommandAction, useAllowedActions, type CommandDefinition } from '@headless/dashboard-sdk/commands'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { chunkUrl, moduleFiles, moduleManifest, shellState } from './fixtures/modules.tsx'
import { renderShell } from './harness.tsx'

function serverCommand(id: string, label: string): CommandDefinition {
  return {
    id,
    label,
    objectKind: 'run',
    permission: 'operate',
    confirmation: 'none',
    typedConfirmationAbove: 100,
    requiresReason: false,
    supportsFilter: true,
    arguments: { type: 'object', required: [], properties: {} },
    canRun: true,
  }
}

const requeue = serverCommand('diag.requeue', 'Requeue')
const cancel = serverCommand('diag.cancel', 'Cancel')

/** A runs view with run r1 selected and its inspector's actions, each disabled with the server's reason when refused. */
function SelectedRunView() {
  const refusal = useAllowedActions('run', [{ id: 'r1', version: '1' }])

  return (
    <>
      <h2>Run r1</h2>
      <CommandAction module="diag" command={requeue} ids={['r1']} noun="run" disabledReason={refusal(requeue.id)} />
      <CommandAction module="diag" command={cancel} ids={['r1']} noun="run" disabledReason={refusal(cancel.id)} />
    </>
  )
}

describe('command palette', () => {
  it('lists the selected run’s allowed commands and leaves out a refused one', async () => {
    const user = userEvent.setup()
    const modules = [moduleManifest('diag', { displayName: 'Diagnostics' })]
    const shell = renderShell(shellState(modules), {
      path: '/diag/runs',
      files: { ...moduleFiles(modules), [chunkUrl('diag', 'runs')]: { default: SelectedRunView } },
      api: (request) => {
        if (request.path === 'diag/commands/allowed') {
          return {
            objects: [
              {
                kind: 'run',
                id: 'r1',
                commands: [
                  { id: requeue.id, allowed: true },
                  { id: cancel.id, allowed: false, disabledReason: 'Not running' },
                ],
              },
            ],
          }
        }

        if (request.path === 'diag/commands/diag.requeue') {
          return { rows: [{ id: 'r1', outcome: 'done' }] }
        }

        return { modules: [] }
      },
    })

    await screen.findByText('Not running')
    await user.keyboard('{Meta>}k{/Meta}')

    const palette = await screen.findByRole('dialog', { name: 'Command palette' })
    expect(within(palette).queryByRole('menuitem', { name: /Cancel/ })).toBeNull()

    await user.click(within(palette).getByRole('menuitem', { name: /Requeue/ }))

    await waitFor(() => expect(shell.requests.map((request) => request.path)).toContain('diag/commands/diag.requeue'))
  })

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

  it('shows a found id bidi-isolated and left-to-right beside a right-to-left title', async () => {
    const user = userEvent.setup()
    const modules = [moduleManifest('diag', { displayName: 'Diagnostics' })]
    renderShell(shellState(modules), {
      files: moduleFiles(modules),
      api: (request) =>
        request.path === 'resolve'
          ? {
              modules: [
                {
                  id: 'diag',
                  displayName: 'Diagnostics',
                  stale: false,
                  items: [{ target: { module: 'diag', kind: 'run', id: 'r-8f31c2' }, title: 'فاتورة الشهر' }],
                },
              ],
            }
          : { modules: [] },
    })

    await screen.findByRole('heading', { name: 'Overview' })
    await user.keyboard('{Meta>}k{/Meta}')
    await user.paste('r-8f31c2')

    const found = await screen.findByRole('menuitem', { name: /فاتورة الشهر r-8f31c2/ })
    const id = within(found).getByText('r-8f31c2')

    expect(id.tagName).toBe('BDI')
    expect(id.getAttribute('dir')).toBe('ltr')
  })
})
