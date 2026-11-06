import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState, type ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Button } from '../../src/components/button.tsx'
import { Dialog, Drawer } from '../../src/components/dialog.tsx'
import { Inspector } from '../../src/components/inspector/inspector.tsx'
import { LiveIndicator, ToastRegion } from '../../src/components/live.tsx'
import { StatusMark } from '../../src/components/marks.tsx'
import { CommandPalette } from '../../src/components/palette/command-palette.tsx'
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

describe('overlays', () => {
  function Opener({ kind }: { kind: 'dialog' | 'drawer' }) {
    const [open, setOpen] = useState(false)
    const Overlay = kind === 'dialog' ? Dialog : Drawer

    return (
      <>
        <Button onPress={() => setOpen(true)}>Open details</Button>
        <Overlay isOpen={open} onOpenChange={setOpen} title="Run details">
          <Button onPress={() => setOpen(false)}>Close details</Button>
        </Overlay>
      </>
    )
  }

  it.each(['dialog', 'drawer'] as const)('returns focus to the control that opened the %s', async (kind) => {
    const user = userEvent.setup()
    render(<Opener kind={kind} />)
    const opener = screen.getByRole('button', { name: 'Open details' })

    await user.click(opener)
    expect(screen.getByRole('dialog', { name: 'Run details' })).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Close details' }))

    expect(screen.queryByRole('dialog')).toBeNull()
    await waitFor(() => expect(document.activeElement).toBe(opener))
  })

  it('closes on Esc and returns focus', async () => {
    const user = userEvent.setup()
    render(<Opener kind="drawer" />)
    const opener = screen.getByRole('button', { name: 'Open details' })

    await user.click(opener)
    await user.keyboard('{Escape}')

    expect(screen.queryByRole('dialog')).toBeNull()
    await waitFor(() => expect(document.activeElement).toBe(opener))
  })
})

describe('live indicator', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('announces Stale through a live region and offers Reconnect', () => {
    vi.useFakeTimers()
    const stream = createFakeStream()
    const reconnect = vi.spyOn(stream, 'reconnect')
    render(withDashboard(<LiveIndicator />, stream))

    expect(screen.getByText('Live')).toBeTruthy()

    stream.setStatus('stale')
    act(() => {
      vi.advanceTimersByTime(40_000)
    })

    expect(screen.getByText('Stale — 40 s')).toBeTruthy()
    expect(screen.getByRole('status').textContent).toBe('Live updates stopped. What you see may be out of date.')

    act(() => screen.getByRole('button', { name: 'Reconnect' }).click())
    expect(reconnect).toHaveBeenCalled()
  })
})

describe('toasts and buttons', () => {
  it('shows a toast with its action, which dismisses it', async () => {
    const user = userEvent.setup()
    const undo = vi.fn()

    function Show() {
      const { toasts } = useDashboard()

      return <Button onPress={() => toasts.show({ message: 'Held 3 generations', action: { label: 'Undo', onAction: undo } })}>Hold</Button>
    }

    render(withDashboard(<><Show /><ToastRegion /></>))
    await user.click(screen.getByRole('button', { name: 'Hold' }))
    await user.click(screen.getByRole('button', { name: 'Undo' }))

    expect(undo).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('Held 3 generations')).toBeNull()
  })

  it('keeps a disabled action focusable, explains why, and ignores presses', async () => {
    const user = userEvent.setup()
    const onPress = vi.fn()
    render(<Button onPress={onPress} disabledReason="Running - cancel instead">Requeue</Button>)
    const button = screen.getByRole('button', { name: 'Requeue' })

    await user.tab()
    expect(document.activeElement).toBe(button)
    expect(screen.getByText('Running - cancel instead').id).toBe(button.getAttribute('aria-describedby'))

    await user.click(button)
    expect(onPress).not.toHaveBeenCalled()
  })

  it.each(['sched', 'enq', 'proc', 'ok', 'fail', 'off'] as const)('marks the %s role with its class and keeps the label outside the dot', (role) => {
    render(<StatusMark tone={role} label="State" />)

    const status = screen.getByText('State')
    const mark = status.querySelector('.hd-mark')!

    expect(status.classList).toContain(`hd-tone-${role}`)
    expect(mark.getAttribute('aria-hidden')).toBe('true')
    expect(mark.textContent).toBe('')
  })
})

describe('command palette', () => {
  it('filters items as the operator types and runs the chosen one', async () => {
    const user = userEvent.setup()
    const openRuns = vi.fn()
    const onOpenChange = vi.fn()
    render(
      <CommandPalette
        isOpen
        onOpenChange={onOpenChange}
        items={[
          { id: 'runs', label: 'Go to runs', kind: 'View', onAction: openRuns },
          { id: 'needs', label: 'Go to Needs you', kind: 'View', onAction: vi.fn() },
        ]}
      />,
    )

    await user.keyboard('runs')
    expect(screen.queryByRole('menuitem', { name: /Needs you/ })).toBeNull()

    await user.keyboard('{Enter}')
    expect(openRuns).toHaveBeenCalledTimes(1)
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })
})

describe('inspector', () => {
  function stubWidth(floating: boolean) {
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: floating, media: query }))
  }

  function Selection() {
    const [open, setOpen] = useState(false)

    return (
      <>
        <button type="button" onClick={() => setOpen(true)}>
          run-000001
        </button>
        {open ? (
          <Inspector label="Run run-000001" onClose={() => setOpen(false)}>
            <p>Failed</p>
          </Inspector>
        ) : null}
      </>
    )
  }

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('takes focus below 1280 px, closes on Esc, and returns focus to what opened it', async () => {
    stubWidth(true)
    const user = userEvent.setup()
    render(<Selection />)

    const opener = screen.getByRole('button', { name: 'run-000001' })
    await user.click(opener)

    const inspector = screen.getByRole('complementary', { name: 'Run run-000001' })
    expect(document.activeElement).toBe(inspector)

    await user.keyboard('{Escape}')

    expect(screen.queryByRole('complementary', { name: 'Run run-000001' })).toBeNull()
    expect(document.activeElement).toBe(opener)
  })

  it('leaves focus on the list when docked beside it', async () => {
    stubWidth(false)
    const user = userEvent.setup()
    render(<Selection />)

    const opener = screen.getByRole('button', { name: 'run-000001' })
    await user.click(opener)

    expect(screen.getByRole('complementary', { name: 'Run run-000001' })).toBeTruthy()
    expect(document.activeElement).toBe(opener)
  })
})
