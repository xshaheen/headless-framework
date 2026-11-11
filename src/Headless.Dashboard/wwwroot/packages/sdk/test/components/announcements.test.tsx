import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { OperationPanel } from '../../src/commands/operations.tsx'
import type { OperationRun, OperationState } from '../../src/commands/operation-runner.ts'
import { Button } from '../../src/components/button.tsx'
import { CopyButton } from '../../src/components/copy-button.tsx'
import { StaleNotice } from '../../src/components/display/banner.tsx'
import { Text } from '../../src/components/display/text.tsx'
import { DetailPanel } from '../../src/components/frame/detail-panel.tsx'
import { FieldGrid } from '../../src/components/frame/field-grid.tsx'
import { HistoryList } from '../../src/components/frame/history-list.tsx'
import { PageFrame, usePageFrame } from '../../src/components/frame/page-frame.tsx'
import { ToastRegion } from '../../src/components/live.tsx'
import { DashboardProvider, useDashboard } from '../../src/context.tsx'
import { createDashboardClient } from '../../src/data/client.tsx'
import { createFakeStream, type FakeStream } from '../../src/testing/fake-stream.ts'

function withDashboard(children: ReactNode, stream: FakeStream = createFakeStream()) {
  return (
    <DashboardProvider
      client={createDashboardClient()}
      environment={{
        basePath: '/headless',
        apiBase: '/headless/api',
        session: { authenticated: true },
        permissions: {},
        stream,
        api: { request: vi.fn() },
      }}
    >
      {children}
    </DashboardProvider>
  )
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe('announcements', () => {
  it('announces each toast from one region that is always in the page, and groups its actions with it', async () => {
    const user = userEvent.setup()

    function Show() {
      const { toasts } = useDashboard()

      return <Button onPress={() => toasts.show({ message: 'Held 3 generations', action: { label: 'Undo', onAction: () => undefined } })}>Hold</Button>
    }

    render(withDashboard(<><Show /><ToastRegion /></>))
    const region = screen.getByRole('region', { name: 'Notifications' })
    expect(region.getAttribute('aria-live')).toBe('polite')

    await user.click(screen.getByRole('button', { name: 'Hold' }))

    const toast = within(region).getByRole('group', { name: 'Held 3 generations' })
    expect(within(toast).getByRole('button', { name: 'Undo' })).toBeTruthy()
    expect(screen.getByRole('region', { name: 'Notifications' })).toBe(region)
  })

  it('says Copied from the same region on every press', async () => {
    const user = userEvent.setup()
    vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined)
    render(<CopyButton text="r1" aria-label="Copy id r1" />)
    const region = screen.getByRole('status')

    await user.click(screen.getByRole('button', { name: 'Copy id r1' }))
    expect(region.textContent).toBe('Copied')
    const first = region.firstChild

    await user.click(screen.getByRole('button', { name: 'Copy id r1' }))
    expect(screen.getByRole('status')).toBe(region)
    // A new text node, so the second Copied is announced too, not taken for the text already there.
    expect(region.firstChild).not.toBe(first)
  })

  it('announces a stale view once while the stream is live, and leaves a dropped stream to the top bar', () => {
    const stream = createFakeStream()
    const view = (stale: boolean) => withDashboard(<StaleNotice stale={stale} what="these runs" onRefresh={() => undefined} />, stream)
    const { rerender } = render(view(false))
    const region = screen.getByRole('status')
    expect(region.textContent).toBe('')

    rerender(view(true))
    expect(screen.getByRole('status')).toBe(region)
    expect(region.textContent).toBe('Live updates stopped for these runs.')
    // The banner shows the notice; only the region speaks, so it is not said twice.
    expect(screen.getByText('Stale. Live updates stopped, so these runs may be out of date.').closest('[role]')).toBe(null)

    act(() => stream.setStatus('stale'))
    expect(region.textContent).toBe('')
  })

  it('announces an Operation’s end once, from its title line, and not each chunk', () => {
    let state: OperationState = { status: 'running', total: 3, done: 1, skipped: 0, failed: 0, problems: [] }
    const listeners = new Set<() => void>()
    const run: OperationRun = {
      getState: () => state,
      subscribe: (listener) => (listeners.add(listener), () => listeners.delete(listener)),
      stop: () => undefined,
      finished: new Promise(() => undefined),
    }
    function move(next: OperationState) {
      state = next
      act(() => listeners.forEach((listener) => listener()))
    }

    render(<OperationPanel label="Requeue failed runs" run={run} onDismiss={() => undefined} onRunAgain={() => undefined} />)
    const title = screen.getByText('Requeue failed runs')
    expect(title.getAttribute('aria-live')).toBe('polite')

    move({ ...state, done: 2 })
    expect(title.textContent).toBe('Requeue failed runs')

    move({ ...state, status: 'completed', done: 3 })
    expect(title.textContent).toBe('Requeue failed runs: Completed')
  })
})

describe('detail headings', () => {
  function Body() {
    return (
      <>
        <FieldGrid label="Outcome">{null}</FieldGrid>
        <HistoryList label="History" items={[]} />
        <Text as="h3" tone="label">
          Arguments
        </Text>
      </>
    )
  }

  it('heads an object’s own page with its name as the h1, and its sections as h2', () => {
    render(<DetailPanel label="Run details" name="billing.charge"><Body /></DetailPanel>)

    expect(screen.getByRole('heading', { level: 1, name: 'billing.charge' })).toBeTruthy()
    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual(['Outcome', 'History', 'Arguments'])
  })

  it('keeps a docked panel’s name an h2 under the list’s title, and its sections h3', () => {
    function Docked() {
      const frame = usePageFrame({ viewId: 'jobs.runs', rows: [{ id: 'r1' }] })

      return (
        <PageFrame frame={frame} panel={<DetailPanel label="Run details" name="billing.charge"><Body /></DetailPanel>}>
          <p>List</p>
        </PageFrame>
      )
    }

    render(<RouterProvider router={createMemoryRouter([{ path: '/', element: <Docked /> }])} />)

    expect(screen.getByRole('heading', { level: 2, name: 'billing.charge' })).toBeTruthy()
    expect(screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual(['Outcome', 'History', 'Arguments'])
  })
})
