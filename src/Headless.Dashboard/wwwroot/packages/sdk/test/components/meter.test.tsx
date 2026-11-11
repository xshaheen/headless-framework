import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Meter, meterTone } from '../../src/components/display/meter.tsx'

const thresholds = [
  { from: 8_000, tone: 'sched' as const },
  { from: 10_000, tone: 'fail' as const },
]

describe('Meter', () => {
  it('is a meter named by its label that reads and shows its value label', () => {
    render(<Meter label="Queue depth" value={9_400} max={12_000} valueLabel="9,400 of 12,000 messages, near the limit" />)

    const meter = screen.getByRole('meter', { name: 'Queue depth' })
    expect(meter.getAttribute('aria-valuenow')).toBe('9400')
    expect(meter.getAttribute('aria-valuetext')).toBe('9,400 of 12,000 messages, near the limit')
    expect(meter.textContent).toContain('9,400 of 12,000 messages, near the limit')
  })

  it.each([
    [4_000, 'proc'],
    [8_000, 'sched'],
    [11_000, 'fail'],
  ] as const)('fills %d in the %s tone of the highest threshold it reached', (value, tone) => {
    const { container } = render(
      <Meter label="Queue depth" value={value} max={12_000} valueLabel={`${value} messages`} thresholds={thresholds} />,
    )

    expect(container.querySelector(`.hd-tone-${tone}`)).not.toBeNull()
    expect(meterTone(value, 'proc', thresholds)).toBe(tone)
  })

  it('marks each threshold inside the range on the track, from its inline start', () => {
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
