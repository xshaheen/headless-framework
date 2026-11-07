import { Button } from '@headless/dashboard-sdk/components'
import { useOperations, type CommandDefinition } from '@headless/dashboard-sdk/commands'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { chunkUrl, moduleFiles, moduleManifest, shellState } from './fixtures/modules.tsx'
import { emptyAggregates, renderShell } from './harness.tsx'

const requeue: CommandDefinition = {
  id: 'requeue',
  label: 'Requeue',
  objectKind: 'run',
  permission: 'operate',
  confirmation: 'confirm',
  typedConfirmationAbove: 100,
  requiresReason: true,
  supportsFilter: true,
  arguments: { type: 'object', required: [], properties: {} },
  canRun: true,
}

function StartOperation() {
  const { start } = useOperations()

  return (
    <Button onPress={() => start({ module: 'diag', command: requeue, filter: { status: 'failed' }, noun: 'failed runs' })}>
      Requeue all failed runs
    </Button>
  )
}

describe('shell layout', () => {
  it('keeps the Operation running across navigation, with Stop in view and the leave warning armed', async () => {
    const user = userEvent.setup()
    const modules = [moduleManifest('diag', { displayName: 'Diagnostics' })]
    const files = { ...moduleFiles(modules), [chunkUrl('diag', 'runs')]: { default: StartOperation } }
    let release: () => void = () => undefined
    let chunks = 0

    const shell = renderShell(shellState(modules), {
      path: '/diag/runs',
      files,
      api: async (request) => {
        if (request.path.endsWith('/confirm')) {
          return { operationId: 'op1', count: 3, confirmation: 'confirm', sample: [], token: 't', expiresAt: '', chunkSize: 1 }
        }

        if (request.path.endsWith('/chunks')) {
          chunks++

          if (chunks === 2) {
            // Hold the second chunk until the test has navigated away.
            await new Promise<void>((resolve) => {
              release = resolve
            })
          }

          return { operationId: 'op1', rows: [{ id: `r${chunks}`, outcome: 'done' }], nextCursor: chunks < 3 ? `c${chunks}` : null, done: chunks >= 3 }
        }

        return emptyAggregates(request)
      },
    })

    await user.click(await screen.findByRole('button', { name: 'Requeue all failed runs' }))
    await user.type(screen.getByRole('textbox', { name: /Reason/ }), 'card processor recovered')
    await user.click(screen.getByRole('button', { name: 'Count rows' }))
    await user.click(await screen.findByRole('button', { name: 'Requeue 3 failed runs' }))

    const panel = await screen.findByRole('group', { name: 'Requeue failed runs' })
    await waitFor(() => expect(chunks).toBe(2))

    const unload = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(unload)
    expect(unload.defaultPrevented).toBe(true)

    await user.click(within(screen.getByRole('navigation', { name: 'Dashboard' })).getByRole('link', { name: 'Overview' }))
    expect(await screen.findByRole('heading', { name: 'Overview', level: 1 })).toBeTruthy()
    expect(within(panel).getByRole('button', { name: 'Stop' })).toBeTruthy()

    release()
    await waitFor(() => expect(chunks).toBe(3))
    expect(await within(screen.getByRole('group', { name: /Requeue failed runs/ })).findByText(/Completed/)).toBeTruthy()
    expect(shell.router.state.location.pathname).toBe('/')
  })

  it('shows Sign-out failed beside Sign out, and the operator stays signed in', async () => {
    const user = userEvent.setup()
    renderShell(shellState([]), {
      signOut: async () => {
        throw new Error('503')
      },
    })

    await user.click(screen.getByRole('button', { name: 'Sign out' }))

    expect((await screen.findByRole('alert')).textContent).toContain('Sign-out failed')
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeTruthy()
    expect(screen.getByText('Mona Haddad')).toBeTruthy()
  })

  it('shows a viewer operate actions disabled with Needs operate permission, and View only', async () => {
    const modules = [moduleManifest('diag', { displayName: 'Diagnostics', canOperate: false })]
    renderShell(shellState(modules), { files: moduleFiles(modules), path: '/diag/runs' })

    const group = await screen.findByRole('group', { name: 'Diagnostics' })
    const settings = within(group).getByRole('link', { name: /Settings/ })
    expect(settings.getAttribute('aria-disabled')).toBe('true')
    expect(settings.getAttribute('href')).toBeNull()
    expect(within(group).getByText('Needs operate permission')).toBeTruthy()
    expect(await screen.findByText('View only')).toBeTruthy()
  })
})
