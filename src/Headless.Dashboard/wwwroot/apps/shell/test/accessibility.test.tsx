import { usePageTitle } from '@headless/dashboard-sdk/components'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { useParams } from '@headless/dashboard-sdk/router'
import { useEffect, useState } from 'react'
import { axeViolations } from '@headless/dashboard-sdk/testing/axe'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AuthMode } from '../src/bootstrap.ts'
import { SignInPage } from '../src/views/sign-in.tsx'
import { chunkUrl, moduleFiles, moduleManifest, shellState } from './fixtures/modules.tsx'
import { emptyAggregates, renderShell } from './harness.tsx'

/** Answers matchMedia as a window `width` pixels wide would. */
function atWidth(width: number) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: width <= Number(/max-width:\s*(\d+)px/.exec(query)?.[1] ?? Infinity),
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }))
}

function renderJobs(path = '/jobs/runs') {
  const modules = [moduleManifest('jobs')]

  return renderShell(shellState(modules), {
    path,
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
}

/** Jobs with a run page that names itself once its run loads, as the Jobs module's run page does. */
function renderRunPage(path: string, release: Promise<void>) {
  const jobs = moduleManifest('jobs', {
    routes: [
      { path: 'runs', chunk: chunkUrl('jobs', 'runs') },
      { path: 'runs/:runId', chunk: chunkUrl('jobs', 'run') },
      { path: 'settings', chunk: chunkUrl('jobs', 'settings') },
    ],
  })
  const files = moduleFiles([jobs])
  files[chunkUrl('jobs', 'run')] = {
    default: function RunPage() {
      const runId = useParams().runId ?? ''
      const [name, setName] = useState<string>()

      useEffect(() => {
        void release.then(() => setName(`billing.charge ${runId}`))
      }, [runId])
      usePageTitle(name ? `${name} run` : undefined)

      return <h2>{name ?? 'Loading the run'}</h2>
    },
  }

  return renderShell(shellState([jobs]), { path, files })
}

function announced(text: string) {
  return screen.getAllByRole('status').some((region) => region.textContent === text)
}

beforeEach(() => {
  document.title = 'Dashboard'
})

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('shell accessibility', () => {
  it.each([1440, 400])('the frame at %i px breaks no WCAG A or AA rule axe checks', async (width) => {
    atWidth(width)
    renderJobs()

    await screen.findByRole('heading', { name: 'Module jobs runs view' })
    expect(await axeViolations()).toEqual([])
  })

  it('the phone menu, open over the view, breaks no rule', async () => {
    atWidth(400)
    const user = userEvent.setup()
    renderJobs()

    await user.click(await screen.findByRole('button', { name: 'Menu' }))
    expect(await axeViolations()).toEqual([])
  })

  it.each<AuthMode>(['basic', 'apiKey', 'host'])('the %s sign-in page breaks no rule', async (mode) => {
    const fetcher = vi.fn<typeof fetch>(async () => new Response('{}', { status: 401 }))
    render(
      <SignInPage
        state={shellState([], { authMode: mode, session: { authenticated: false } })}
        search=""
        fetch={fetcher}
        assign={() => undefined}
      />,
    )

    await screen.findByRole('heading', { level: 1, name: 'Sign in' })
    expect(await axeViolations()).toEqual([])
  })

  it('heads a page nothing lives at with its own h1', async () => {
    renderShell(shellState([moduleManifest('jobs')]), { path: '/nowhere' })

    expect(await screen.findByRole('heading', { level: 1, name: 'Nothing lives at this address' })).toBeTruthy()
  })

  it('keeps go to id in the tab order below 1024 px, folded to its icon only visually', async () => {
    atWidth(400)
    const user = userEvent.setup()
    renderJobs()
    const field = await screen.findByRole('textbox', { name: 'Go to id' })

    // jsdom applies no stylesheet, so the class that folds the field is the only evidence of how it folds: the input
    // must stay rendered (visually hidden), never `display: none`, which would take it out of the tab order.
    const folding = field.closest('label')!.className
    expect(folding).toContain('max-lg:not-focus-within:[&_input]:sr-only')
    expect(folding).not.toMatch(/input[^ ]*\]:hidden/)

    // From Menu, past the product tab, Tab reaches the field.
    screen.getByRole('button', { name: 'Menu' }).focus()
    await user.tab()
    await user.tab()
    expect(document.activeElement).toBe(field)
  })

  it('says what a pasted id found and what Enter does, since the matches are not where focus is', async () => {
    const user = userEvent.setup()
    renderJobs()

    await user.click(await screen.findByRole('textbox', { name: 'Go to id' }))
    await user.paste('job_7f2b')

    await waitFor(() => expect(announced('1 match: billing.charge. Press Enter to open it.')).toBe(true))
  })

  it('says a failed lookup failed', async () => {
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

    await waitFor(() => expect(announced('Lookup failed.')).toBe(true))
  })

  it('titles each view by its sidebar entry and product, and says a new view once', async () => {
    const user = userEvent.setup()
    renderJobs()

    await screen.findByRole('heading', { name: 'Module jobs runs view' })
    expect(document.title).toBe('Runs · Jobs · Dashboard')
    expect(announced('Runs · Jobs · Dashboard')).toBe(false)

    const settings = within(screen.getByRole('navigation', { name: 'Jobs' })).getByRole('link', { name: 'Settings' })
    await user.click(settings)

    await screen.findByRole('heading', { name: 'Module jobs settings view' })
    expect(document.title).toBe('Settings · Jobs · Dashboard')
    expect(announced('Settings · Jobs · Dashboard')).toBe(true)
    // The link that went there is still on the page, so focus stays on it.
    expect(document.activeElement).toBe(settings)
  })

  it('moves focus into the phone menu, and Escape closes it back to Menu', async () => {
    atWidth(400)
    const user = userEvent.setup()
    renderJobs()
    const menu = await screen.findByRole('button', { name: 'Menu' })

    await user.click(menu)

    const sidebar = screen.getByRole('navigation', { name: 'Jobs' })
    expect(menu.getAttribute('aria-controls')).toBe(sidebar.id)
    expect(sidebar.contains(document.activeElement)).toBe(true)

    await user.keyboard('{Escape}')

    expect(screen.queryByRole('navigation', { name: 'Jobs' })).toBeNull()
    expect(document.activeElement).toBe(menu)
  })

  it('lands focus on the new view when the control that went there leaves with the menu', async () => {
    atWidth(400)
    const user = userEvent.setup()
    renderJobs()

    await user.click(await screen.findByRole('button', { name: 'Menu' }))
    await user.click(within(screen.getByRole('navigation', { name: 'Jobs' })).getByRole('link', { name: 'Settings' }))

    await screen.findByRole('heading', { name: 'Module jobs settings view' })
    expect(document.activeElement).toBe(screen.getByRole('main'))
  })

  it('draws a focus ring on the palette search field, which axe cannot see', async () => {
    const user = userEvent.setup()
    renderJobs()
    await screen.findByRole('heading', { name: 'Module jobs runs view' })

    await user.keyboard('{Control>}k{/Control}')

    const field = await screen.findByRole('searchbox', { name: 'Search commands, views, and objects' })
    expect(document.activeElement).toBe(field)
    // jsdom applies no stylesheet, so the field's focused state and the classes it turns on are the evidence: an
    // accent outline inside the field, where the dialog's clipping edge cannot hide it.
    expect(field.hasAttribute('data-focused')).toBe(true)
    expect(field.className).toContain('data-focused:outline-solid')
    expect(field.className).toContain('data-focused:outline-2')
    expect(field.className).toContain('data-focused:outline-ring')
    expect(field.className).not.toMatch(/data-focused:outline-(none|hidden)/)
    expect(await axeViolations()).toEqual([])
  })

  it("titles a run's page by its run, and says the new page once, when the name arrives", async () => {
    let load = () => undefined as void
    const loaded = new Promise<void>((resolve) => {
      load = resolve
    })
    const { router } = renderRunPage('/jobs/runs', loaded)
    await screen.findByRole('heading', { name: 'Module jobs runs view' })
    // A route that names no page of its own keeps the sidebar entry's name.
    expect(document.title).toBe('Runs · Jobs · Dashboard')

    await act(() => router.navigate('/jobs/runs/run_7'))
    await screen.findByRole('heading', { name: 'Loading the run' })

    // Nothing is said while the run's name loads: the list's title would name the wrong page.
    expect(announced('Runs · Jobs · Dashboard')).toBe(false)

    load()

    await screen.findByRole('heading', { name: 'billing.charge run_7' })
    await waitFor(() => expect(document.title).toBe('billing.charge run_7 run · Jobs · Dashboard'))
    expect(announced('billing.charge run_7 run · Jobs · Dashboard')).toBe(true)

    // Back on the list, the run page's name goes with it and the list's is said.
    await act(() => router.navigate('/jobs/runs'))
    await screen.findByRole('heading', { name: 'Module jobs runs view' })
    expect(document.title).toBe('Runs · Jobs · Dashboard')
    expect(announced('Runs · Jobs · Dashboard')).toBe(true)
  })
})
