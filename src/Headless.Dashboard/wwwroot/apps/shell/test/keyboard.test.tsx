import { DashboardProvider, createDashboardClient } from '@headless/dashboard-sdk'
import { useShortcut } from '@headless/dashboard-sdk/components'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { createFakeStream } from '@headless/dashboard-sdk/testing'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { chunkUrl, moduleFiles, moduleManifest, shellState } from './fixtures/modules.tsx'
import { emptyAggregates, renderShell } from './harness.tsx'

/** The keys of the shortcut table in the dashboard's design system, as an operator reads them on the key caps. */
const designKeys = ['⌘K', '/', 'J', 'K', 'Enter', 'X', 'Esc', 'R', 'T', 'G O', 'G N', 'G R', 'G M', '[', ']', '?']

describe('shell keyboard', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('opens the shortcut list on ?, listing every key of the design system', async () => {
    const user = userEvent.setup()
    const modules = [moduleManifest('jobs'), moduleManifest('messaging')]
    renderShell(shellState(modules), { files: moduleFiles(modules) })

    await screen.findByRole('heading', { name: 'Module jobs runs view' })
    await user.keyboard('?')

    const list = await screen.findByRole('dialog', { name: 'Keyboard shortcuts' })
    const caps = [...list.querySelectorAll('kbd')].map((cap) => cap.textContent)

    for (const key of designKeys) {
      expect(caps).toContain(key)
    }
  })

  it('lists a G chord only when the module it opens is registered', async () => {
    const user = userEvent.setup()
    const modules = [moduleManifest('jobs')]
    renderShell(shellState(modules), { files: moduleFiles(modules) })

    await screen.findByRole('heading', { name: 'Module jobs runs view' })
    await user.keyboard('?')

    const list = await screen.findByRole('dialog', { name: 'Keyboard shortcuts' })
    const caps = [...list.querySelectorAll('kbd')].map((cap) => cap.textContent)
    expect(caps).toContain('G R')
    expect(caps).toContain('G N')
    expect(caps).not.toContain('G M')
  })

  it('opens Attention on G N, and Escape closes it and returns focus to the pill', async () => {
    const user = userEvent.setup()
    const modules = [moduleManifest('jobs')]
    renderShell(shellState(modules), { files: moduleFiles(modules) })

    await screen.findByRole('heading', { name: 'Module jobs runs view' })
    await user.keyboard('gn')

    const popover = await screen.findByRole('dialog', { name: 'Attention' })
    expect(await within(popover).findByText('Nothing needs you.')).toBeTruthy()

    await user.keyboard('{Escape}')

    expect(screen.queryByRole('dialog', { name: 'Attention' })).toBeNull()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: /^Attention/ }))
  })

  it('goes to the current product’s home with G O, and to the Jobs runs list with G R', async () => {
    const user = userEvent.setup()
    const modules = [moduleManifest('jobs')]
    const shell = renderShell(shellState(modules), { path: '/jobs/settings', files: moduleFiles(modules) })

    await screen.findByRole('heading', { name: 'Module jobs settings view' })
    await user.keyboard('go')
    await waitFor(() => expect(shell.router.state.location.pathname).toBe('/jobs/runs'))

    await user.keyboard('gr')
    await waitFor(() => expect(shell.router.state.location.pathname).toBe('/jobs'))
  })

  it('goes with G then a letter while a select has focus, since a select is not a text field', async () => {
    const user = userEvent.setup()
    const modules = [moduleManifest('jobs')]
    const files = {
      ...moduleFiles(modules),
      [chunkUrl('jobs', 'settings')]: {
        default: () => (
          <select aria-label="Status">
            <option>Any</option>
          </select>
        ),
      },
    }
    const shell = renderShell(shellState(modules), { path: '/jobs/settings', files })

    const status = await screen.findByRole('combobox', { name: 'Status' })
    act(() => status.focus())
    await user.keyboard('go')

    await waitFor(() => expect(shell.router.state.location.pathname).toBe('/jobs/runs'))
  })

  it('focuses go to id on /, lists the run a pasted id names, and opens it on Enter', async () => {
    const user = userEvent.setup()
    const modules = [moduleManifest('jobs', { displayName: 'Jobs' })]
    const shell = renderShell(shellState(modules), {
      path: '/jobs/settings',
      files: moduleFiles(modules),
      api: (request) =>
        request.path === 'resolve'
          ? {
              modules: [
                {
                  id: 'jobs',
                  displayName: 'Jobs',
                  stale: false,
                  items: [{ target: { module: 'jobs', kind: 'run', id: 'job_7f2b' }, title: 'billing.charge' }],
                },
              ],
            }
          : emptyAggregates(request),
    })

    await screen.findByRole('heading', { name: 'Jobs settings view' })
    await user.keyboard('/')

    const field = screen.getByRole('textbox', { name: 'Go to id' })
    expect(document.activeElement).toBe(field)

    await user.paste('job_7f2b')
    expect(await within(await screen.findByRole('region', { name: 'Matches' })).findByRole('button', { name: 'billing.charge' })).toBeTruthy()

    await user.keyboard('{Enter}')

    await waitFor(() => expect(`${shell.router.state.location.pathname}${shell.router.state.location.search}`).toBe('/jobs/runs?selected=job_7f2b'))
  })

  it('says no object has a pasted id that matches nothing', async () => {
    const user = userEvent.setup()
    renderShell(shellState([]))

    await user.keyboard('/')
    await user.paste('nope-123')

    expect(await screen.findByText('No object with id nope-123')).toBeTruthy()
  })

  it('says a lookup failed, rather than that nothing has the id, when the request fails', async () => {
    const user = userEvent.setup()
    renderShell(shellState([]), {
      api: (request) => {
        if (request.path === 'resolve') {
          throw new DashboardApiError(503, 'Unavailable')
        }

        return emptyAggregates(request)
      },
    })

    await user.keyboard('/')
    await user.paste('job_7f2b')

    expect(await screen.findByText('Lookup failed. Try again.')).toBeTruthy()
    expect(screen.queryByText(/No object with id/)).toBeNull()
  })

  it('names the modules that did not answer when no answering module has the id', async () => {
    const user = userEvent.setup()
    renderShell(shellState([]), {
      api: (request) =>
        request.path === 'resolve' ? { modules: [{ id: 'jobs', displayName: 'Jobs', stale: true, items: [] }] } : emptyAggregates(request),
    })

    await user.keyboard('/')
    await user.paste('job_7f2b')

    expect(await screen.findByText('No match in the modules that answered (Jobs did not answer)')).toBeTruthy()
  })

  it('closes the palette opened over Attention on Escape, and leaves Attention open', async () => {
    const user = userEvent.setup()
    const modules = [moduleManifest('jobs')]
    renderShell(shellState(modules), { files: moduleFiles(modules) })

    await screen.findByRole('heading', { name: 'Module jobs runs view' })
    await user.keyboard('gn')
    await screen.findByRole('dialog', { name: 'Attention' })

    await user.keyboard('{Control>}k{/Control}')
    await screen.findByRole('dialog', { name: 'Command palette' })
    await user.keyboard('{Escape}')

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Command palette' })).toBeNull())
    expect(screen.getByRole('dialog', { name: 'Attention' })).toBeTruthy()
  })

  it('opens the palette on ⌘K even while a text field has focus, and ignores other keys there', async () => {
    const user = userEvent.setup()
    const shell = renderShell(shellState([]))

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
