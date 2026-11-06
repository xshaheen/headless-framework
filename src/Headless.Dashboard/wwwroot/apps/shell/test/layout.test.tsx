import { Button, PrimaryAction } from '@headless/dashboard-sdk/components'
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

  it('shows every time in the zone chosen in the account menu, in the document bar too, and keeps it after a reload', async () => {
    localStorage.clear()
    const user = userEvent.setup()
    const sections = [
      {
        id: 'diag',
        displayName: 'Diagnostics',
        stale: false,
        items: [{ id: 'f', groupKey: 'f', title: 'Failed runs', severity: 'critical', count: 3, capped: false, since: '2026-11-05T15:40:00Z' }],
      },
    ]
    const api = (request: { path: string }) => ({ modules: request.path === 'needs-you' ? sections : [] })
    const since = async () => (await screen.findByText(/^since /)).getAttribute('title')

    const first = renderShell(shellState([]), { api })

    expect(await since()).toBe('Nov 5, 2026, 15:40:00 UTC')
    expect(screen.getByTitle('Display time zone').textContent).toBe('UTC')

    await user.selectOptions(screen.getByLabelText('Time zone'), 'Africa/Cairo')

    expect(await since()).toBe('Nov 5, 2026, 17:40:00 GMT+02:00 Africa/Cairo')
    expect(screen.getByTitle('Display time zone').textContent).toBe('Africa/Cairo')

    first.unmount()
    renderShell(shellState([]), { api })

    expect(await since()).toBe('Nov 5, 2026, 17:40:00 GMT+02:00 Africa/Cairo')
    expect(screen.getByTitle('Display time zone').textContent).toBe('Africa/Cairo')
    localStorage.clear()
  })

  it('shows a view’s primary action in the document bar, and not on another view', async () => {
    const modules = [moduleManifest('diag', { displayName: 'Diagnostics' })]
    const files = {
      ...moduleFiles(modules),
      [chunkUrl('diag', 'runs')]: {
        default: function RunsView() {
          return (
            <>
              <h2>Runs view</h2>
              <PrimaryAction>
                <Button variant="primary">Add run</Button>
              </PrimaryAction>
            </>
          )
        },
      },
    }
    const shell = renderShell(shellState(modules), { path: '/diag/runs', files })

    await screen.findByRole('heading', { name: 'Runs view' })
    expect(within(screen.getByRole('banner')).getByRole('button', { name: 'Add run' })).toBeTruthy()
    expect(within(screen.getByRole('main')).queryByRole('button', { name: 'Add run' })).toBeNull()

    await shell.router.navigate('/diag/settings')

    await screen.findByRole('heading', { name: 'Diagnostics settings view' })
    expect(screen.queryByRole('button', { name: 'Add run' })).toBeNull()
  })

  it('names an object page in the breadcrumb under its list: module, list, then the object', async () => {
    const modules = [
      moduleManifest('jobs', {
        displayName: 'Jobs',
        routes: [{ path: 'functions/:name', chunk: chunkUrl('jobs', 'function') }],
      }),
    ]
    const sidebar = { jobs: [{ id: 'jobs.nav.functions', label: 'Functions', route: 'functions', icon: 'function' }] }
    renderShell(shellState(modules, {}, sidebar), { path: '/jobs/functions/billing.charge', files: moduleFiles(modules) })

    await screen.findByRole('heading', { name: 'Jobs functions/:name view' })
    const crumb = screen.getByRole('navigation', { name: 'Breadcrumb' })
    expect(crumb.textContent).toBe('Jobs›Functions›billing.charge')
    expect(within(crumb).getByRole('heading', { level: 1 }).textContent).toBe('billing.charge')
  })
})
