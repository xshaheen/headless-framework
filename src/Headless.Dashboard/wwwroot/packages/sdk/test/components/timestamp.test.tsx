import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Timestamp } from '../../src/components/display/timestamp.tsx'
import { DataTable } from '../../src/components/table/data-table.tsx'
import { DashboardProvider } from '../../src/context.tsx'
import { createDashboardClient } from '../../src/data/client.tsx'
import { LocaleProvider } from '../../src/locale/locale.tsx'
import { createFakeStream } from '../../src/testing/fake-stream.ts'

function withDashboard(children: ReactNode) {
  return (
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
      {children}
    </DashboardProvider>
  )
}

beforeEach(() => {
  localStorage.clear()
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(new Date('2026-11-10T12:00:00Z'))
})

afterEach(() => {
  vi.useRealTimers()
  localStorage.clear()
})

describe('Timestamp', () => {
  it('turns a next fire into due once its time passes, with no new data', () => {
    render(
      <LocaleProvider>
        <Timestamp value="2026-11-10T12:00:03Z" scheduled />
      </LocaleProvider>,
    )

    expect(screen.getByRole('button', { name: 'in 3 sec.' })).toBeTruthy()

    act(() => {
      vi.advanceTimersByTime(5_000)
    })

    expect(screen.getByRole('button', { name: 'due 2 sec. ago' })).toBeTruthy()
  })

  it('counts a past time up in whole units, floored, without saying due when it is not scheduled', () => {
    render(
      <LocaleProvider>
        <Timestamp value="2026-11-10T11:58:30Z" />
      </LocaleProvider>,
    )

    // 90 seconds is one whole minute, not the two that rounding would say.
    expect(screen.getByRole('button', { name: '1 min. ago' })).toBeTruthy()

    act(() => {
      vi.advanceTimersByTime(30_000)
    })

    expect(screen.getByRole('button', { name: '2 min. ago' })).toBeTruthy()
  })

  it('keeps one timer for the whole page, however many times it shows', () => {
    vi.clearAllTimers()
    render(
      <LocaleProvider>
        {Array.from({ length: 50 }, (_, at) => (
          <Timestamp key={at} value={new Date(Date.parse('2026-11-10T11:00:00Z') - at * 61_000).toISOString()} />
        ))}
      </LocaleProvider>,
    )

    expect(vi.getTimerCount()).toBe(1)
  })

  it('shows the exact time, zone, and offset on keyboard focus and on a press', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    render(
      <LocaleProvider>
        <Timestamp value="2026-11-10T11:59:58Z" />
        <Timestamp value="2026-11-10T15:40:00Z" zone="America/New_York" scheduled />
      </LocaleProvider>,
    )

    await user.tab()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: '2 sec. ago' }))
    expect((await screen.findByRole('tooltip')).textContent).toBe('Nov 10, 2026, 11:59:58 UTC')

    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('tooltip')).toBeNull())

    await user.click(screen.getByRole('button', { name: 'in 3 hr.' }))
    expect((await screen.findByRole('tooltip')).textContent).toBe('Nov 10, 2026, 10:40:00 GMT-05:00 America/New_York')
  })

  it('shows what is missing in place of a time the server has not stored, and a bad time as it came', () => {
    render(
      <LocaleProvider>
        <Timestamp value={null} missing="Not tracked" />
        <Timestamp value="soon" />
      </LocaleProvider>,
    )

    expect(screen.getByText('Not tracked')).toBeTruthy()
    expect(screen.getByText('soon')).toBeTruthy()
  })

  it('takes its own press in a table row, by pointer and by keyboard, so the row is not selected', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const onSelectionChange = vi.fn()
    render(
      withDashboard(
        <DataTable
          label="Runs"
          rows={[{ id: 'r1', at: '2026-11-10T11:55:00Z' }]}
          selectionMode="single"
          selectedIds={new Set()}
          onSelectionChange={onSelectionChange}
          columns={[
            { id: 'id', header: 'Run', cell: (row) => row.id },
            { id: 'at', header: 'At', kind: 'time', cell: (row) => <Timestamp value={row.at} /> },
          ]}
          emptyState="No runs."
        />,
      ),
    )

    const time = screen.getByRole('button', { name: '5 min. ago' })
    await user.click(time)
    expect((await screen.findByRole('tooltip')).textContent).toBe('Nov 10, 2026, 11:55:00 UTC')

    act(() => time.focus())
    await user.keyboard('{Enter}')
    await user.keyboard(' ')

    expect(onSelectionChange).not.toHaveBeenCalled()
    expect(screen.getByRole('row', { name: /r1/ }).getAttribute('aria-selected')).toBe('false')
  })
})
