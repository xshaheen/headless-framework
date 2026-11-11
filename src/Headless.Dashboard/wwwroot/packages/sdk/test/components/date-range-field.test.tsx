import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState, type ReactNode } from 'react'
import { createMemoryRouter, RouterProvider, useLocation } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DateRangeField } from '../../src/components/table/date-range-field.tsx'
import { timeRangePresets, type TimeRange } from '../../src/date-range/time-range.ts'
import { useListRange, useTimeRangeSearch } from '../../src/date-range/use-time-range.ts'
import { LocaleProvider } from '../../src/locale/locale.tsx'

function Held({
  initial,
  onChange = () => undefined,
  min,
  max,
}: {
  initial?: TimeRange
  onChange?: (range: TimeRange | undefined) => void
  min?: string
  max?: string
}) {
  const [value, setValue] = useState(initial)

  return (
    <DateRangeField
      label="Due between"
      compact
      presets={timeRangePresets}
      value={value}
      min={min}
      max={max}
      onChange={(range) => {
        setValue(range)
        onChange(range)
      }}
    />
  )
}

function inZone(zone: string, children: ReactNode) {
  localStorage.setItem('headless-dashboard:time-zone', zone)

  return <LocaleProvider>{children}</LocaleProvider>
}

async function openChip(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: /Due between/ }))

  return screen.findByRole('dialog')
}

function todayMarked(dialog: HTMLElement) {
  return within(dialog)
    .getAllByRole('button')
    .filter((cell) => cell.hasAttribute('data-zone-today'))
    .map((cell) => cell.getAttribute('aria-label')?.replace(/^Today, /, ''))
}

beforeEach(() => {
  localStorage.clear()
  vi.useFakeTimers({ shouldAdvanceTime: true })
})

afterEach(() => {
  vi.useRealTimers()
  localStorage.clear()
})

describe('DateRangeField presets', () => {
  it('offers the rolling windows in the chip’s popover and reads the chosen one on the chip', async () => {
    vi.setSystemTime(new Date('2026-11-10T10:00:00Z'))
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const onChange = vi.fn()
    render(inZone('UTC', <Held onChange={onChange} />))

    const dialog = await openChip(user)
    const presets = within(dialog).getByRole('group', { name: 'Due between presets' })
    expect(within(presets).getAllByRole('button').map((button) => button.textContent)).toEqual([
      '15 min',
      '1 hr',
      '24 hr',
      '7 days',
      '30 days',
      'Custom',
    ])

    await user.click(within(presets).getByRole('button', { name: '24 hr' }))

    expect(onChange).toHaveBeenLastCalledWith({ kind: 'preset', preset: '24h' })
    expect(within(presets).getByRole('button', { name: '24 hr' }).getAttribute('aria-pressed')).toBe('true')
    // A preset needs no days, so the calendar steps aside until Custom.
    expect(within(dialog).queryByRole('grid')).toBeNull()

    await user.keyboard('{Escape}')
    expect((await screen.findByRole('button', { name: /Due between/ })).textContent).toBe('Due between24 hr')
  })

  it('fills the days the preset covered when Custom is chosen, counted in the display zone', async () => {
    // 22:30 UTC on the 10th is already the 11th in Riyadh.
    vi.setSystemTime(new Date('2026-11-10T22:30:00Z'))
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const onChange = vi.fn()
    render(inZone('Asia/Riyadh', <Held initial={{ kind: 'preset', preset: '7d' }} onChange={onChange} />))

    const dialog = await openChip(user)
    await user.click(within(dialog).getByRole('button', { name: 'Custom' }))

    expect(onChange).toHaveBeenLastCalledWith({ kind: 'custom', start: '2026-11-04', end: '2026-11-11' })
    const start = within(dialog).getByRole('group', { name: 'Due between' })
    expect(within(start).getAllByRole('spinbutton', { name: /day/i }).map((day) => day.getAttribute('aria-valuenow'))).toEqual([
      '4',
      '11',
    ])
  })
})

describe('DateRangeField calendar', () => {
  it('marks today in the display zone, not the browser’s, and moves it at that zone’s midnight', async () => {
    // 21:59:30 UTC is 23:59:30 in Cairo, 30 seconds before its 11 November begins.
    vi.setSystemTime(new Date('2026-11-10T21:59:30Z'))
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    render(inZone('Africa/Cairo', <Held initial={{ kind: 'custom', start: '2026-11-02', end: '2026-11-03' }} />))

    const dialog = await openChip(user)
    expect(todayMarked(dialog)).toEqual(['Tuesday, November 10, 2026'])

    act(() => {
      vi.advanceTimersByTime(31_000)
    })

    await waitFor(() => expect(todayMarked(dialog)).toEqual(['Wednesday, November 11, 2026']))
  })

  it('shows every day disabled when the earliest day falls after the latest, instead of failing to render', async () => {
    vi.setSystemTime(new Date('2026-11-10T10:00:00Z'))
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    render(
      inZone('UTC', <Held initial={{ kind: 'custom', start: '2026-11-02', end: '2026-11-03' }} min="2026-11-20" max="2026-11-05" />),
    )

    const dialog = await openChip(user)
    const days = within(within(dialog).getByRole('grid')).getAllByRole('button')

    expect(days.length).toBeGreaterThan(27)
    expect(days.every((day) => day.getAttribute('aria-disabled') === 'true')).toBe(true)
  })
})

describe('useTimeRangeSearch', () => {
  function View() {
    const [range, setRange] = useTimeRangeSearch()
    const listRange = useListRange(range)
    const location = useLocation()

    return (
      <>
        <DateRangeField label="Due between" compact presets={timeRangePresets} value={range} onChange={setRange} />
        <output aria-label="URL">{location.search}</output>
        <output aria-label="List range">{listRange ? `${listRange.start} ${listRange.end}` : 'all'}</output>
      </>
    )
  }

  function at(url: string) {
    const router = createMemoryRouter([{ path: '/', element: <View /> }], { initialEntries: [url] })
    render(inZone('Africa/Cairo', <RouterProvider router={router} />))

    return router
  }

  function shown(name: string) {
    // An open popover hides the page from assistive technology, so the readouts are found among hidden elements.
    return screen.getByRole('status', { name, hidden: true }).textContent
  }

  it('reads custom days from the URL and maps them to the list’s instants in the display zone', () => {
    at('/?from=2026-11-01&to=2026-11-10&open=r1')

    expect(screen.getByRole('button', { name: /Due between/ }).textContent).toBe('Due betweenNov 1 – Nov 10')
    expect(shown('List range')).toBe('2026-10-31T22:00:00.000Z 2026-11-10T22:00:00.000Z')
  })

  it('reads a bad value as no range, and writes a chosen preset over custom days, keeping other params', async () => {
    vi.setSystemTime(new Date('2026-11-10T10:00:00Z'))
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const router = at('/?from=2026-11-10&to=2026-11-01&open=r1')

    expect(shown('List range')).toBe('all')

    const dialog = await openChip(user)
    await user.click(within(dialog).getByRole('button', { name: '7 days' }))

    expect(shown('URL')).toBe('?open=r1&range=7d')
    // A rolling window of seven days that ends at the moment it was picked.
    const [start, end] = shown('List range')!.split(' ').map(Date.parse)
    expect(end! - start!).toBe(7 * 86_400_000)
    expect(Math.abs(end! - Date.now())).toBeLessThan(5_000)
    expect(router.state.historyAction).toBe('PUSH')

    await user.click(within(dialog).getByRole('button', { name: 'Clear range' }))
    expect(shown('URL')).toBe('?open=r1')
  })
})
