import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { axeViolations } from '@headless/dashboard-sdk/testing/axe'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AuthMode } from '../src/bootstrap.ts'
import { SignInPage } from '../src/views/sign-in.tsx'
import { moduleFiles, moduleManifest, shellState } from './fixtures/modules.tsx'
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

  it('opens the phone menu as a modal sheet that holds focus, and Escape closes it back to Menu', async () => {
    atWidth(400)
    const user = userEvent.setup()
    renderJobs()
    const menu = await screen.findByRole('button', { name: 'Menu' })

    await user.click(menu)

    const sheet = screen.getByRole('dialog', { name: 'Jobs menu' })
    const sidebar = within(sheet).getByRole('navigation', { name: 'Jobs' })
    expect(menu.getAttribute('aria-controls')).toBe(sidebar.id)
    expect(sidebar.contains(document.activeElement)).toBe(true)

    // Tab from the last entry wraps to the first: focus never leaves for the view behind the scrim.
    const links = within(sidebar).getAllByRole('link')
    links.at(-1)!.focus()
    await user.tab()
    expect(sheet.contains(document.activeElement)).toBe(true)

    await user.keyboard('{Escape}')

    expect(screen.queryByRole('navigation', { name: 'Jobs' })).toBeNull()
    await waitFor(() => expect(document.activeElement).toBe(menu))
  })

  it('lands focus on the new view when the control that went there leaves with the menu', async () => {
    atWidth(400)
    const user = userEvent.setup()
    renderJobs()

    await user.click(await screen.findByRole('button', { name: 'Menu' }))
    await user.click(within(screen.getByRole('navigation', { name: 'Jobs' })).getByRole('link', { name: 'Settings' }))

    await screen.findByRole('heading', { name: 'Module jobs settings view' })
    expect(document.activeElement).toBe(screen.getByRole('main'))
    // The sheet hands focus back to Menu a frame after it closes, unless focus has moved on, as it has here.
    await new Promise((resolve) => requestAnimationFrame(resolve))
    expect(document.activeElement).toBe(screen.getByRole('main'))
  })
})
