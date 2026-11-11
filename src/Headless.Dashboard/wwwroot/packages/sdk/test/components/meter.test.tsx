import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Meter, meterTone } from '../../src/components/display/meter.tsx'

const thresholds = [
  { from: 8_000, tone: 'sched' as const },
  { from: 10_000, tone: 'fail' as const },
]

describe('Meter', () => {
  it('takes its accessible name from label and announces the served reading in words', () => {
    render(<Meter label="Queue depth" value={4_812} max={5_000} valueLabel="4,812 of 5,000 messages, over the warning level" />)

    const meter = screen.getByRole('meter', { name: 'Queue depth' })
    expect(meter.getAttribute('aria-valuenow')).toBe('4812')
    expect(meter.getAttribute('aria-valuetext')).toBe('4,812 of 5,000 messages, over the warning level')
    expect(meter.textContent).toContain('4,812 of 5,000 messages, over the warning level')
  })

  it.each([
    [4_000, 'proc'],
    [8_000, 'sched'],
    [11_000, 'fail'],
  ] as const)('colours a reading of %d in the %s role of the last threshold it passed', (value, tone) => {
    const { container } = render(
      <Meter label="Queue depth" value={value} max={12_000} valueLabel={`${value} messages`} thresholds={thresholds} />,
    )

    expect(container.querySelector(`.hd-tone-${tone}`)).not.toBeNull()
    expect(meterTone(value, 'proc', thresholds)).toBe(tone)
  })

  it('ticks only the thresholds strictly inside the scale, measured from the inline start', () => {
    const { container } = render(
      <Meter
        label="Queue depth"
        value={1}
        max={10_000}
        valueLabel="1 message"
        thresholds={[...thresholds, { from: 0, tone: 'ok' }]}
      />,
    )

    const marks = [...container.querySelectorAll<HTMLElement>('[data-threshold]')]
    expect(marks.map((mark) => mark.dataset.threshold)).toEqual(['8000'])
    expect(marks[0]!.style.insetInlineStart).toBe('80%')
  })
})
