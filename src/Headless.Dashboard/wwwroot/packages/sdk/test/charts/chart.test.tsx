import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { Chart } from '../../src/charts/index.ts'
import type { DonutChartProps, TimeSeriesChartProps } from '../../src/charts/index.ts'
import { LocaleProvider } from '../../src/locale/locale.tsx'

// Ten-second buckets from 12:00:00 UTC; the node measured nothing for seconds 120 to 130, buckets 12 and 13.
const rate: TimeSeriesChartProps = {
  kind: 'line',
  label: 'Throughput',
  unit: 'messages/s',
  zone: 'UTC',
  start: '2026-11-10T12:00:00Z',
  intervalSeconds: 10,
  asOf: '2026-11-10T12:03:05Z',
  series: [
    {
      name: 'Published',
      tone: 'accent',
      points: [4, 5, 6, 5, 4, 5, 6, 5, 4, 5, 6, 5, null, null, 5, 6, 5, 4],
    },
  ],
}

function renderChart(props: Parameters<typeof Chart>[0]) {
  return render(
    <LocaleProvider>
      <Chart {...props} />
    </LocaleProvider>,
  )
}

/** The line's points, split where the path lifts the pen. */
function segments(path: string) {
  return path
    .split('M')
    .filter(Boolean)
    .map((segment) => segment.split('L').map((point) => point.trim().split(/[ ,]+/).map(Number)))
}

/** Each row of a table as the text of its cells, header row first. */
function cells(table: HTMLElement) {
  return within(table)
    .getAllByRole('row')
    .map((row) => [...row.querySelectorAll('th, td')].map((cell) => cell.textContent))
}

