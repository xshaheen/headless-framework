import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { StatCard, StatStrip } from '../../src/components/display/stat-strip.tsx'
import { TrendChip, trendSentiment } from '../../src/components/display/trend-chip.tsx'
import { LocaleProvider } from '../../src/locale/locale.tsx'

function renderChip(chip: React.ReactElement) {
  return render(<LocaleProvider>{chip}</LocaleProvider>)
}

describe('TrendChip', () => {
  it('reads a falling failure rate as good news, and a rising one as bad, whatever the sign', () => {
    const { container } = renderChip(
      <>
        <TrendChip value={-0.12} goodDirection="down" />
        <TrendChip value={0.12} goodDirection="down" />
      </>,
    )

    const [fell, rose] = [...container.querySelectorAll<HTMLElement>('[data-sentiment]')]
    expect(fell!.dataset.sentiment).toBe('good')
    expect(fell!.classList.contains('hd-tone-ok')).toBe(true)
    expect(rose!.dataset.sentiment).toBe('bad')
    expect(rose!.classList.contains('hd-tone-fail')).toBe(true)
  })

  it.each([
    [0.18, 'up', 'good'],
    [-0.18, 'up', 'bad'],
    [0.18, 'neutral', 'neutral'],
    [0, 'down', 'neutral'],
  ] as const)('takes %d with goodDirection %s as %s', (value, goodDirection, sentiment) => {
    expect(trendSentiment(value, goodDirection)).toBe(sentiment)
  })

  it('writes the signed change with its arrow in one bidi isolate, so colour is never the only cue', () => {
    const { container } = renderChip(<TrendChip value={0.184} />)

    const isolate = container.querySelector('bdi')!
    expect(isolate.textContent).toBe('+18.4%')
    expect(isolate.querySelector('svg[aria-hidden="true"]')).not.toBeNull()
  })

  it('writes zero with no sign, and another unit when the server serves one', () => {
    const { container } = renderChip(
      <>
        <TrendChip value={0} />
        <TrendChip value={-0.011} goodDirection="down">
          −1.1 pt
        </TrendChip>
      </>,
    )

    expect([...container.querySelectorAll('bdi')].map((bdi) => bdi.textContent)).toEqual(['0%', '−1.1 pt'])
  })
})

describe('StatCard trend', () => {
  it('sits on the value’s line and joins the card’s name after a space', () => {
    renderChip(
      <StatStrip label="Runs by state">
        <StatCard label="Failed" value="23" tone="fail" trend={<TrendChip value={0.18} goodDirection="down" />} />
        <StatCard label="Queued" value="4" />
      </StatStrip>,
    )

    const strip = screen.getByRole('region', { name: 'Runs by state' })
    expect(strip.textContent).toBe('Failed 23 +18%Queued 4')
    expect(within(strip).getAllByText('+18%')).toHaveLength(1)
  })
})
