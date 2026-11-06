import { DashboardProvider, createDashboardClient } from '@headless/dashboard-sdk'
import { useShortcut } from '@headless/dashboard-sdk/components'
import { createFakeStream } from '@headless/dashboard-sdk/testing'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { moduleManifest, shellState } from './fixtures/modules.tsx'
import { renderShell } from './harness.tsx'

/** The keys of the shortcut table in the dashboard's design system, as an operator reads them on the key caps. */
const designKeys = ['⌘K', 'Esc', '/', 'J', 'K', 'X', 'R', 'T', 'G N', 'G O', 'G R', 'G M', '[', ']', '?']

describe('shell keyboard', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('opens the shortcut list on ?, listing every key of the design system', async () => {
    const user = userEvent.setup()
    renderShell(shellState([moduleManifest('jobs'), moduleManifest('messaging')]))

    await screen.findByRole('heading', { name: 'Overview' })
    await user.keyboard('?')

    const list = await screen.findByRole('dialog', { name: 'Keyboard shortcuts' })
    const caps = [...list.querySelectorAll('kbd')].map((cap) => cap.textContent)

    for (const key of designKeys) {
      expect(caps).toContain(key)
    }
  })

  it('lists a G chord only when the module it opens is registered', async () => {
    const user = userEvent.setup()
    renderShell(shellState([moduleManifest('jobs')]))

    await screen.findByRole('heading', { name: 'Overview' })
    await user.keyboard('?')

    const list = await screen.findByRole('dialog', { name: 'Keyboard shortcuts' })
    const caps = [...list.querySelectorAll('kbd')].map((cap) => cap.textContent)
    expect(caps).toContain('G R')
    expect(caps).toContain('G N')
    expect(caps).not.toContain('G M')
  })

  it('goes to a view with G then a letter', async () => {
    const user = userEvent.setup()
    const shell = renderShell(shellState([]))

    await screen.findByRole('heading', { name: 'Overview' })
    await user.keyboard('gn')

    // Needs you is a lazy route: the location changes only once its chunk has loaded, which a busy run delays.
    await waitFor(() => expect(shell.router.state.location.pathname).toBe('/needs-you'))

    await user.keyboard('go')

    await waitFor(() => expect(shell.router.state.location.pathname).toBe('/'))
  })

  it('goes to the Jobs runs list with G R when the Jobs module is registered', async () => {
    const user = userEvent.setup()
    const shell = renderShell(shellState([moduleManifest('jobs')]))

    await screen.findByRole('heading', { name: 'Overview' })
    await user.keyboard('gr')

    await waitFor(() => expect(shell.router.state.location.pathname).toBe('/jobs'))
  })

  it('opens the palette on ⌘K even while a text field has focus, and ignores other keys there', async () => {
    const user = userEvent.setup()
    const shell = renderShell(shellState([]))

    await screen.findByRole('heading', { name: 'Overview' })
    await user.keyboard('{Meta>}k{/Meta}')
    const palette = await screen.findByRole('dialog', { name: 'Command palette' })
    const search = within(palette).getByRole('searchbox')

    await user.type(search, 'gn?')

    expect(search).toHaveProperty('value', 'gn?')
    expect(shell.router.state.location.pathname).toBe('/')
    expect(screen.queryByRole('dialog', { name: 'Keyboard shortcuts' })).toBeNull()
  })

  it('refuses a module shortcut that reuses a shell key', () => {
    function ModuleView() {
      useShortcut({ key: 'J', label: 'Jump' }, () => undefined)

      return null
    }

    vi.spyOn(console, 'error').mockImplementation(() => undefined)

    expect(() =>
      render(
        <DashboardProvider
          client={createDashboardClient()}
          environment={{
            basePath: '/headless',
            apiBase: '/headless/api',
            session: { authenticated: true },
            permissions: {},
            stream: createFakeStream(),
            api: { request: vi.fn() },
          }}
        >
          <ModuleView />
        </DashboardProvider>,
      ),
    ).toThrow('The shortcut "J" is a shell key')
  })
})