describe('Chart', () => {
  it.each([
    ['line', ['.recharts-line-curve']],
    ['area', ['.recharts-area-curve', '.recharts-area-area']],
  ] as const)('draws unmeasured buckets as a gap in the %s, not as a drop to zero', (kind, selectors) => {
    const { container } = renderChart({ ...rate, kind })

    for (const selector of selectors) {
      const parts = segments(container.querySelector(selector)!.getAttribute('d')!)

      // Twelve measured buckets, the gap, then four more: two runs of the pen, and no point for the two nulls. An area's
      // fill path walks each run's top and then its baseline, so it counts each run twice.
      const fill = selector === '.recharts-area-area'
      expect(parts.map((part) => part.length)).toEqual(fill ? [24, 8] : [12, 4])

      // Where zero would sit, from the drawn heights of the served 4 and 6: no top point lands there.
      const tops = parts.flatMap((part) => (fill ? part.slice(0, part.length / 2) : part))
      const y4 = tops[0]![1]!
      const y6 = tops[2]![1]!
      const zero = y4 + (y4 - y6) * 2
      expect(tops.every(([, y]) => Math.abs(y! - zero) > 1)).toBe(true)
    }
  })

  it('names the series, the range, and the zone in the image label', () => {
    renderChart(rate)

    expect(screen.getByRole('img').getAttribute('aria-label')).toBe(
      'Throughput: Published in messages/s, every 10 seconds, from 12:00:00 to 12:02:50 UTC; unmeasured buckets are gaps; as of Nov 10, 2026, 12:03:05 UTC.',
    )
  })

  it('lists the same values the tooltip shows, and keeps focus on the toggle', async () => {
    const user = userEvent.setup()
    const { container } = renderChart(rate)

    // Hover bucket 12 (12:02:00), one of the two the node did not measure.
    const surface = container.querySelector('.recharts-wrapper')!
    fireEvent.mouseMove(surface, { clientX: (480 / 17) * 12, clientY: 50 })
    // Recharts answers a mouse move on the next animation frame.
    const tooltip = container.querySelector<HTMLElement>('.recharts-tooltip-wrapper')!
    await waitFor(() => expect(within(tooltip).getByText('12:02:00')).toBeTruthy())
    expect(within(tooltip).getByText('Published').nextElementSibling?.textContent).toBe('not measured')

    // A page holds several charts, so the toggle names its own, still led by the words it shows.
    const toggle = screen.getByRole('button', { name: 'Show list: Throughput' })
    await user.click(toggle)

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Show chart: Throughput' }))
    expect(screen.queryByRole('img')).toBeNull()

    // The unit is said once, in the caption; each cell holds the number alone, and an unmeasured bucket says so.
    const table = screen.getByRole('table', { name: 'Throughput' })
    expect(within(table).getByText('Values in messages/s')).toBeTruthy()
    const rows = cells(table)
    expect(rows[0]).toEqual(['Time', 'Published'])
    expect(rows[1]).toEqual(['12:00:00', '4'])
    expect(rows[13]).toEqual(['12:02:00', 'not measured'])
    expect(rows[14]).toEqual(['12:02:10', 'not measured'])
    expect(rows[15]).toEqual(['12:02:20', '5'])
    expect(rows).toHaveLength(19)

    await user.keyboard('{Enter}')
    expect(document.activeElement).toBe(screen.getByRole('button', { name: /^Show list:/ }))
    expect(screen.getByRole('img')).toBeTruthy()
  })

  const outcomes: DonutChartProps = {
    kind: 'donut',
    label: 'Runs by outcome',
    unit: 'runs',
    zone: 'UTC',
    start: '2026-11-09T12:00:00Z',
    end: '2026-11-10T12:00:00Z',
    asOf: '2026-11-10T12:00:04Z',
    complete: true,
    parts: [
      { name: 'Succeeded', tone: 'ok', count: { value: 300, capped: false } },
      { name: 'Failed', tone: 'fail', count: { value: 100, capped: false } },
    ],
  }

  it('shows each part with its share when the counts are exact and complete', () => {
    renderChart(outcomes)

    expect(screen.getByRole('img').getAttribute('aria-label')).toBe(
      'Runs by outcome: Succeeded 300 runs, 75%; Failed 100 runs, 25%, from Nov 9, 12:00 to Nov 10, 12:00 UTC; as of Nov 10, 2026, 12:00:04 UTC.',
    )
  })

  it('lists the parts with numbers alone and the shares, under one caption that names the unit', async () => {
    const user = userEvent.setup()
    renderChart(outcomes)

    await user.click(screen.getByRole('button', { name: /^Show list:/ }))

    const table = screen.getByRole('table', { name: 'Runs by outcome' })
    expect(within(table).getByText('Counts in runs')).toBeTruthy()
    expect(cells(table)).toEqual([
      ['Part', 'Count', 'Share'],
      ['Succeeded', '300', '75%'],
      ['Failed', '100', '25%'],
    ])
  })

  it('gives a time series and a donut the same toggle, and draws its focus ring only for the keyboard', async () => {
    const user = userEvent.setup()
    render(
      <LocaleProvider>
        <Chart {...rate} />
        <Chart {...outcomes} />
      </LocaleProvider>,
    )

    const [series, donut] = screen.getAllByRole('button', { name: /^Show list:/ })
    expect(series!.className).toBe(donut!.className)

    // A pointer press leaves no keyboard-focus mark, which the stylesheet reads to hide the ring; Tab sets it.
    await user.click(series!)
    const pressed = screen.getByRole('button', { name: /^Show chart:/ })
    expect(document.activeElement).toBe(pressed)
    expect(pressed.hasAttribute('data-focus-visible')).toBe(false)
    await user.tab()
    expect(document.activeElement).toBe(donut)
    expect(donut!.hasAttribute('data-focus-visible')).toBe(true)

    await user.keyboard('{Enter}')
    expect(screen.getAllByRole('button', { name: /^Show chart:/ })).toHaveLength(2)
    expect(screen.getAllByRole('table').map((table) => table.getAttribute('aria-label'))).toEqual([
      'Throughput',
      'Runs by outcome',
    ])
  })

  it('shows the hovered part with its exact count and share in the tooltip', async () => {
    const { container } = renderChart(outcomes)

    fireEvent.mouseEnter(container.querySelectorAll('.recharts-pie-sector')[1]!)

    const tooltip = container.querySelector<HTMLElement>('.recharts-tooltip-wrapper')!
    await waitFor(() => expect(within(tooltip).getByText('Failed')).toBeTruthy())
    expect(within(tooltip).getByText('Failed').nextElementSibling?.textContent).toBe('100 runs, 25%')
  })

  it('replaces an incomplete donut with the list of counts and no shares', () => {
    renderChart({ ...outcomes, complete: false })

    expect(screen.queryByRole('img')).toBeNull()
    const table = screen.getByRole('table', { name: 'Runs by outcome' })
    expect(within(table).getByText('Counts in runs')).toBeTruthy()
    const rows = cells(table)
    expect(rows).toEqual([
      ['Part', 'Count'],
      ['Succeeded', '300'],
      ['Failed', '100'],
    ])
    expect(document.body.textContent).not.toMatch(/%/)
  })

  it('shows counts and no shares when a count is capped, whatever the complete flag says', () => {
    renderChart({ ...outcomes, parts: [outcomes.parts[0]!, { ...outcomes.parts[1]!, count: { value: 10_000, capped: true } }] })

    expect(screen.queryByRole('img')).toBeNull()
    const rows = cells(screen.getByRole('table', { name: 'Runs by outcome' }))
    expect(rows.slice(1)).toEqual([
      ['Succeeded', '300'],
      ['Failed', '10,000+'],
    ])
    expect(document.body.textContent).not.toMatch(/%/)
  })

  it('says the range is empty when every part counts zero, with no shares', () => {
    const zero = outcomes.parts.map((part) => ({ ...part, count: { value: 0, capped: false } }))

    renderChart({ ...outcomes, parts: zero })
    expect(screen.getByText('No runs in this range')).toBeTruthy()
    expect(document.body.textContent).not.toMatch(/%/)

    renderChart({ ...outcomes, parts: zero, empty: 'Nothing ran today' })
    expect(screen.getByText('Nothing ran today')).toBeTruthy()
  })

  it('names counts with no range as covering every item, and keeps their as-of time', () => {
    renderChart({ ...outcomes, start: undefined, end: undefined })

    expect(screen.getByRole('img').getAttribute('aria-label')).toBe(
      'Runs by outcome: Succeeded 300 runs, 75%; Failed 100 runs, 25%, all runs; as of Nov 10, 2026, 12:00:04 UTC.',
    )
  })

  it('says what partial counts with no range cover, with no shares', () => {
    renderChart({ ...outcomes, start: undefined, end: undefined, asOf: undefined, complete: false })

    expect(screen.queryByRole('img')).toBeNull()
    expect(screen.getByText('Counts of all runs. These counts are partial, so no shares are shown.')).toBeTruthy()
  })

  // Four days from Nov 9; Nov 10 is today, so Nov 11 and 12 count runs scheduled for them.
  const days: TimeSeriesChartProps = {
    kind: 'stacked-bar',
    label: 'Runs per day',
    unit: 'runs',
    zone: 'UTC',
    start: '2026-11-09T12:00:00Z',
    intervalSeconds: 86_400,
    asOf: '2026-11-10T12:00:04Z',
    present: { index: 1, label: 'Today' },
    series: [
      { name: 'Succeeded', tone: 'ok', points: [5, 3, 0, 0] },
      { name: 'Idle', tone: 'sched', points: [0, 2, 4, 1] },
    ],
  }

  it('draws the buckets after the present outlined, and marks the present on the axis', () => {
    const { container } = renderChart(days)

    const bars = [...container.querySelectorAll('.recharts-bar-rectangle path')]
    // Two series of four buckets; the zero-height bars draw nothing, so only the drawn ones are read.
    const drawn = bars.map((bar) => ({ fillOpacity: bar.getAttribute('fill-opacity'), stroke: bar.getAttribute('stroke') }))
    expect(drawn).toEqual([
      { fillOpacity: '1', stroke: 'none' },
      { fillOpacity: '1', stroke: 'none' },
      { fillOpacity: '1', stroke: 'none' },
      { fillOpacity: '0', stroke: 'var(--series-1)' },
      { fillOpacity: '0', stroke: 'var(--series-1)' },
    ])
    expect(container.querySelector('.recharts-reference-line')).toBeTruthy()
    expect(screen.getByText('Today')).toBeTruthy()
    expect(screen.getByRole('img').getAttribute('aria-label')).toBe(
      'Runs per day: Succeeded and Idle in runs, every 1 day, from Nov 9 to Nov 12 UTC; Today is Nov 10, later buckets are scheduled and drawn outlined; as of Nov 10, 2026, 12:00:04 UTC.',
    )
  })

  it('labels an empty range on the scale its grid is drawn at, not zero on every line', () => {
    renderChart({ ...days, series: days.series.map((item) => ({ ...item, points: item.points.map(() => 0) })) })

    expect(screen.getByText('2')).toBeTruthy()
    expect(screen.getByText('1')).toBeTruthy()
    expect(screen.getAllByText('0')).toHaveLength(1)
  })

  it('keeps every axis label a whole count when the series holds counts', () => {
    renderChart({ ...days, series: days.series.map((item, index) => ({ ...item, points: item.points.map(() => (index === 0 ? 31 : 0)) })) })

    expect(screen.getByText('32')).toBeTruthy()
    expect(screen.getByText('16')).toBeTruthy()
    expect(screen.queryByText('15.5')).toBeNull()
  })

  it('keeps a fractional scale for a rate', () => {
    renderChart({ ...days, series: days.series.map((item, index) => ({ ...item, points: item.points.map(() => (index === 0 ? 2.5 : 0)) })) })

    expect(screen.getByText('2.5')).toBeTruthy()
    expect(screen.getByText('1.25')).toBeTruthy()
  })

  it('drops the end date the present mark would run into when the range ends today', () => {
    renderChart({ ...days, present: { index: 3, label: 'Today' } })

    expect(screen.getByText('Today')).toBeTruthy()
    expect(screen.getByText('Nov 9')).toBeTruthy()
    expect(screen.queryByText('Nov 12')).toBeNull()
  })

  it('keeps both end dates when the present sits inside the range', () => {
    renderChart(days)

    expect(screen.getByText('Nov 9')).toBeTruthy()
    expect(screen.getByText('Nov 12')).toBeTruthy()
  })

  it('marks the present and the scheduled buckets in the list', async () => {
    const user = userEvent.setup()
    renderChart(days)

    await user.click(screen.getByRole('button', { name: /^Show list:/ }))

    expect(cells(screen.getByRole('table', { name: 'Runs per day' })).map((row) => row[0])).toEqual([
      'Time',
      'Nov 9',
      'Nov 10 · Today',
      'Nov 11 · scheduled',
      'Nov 12 · scheduled',
    ])
  })

  it.each([
    ['ltr', (first: number, last: number) => first < last],
    ['rtl', (first: number, last: number) => first > last],
  ] as const)('starts time at the inline start of a %s page, where the first date label sits', (dir, ordered) => {
    const { container } = render(
      <div dir={dir}>
        <LocaleProvider>
          <Chart {...rate} />
        </LocaleProvider>
      </div>,
    )

    const points = segments(container.querySelector('.recharts-line-curve')!.getAttribute('d')!).flat()
    expect(ordered(points[0]![0]!, points.at(-1)![0]!)).toBe(true)
  })

  it('keeps the image, its sentence, and the list toggle on a right-to-left page', async () => {
    const user = userEvent.setup()
    render(
      <div dir="rtl">
        <LocaleProvider>
          <Chart {...rate} />
        </LocaleProvider>
      </div>,
    )

    expect(screen.getByRole('img').getAttribute('aria-label')).toContain('Throughput')
    await user.click(screen.getByRole('button', { name: 'Show list: Throughput' }))
    expect(screen.getByRole('table', { name: 'Throughput' })).toBeTruthy()
  })
})
