import { act, cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { Dialog, DialogTrigger, Popover, Button as AriaButton } from 'react-aria-components'
import { createMemoryRouter, RouterProvider, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { DetailPanel } from '../../src/components/frame/detail-panel.tsx'
import { PageFrame, usePageFrame } from '../../src/components/frame/page-frame.tsx'
import { KeymapProvider } from '../../src/components/keymap.tsx'

interface Run {
  id: string
  name: string
  status: string
}

const failed: Run[] = [
  { id: 'r1', name: 'billing.charge', status: 'Failed' },
  { id: 'r2', name: 'orders.ship', status: 'Failed' },
  { id: 'r3', name: 'mail.send', status: 'Failed' },
]

function Location() {
  const { pathname, search } = useLocation()

  return <output aria-label="location">{pathname + search}</output>
}

function FailedRuns({ rows }: { rows: readonly Run[] }) {
  const frame = usePageFrame({ viewId: 'jobs.failed', rows, matches: (row) => row.status === 'Failed' })
  const open = rows.find((row) => row.id === frame.openId)

  return (
    <PageFrame
      frame={frame}
      header={<h1>Failed</h1>}
      panel={
        open ? (
          <DetailPanel
            label="Run details"
            name={open.name}
            id={open.id}
            status={{ tone: 'fail', label: open.status }}
            detachTo={`/runs/${open.id}`}
            actions={
              <DialogTrigger>
                <AriaButton>More</AriaButton>
                <Popover>
                  <Dialog aria-label="More actions">
                    <AriaButton>Duplicate</AriaButton>
                  </Dialog>
                </Popover>
              </DialogTrigger>
            }
          >
            <p>{`Showing ${open.name}`}</p>
          </DetailPanel>
        ) : null
      }
    >
      <ul>
        {rows.map((row) => (
          <li key={row.id}>
            <button type="button" data-row-id={row.id} onClick={() => frame.open(row.id)}>
              {row.name}
            </button>
            {frame.checked.has(row.id) ? <span>{`${row.name} checked`}</span> : null}
          </li>
        ))}
      </ul>
    </PageFrame>
  )
}

let setRows: (rows: readonly Run[]) => void = () => undefined

/** Holds the rows as a live list would, so a test can change one in place. */
function LiveRuns({ initial }: { initial: readonly Run[] }) {
  const [rows, set] = useState(initial)
  setRows = set

  return <FailedRuns rows={rows} />
}

function renderFrame(rows: readonly Run[] = failed, entry = '/jobs/failed') {
  const router = createMemoryRouter(
    [
      {
        path: '/jobs/failed',
        element: (
          <KeymapProvider>
            <LiveRuns initial={rows} />
            <Location />
          </KeymapProvider>
        ),
      },
      { path: '/runs/:id', element: <Location /> },
    ],
    { initialEntries: [entry] },
  )
  render(<RouterProvider router={router} />)

  return router
}

const location = () => screen.getByRole('status', { hidden: true, name: 'location' }).textContent

describe('PageFrame', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    localStorage.clear()
  })

  it('records the open row in the URL, closes it on Esc, and Back reopens it', async () => {
    const user = userEvent.setup()
    const router = renderFrame()

    await user.click(screen.getByRole('button', { name: 'billing.charge' }))

    expect(location()).toBe('/jobs/failed?open=r1')
    expect(screen.getByRole('region', { name: 'Run details' })).toBeTruthy()
    // Opening keeps focus in the list.
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'billing.charge' }))

    await user.keyboard('{Escape}')

    expect(location()).toBe('/jobs/failed')
    expect(screen.queryByRole('region', { name: 'Run details' })).toBeNull()

    await act(() => router.navigate(-1))

    expect(location()).toBe('/jobs/failed?open=r1')
    expect(screen.getByText('Showing billing.charge')).toBeTruthy()
  })

  it('moves the open panel with J, replacing the history entry, and checks with X without opening', async () => {
    const user = userEvent.setup()
    const router = renderFrame()

    await user.click(screen.getByRole('button', { name: 'billing.charge' }))
    await user.keyboard('j')

    expect(location()).toBe('/jobs/failed?open=r2')
    expect(screen.getByText('Showing orders.ship')).toBeTruthy()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'orders.ship' }))

    await user.keyboard('x')

    expect(screen.getByText('orders.ship checked')).toBeTruthy()
    expect(location()).toBe('/jobs/failed?open=r2')

    // J replaced the entry, so one Back leaves the list as it was before the first open.
    await act(() => router.navigate(-1))
    expect(location()).toBe('/jobs/failed')
  })

  it('closes a popover, then the panel, then clears the checked rows, one Esc each', async () => {
    const user = userEvent.setup()
    renderFrame()

    await user.click(screen.getByRole('button', { name: 'billing.charge' }))
    await user.keyboard('x')
    expect(screen.getByText('billing.charge checked')).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'More' }))
    expect(screen.getByRole('dialog', { name: 'More actions' })).toBeTruthy()

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'More actions' })).toBeNull()
    expect(screen.getByRole('region', { name: 'Run details' })).toBeTruthy()

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('region', { name: 'Run details' })).toBeNull()
    expect(screen.getByText('billing.charge checked')).toBeTruthy()

    await user.keyboard('{Escape}')
    expect(screen.queryByText('billing.charge checked')).toBeNull()
  })

  it('returns focus to the row that opened the panel when it closes', async () => {
    const user = userEvent.setup()
    renderFrame()

    const row = screen.getByRole('button', { name: 'orders.ship' })
    await user.click(row)
    await user.click(screen.getByRole('button', { name: 'Close details' }))

    expect(screen.queryByRole('region', { name: 'Run details' })).toBeNull()
    expect(document.activeElement).toBe(row)
  })

  it('keeps an open row that leaves the filter open, with a note', async () => {
    const user = userEvent.setup()
    renderFrame()

    await user.click(screen.getByRole('button', { name: 'billing.charge' }))
    expect(screen.queryByText('No longer matches the filter')).toBeNull()

    act(() => setRows(failed.map((row) => (row.id === 'r1' ? { ...row, status: 'Queued' } : row))))

    expect(screen.getByRole('region', { name: 'Run details' })).toBeTruthy()
    expect(screen.getByText('No longer matches the filter')).toBeTruthy()
  })

  it('steps J past a row that left the filter, since the list hides it', async () => {
    const user = userEvent.setup()
    renderFrame()

    await user.click(screen.getByRole('button', { name: 'billing.charge' }))
    act(() => setRows(failed.map((row) => (row.id === 'r2' ? { ...row, status: 'Queued' } : row))))
    await user.keyboard('j')

    expect(location()).toBe('/jobs/failed?open=r3')
  })

  it('detaches to the object’s own page', async () => {
    const user = userEvent.setup()
    renderFrame()

    await user.click(screen.getByRole('button', { name: 'mail.send' }))
    await user.click(screen.getByRole('link', { name: 'Open in its own page' }))

    expect(location()).toBe('/runs/r3')
  })

  it('resizes by arrow keys, jumps to its limits, resets, and remembers the height per view', async () => {
    // jsdom lays nothing out: the list stands 400 px tall and the panel 340, so 740 px are shared and the panel may
    // grow to 580 while the list keeps 160.
    const heights = vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockImplementation(function (this: HTMLElement) {
      return this.classList.contains('hd-frame-list') ? 400 : this.classList.contains('hd-frame-panel') ? 340 : 0
    })
    const user = userEvent.setup()
    renderFrame(failed, '/jobs/failed?open=r1')

    const handle = screen.getByRole('separator', { name: 'Resize details' })
    expect(handle.getAttribute('aria-orientation')).toBe('horizontal')
    expect(handle.getAttribute('aria-valuenow')).toBe('340')
    expect(handle.getAttribute('aria-valuemin')).toBe('200')
    expect(handle.getAttribute('aria-valuemax')).toBe('580')

    handle.focus()
    await user.keyboard('{ArrowUp}')
    expect(handle.getAttribute('aria-valuenow')).toBe('356')
    await user.keyboard('{Shift>}{ArrowDown}{/Shift}')
    expect(handle.getAttribute('aria-valuenow')).toBe('292')
    await user.keyboard('{End}')
    expect(handle.getAttribute('aria-valuenow')).toBe('580')
    await user.keyboard('{Home}')
    expect(handle.getAttribute('aria-valuenow')).toBe('200')
    await user.dblClick(handle)
    expect(handle.getAttribute('aria-valuenow')).toBe('340')
    await user.keyboard('{ArrowDown}')

    cleanup()
    renderFrame(failed, '/jobs/failed?open=r1')

    expect(screen.getByRole('separator', { name: 'Resize details' }).getAttribute('aria-valuenow')).toBe('324')
    heights.mockRestore()
  })

  it('moves the panel to the side at 1280 px and wider, and docks it back', async () => {
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query }))
    const user = userEvent.setup()
    renderFrame(failed, '/jobs/failed?open=r1')

    await user.click(screen.getByRole('button', { name: 'Move details to the side' }))

    expect(screen.queryByRole('separator')).toBeNull()
    expect(document.querySelector('.hd-frame')?.hasAttribute('data-side')).toBe(true)

    await user.click(screen.getByRole('button', { name: 'Move details below' }))
    expect(screen.getByRole('separator', { name: 'Resize details' })).toBeTruthy()
  })

  it('opens as a modal on a phone that keeps focus inside, and Close returns focus to the row', async () => {
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: true, media: query }))
    const user = userEvent.setup()
    renderFrame()

    const row = screen.getByRole('button', { name: 'orders.ship' })
    await user.click(row)

    const dialog = screen.getByRole('dialog', { name: 'Run details' })
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    expect(document.activeElement).toBe(dialog)
    expect(screen.queryByRole('separator')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Move details to the side' })).toBeNull()

    for (let press = 0; press < 8; press++) {
      await user.tab()
      expect(dialog.contains(document.activeElement)).toBe(true)
    }

    await user.tab({ shift: true })
    expect(dialog.contains(document.activeElement)).toBe(true)

    await user.click(screen.getByRole('button', { name: 'Close details' }))

    expect(screen.queryByRole('dialog', { name: 'Run details' })).toBeNull()
    expect(document.activeElement).toBe(row)
  })
})
