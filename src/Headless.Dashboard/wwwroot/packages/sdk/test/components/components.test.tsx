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
import { formatJson, PayloadView } from '../../src/components/payload/payload-view.tsx'
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

  it('labels a status as well as marking it', () => {
    render(<StatusMark tone="needs" label="Failed" />)

    expect(screen.getByText('Failed')).toBeTruthy()
  })
})

describe('payload view', () => {
  it('formats JSON and keeps a large integer exact', () => {
    expect(formatJson('{"id":9007199254740993,"tags":[],"a":{"b":"x, y"}}')).toBe(
      '{\n  "id": 9007199254740993,\n  "tags": [],\n  "a": {\n    "b": "x, y"\n  }\n}',
    )

    render(<PayloadView content='{"amount":12345678901234567890}' contentType="application/json" />)
    expect(screen.getByText(/12345678901234567890/)).toBeTruthy()
  })

  it('shows the size and the first 64 KB of a large payload, with Copy all', () => {
    render(<PayloadView content={'x'.repeat(70 * 1024)} contentType="text/plain" />)

    expect(screen.getByText('70.0 KB, first 64 KB shown')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Copy all' })).toBeTruthy()
  })

  it('names an unsupported contract version instead of failing to parse', () => {
    render(<PayloadView content="{}" unsupportedVersion={3} />)

    expect(screen.getByText('Unsupported contract version 3')).toBeTruthy()
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
