import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ScheduleStrip, type ScheduleTrack } from '../../src/components/schedule/schedule-strip.tsx'
import { DashboardProvider } from '../../src/context.tsx'
import { createDashboardClient } from '../../src/data/client.tsx'
import { createFakeStream } from '../../src/testing/fake-stream.ts'

const now = Date.parse('2026-11-05T10:00:00Z')
const minutes = (count: number) => new Date(now + count * 60_000).toISOString()

const tracks: ScheduleTrack[] = [
  {
    id: 'billing.charge',
    label: 'billing.charge',
    ticks: [
      { id: 'run-1', at: minutes(-20), label: 'failed', tone: 'needs' },
      { id: 'fire-1', at: minutes(10), label: 'next fire' },
      { id: 'fire-2', at: minutes(20), label: 'next fire' },
      { id: 'fire-3', at: minutes(40), label: 'next fire' },
      // Outside the default hour each side of now.
      { id: 'fire-4', at: minutes(90), label: 'next fire' },
    ],
  },
]

function renderStrip(props: { onSelect?: (id: string) => void; onSpanChange?: (span: number) => void; tracks?: ScheduleTrack[] } = {}) {
  render(
    <DashboardProvider
      client={createDashboardClient()}
      environment={{
        basePath: '/headless',
        apiBase: '/headless/api',
        session: { authenticated: true },
        permissions: {},
        stream: createFakeStream(),
        api: { request: () => Promise.reject(new Error('No API in this test.')) },
      }}
    >
      <ScheduleStrip label="Schedule" tracks={tracks} now={now} timeZone="Europe/Berlin" {...props} />
    </DashboardProvider>,
  )
}

describe('ScheduleStrip', () => {
  it('draws one mark per fire in the window, each named with its track, status, and time in the scheduler zone', () => {
    renderStrip()

    const marks = screen.getAllByRole('gridcell')

    expect(marks).toHaveLength(4)
    expect(marks[1]!.getAttribute('aria-label')).toMatch(/^billing\.charge, next fire, .*11:10:00/)
    expect(marks[0]!.getAttribute('class')).not.toContain('hd-strip-future')
    expect(marks[1]!.getAttribute('class')).toContain('hd-strip-future')
  })

  it('merges a dense schedule into at most one mark per 24th of the window, keeping every tick in a count', () => {
    // Every 30 s for the past hour: 120 runs, which would draw an unreadable band one tick apart.
    const dense = Array.from({ length: 120 }, (_, index) => ({
      id: `run-${index}`,
      at: new Date(now - (index + 1) * 30_000).toISOString(),
      label: 'succeeded',
      tone: 'healthy' as const,
    }))
    renderStrip({ tracks: [{ id: 'sandbox.progress', label: 'sandbox.progress', ticks: dense }] })

    const marks = screen.getAllByRole('gridcell')
    const counts = marks.map((mark) => Number(/, (\d+) ticks,/.exec(mark.getAttribute('aria-label')!)?.[1] ?? 1))
    const xs = marks.map((mark) => Number.parseFloat(mark.querySelector('rect')!.getAttribute('x')!))

    expect(marks.length).toBeLessThanOrEqual(12)
    expect(counts.reduce((sum, count) => sum + count, 0)).toBe(120)
    xs.slice(1).forEach((x, index) => expect(x - xs[index]!).toBeGreaterThanOrEqual(100 / 24 - 1e-9))
  })

  it('narrows the window with ] and widens it with [', async () => {
    const user = userEvent.setup()
    const onSpanChange = vi.fn()
    renderStrip({ onSpanChange })

    await user.keyboard(']')

    expect(onSpanChange).toHaveBeenLastCalledWith(30 * 60_000)
    expect(screen.getAllByRole('gridcell').map((mark) => mark.getAttribute('aria-label')?.split(',')[1]?.trim())).toEqual([
      'failed',
      'next fire',
      'next fire',
    ])

    // `[` opens a key descriptor in user-event, so `[[` types one `[`.
    await user.keyboard('[[[[')

    expect(onSpanChange).toHaveBeenLastCalledWith(2 * 60 * 60_000)
    expect(screen.getAllByRole('gridcell')).toHaveLength(5)
  })

  it('moves between ticks with the arrow keys and selects the one behind a tick with Enter', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()
    renderStrip({ onSelect })

    await user.tab()
    await user.keyboard('{ArrowRight}{ArrowRight}{Enter}')

    expect(onSelect).toHaveBeenCalledWith('fire-2')
  })
})
