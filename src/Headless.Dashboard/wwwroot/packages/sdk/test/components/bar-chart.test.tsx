import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { BarChart } from '../../src/components/display/bar-chart.tsx'
import { LocaleProvider } from '../../src/locale/locale.tsx'

const series = [
  { name: 'Succeeded', tone: 'ok' as const },
  { name: 'Failed', tone: 'fail' as const },
]

const columns = [
  { label: '09:00', values: [120, 4] },
  { label: '10:00', values: [1_300, 40] },
  { label: '11:00', values: [80, 0] },
]

describe('BarChart', () => {
  it('labels the image with a sentence naming the series, the range, and the tallest bar', () => {
    render(
      <LocaleProvider>
        <BarChart label="Outcomes per hour" series={series} columns={columns} unit="runs" />
      </LocaleProvider>,
    )

    expect(screen.getByRole('img').getAttribute('aria-label')).toBe(
      'Outcomes per hour: Succeeded and Failed, from 09:00 to 11:00; the tallest bar is 1,340 runs.',
    )
  })

  it('draws each column to scale from zero, stacking the series in order', () => {
    render(
      <LocaleProvider>
        <BarChart label="Outcomes per hour" series={series} columns={columns} unit="runs" />
      </LocaleProvider>,
    )

    const bars = [...screen.getByRole('img').querySelectorAll('rect')].map((rect) => [
      rect.getAttribute('class'),
      Number(rect.getAttribute('y')).toFixed(2),
      Number(rect.getAttribute('height')).toFixed(2),
    ])

    // The 10:00 column is the tallest: its two parts fill the plot from the baseline up.
    expect(bars[2]).toEqual(['hd-tone-ok', (100 - (1_300 / 1_340) * 100).toFixed(2), ((1_300 / 1_340) * 100).toFixed(2)])
    expect(bars[3]).toEqual(['hd-tone-fail', '0.00', ((40 / 1_340) * 100).toFixed(2)])
  })
})
