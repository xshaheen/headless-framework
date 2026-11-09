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
  it('draws unmeasured buckets as a gap in the line, not as a drop to zero', () => {
    const { container } = renderChart(rate)
    const line = container.querySelector('.recharts-line-curve')!
    const parts = segments(line.getAttribute('d')!)

    // Twelve measured buckets, the gap, then four more: two runs of the pen, and no point for the two nulls.
    expect(parts.map((part) => part.length)).toEqual([12, 4])

    // Where zero would sit, from the drawn heights of the served 4 and 6: no point lands there.
    const points = parts.flat()
    const y4 = points[0]![1]!
    const y6 = points[2]![1]!
    const zero = y4 + (y4 - y6) * 2
    expect(points.every(([, y]) => Math.abs(y! - zero) > 1)).toBe(true)
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

    const toggle = screen.getByRole('button', { name: 'Show list' })
    await user.click(toggle)

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Show chart' }))
    expect(screen.queryByRole('img')).toBeNull()

    const table = screen.getByRole('table', { name: 'Throughput' })
    const rows = cells(table)
    expect(rows[0]).toEqual(['Time', 'Published'])
    expect(rows[1]).toEqual(['12:00:00', '4 messages/s'])
    expect(rows[13]).toEqual(['12:02:00', 'not measured'])
    expect(rows[14]).toEqual(['12:02:10', 'not measured'])
    expect(rows[15]).toEqual(['12:02:20', '5 messages/s'])
    expect(rows).toHaveLength(19)

    await user.keyboard('{Enter}')
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Show list' }))
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

  it('replaces an incomplete donut with the list of counts and no shares', () => {
    renderChart({ ...outcomes, complete: false })

    expect(screen.queryByRole('img')).toBeNull()
    const table = screen.getByRole('table', { name: 'Runs by outcome' })
    const rows = cells(table)
    expect(rows).toEqual([
      ['Part', 'Count'],
      ['Succeeded', '300 runs'],
      ['Failed', '100 runs'],
    ])
    expect(document.body.textContent).not.toMatch(/%/)
  })

  it('shows counts and no shares when a count is capped, whatever the complete flag says', () => {
    renderChart({ ...outcomes, parts: [outcomes.parts[0]!, { ...outcomes.parts[1]!, count: { value: 10_000, capped: true } }] })

    expect(screen.queryByRole('img')).toBeNull()
    expect(screen.getByText('10,000+ runs')).toBeTruthy()
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
})
