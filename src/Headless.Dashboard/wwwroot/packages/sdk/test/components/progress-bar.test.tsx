import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ProgressBar } from '../../src/components/live.tsx'

describe('ProgressBar', () => {
  it('names the progress and reads its value text', () => {
    render(<ProgressBar label="Progress of import" value={62.5} max={100} valueText="62.5 % · step 25 of 40" />)

    const bar = screen.getByRole('progressbar', { name: 'Progress of import' })
    expect(bar.getAttribute('aria-valuetext')).toBe('62.5 % · step 25 of 40')
    expect(screen.getByText('62.5 % · step 25 of 40')).toBeTruthy()
  })

  it('keeps the value and its detail on the one line an inline bar draws', () => {
    render(
      <ProgressBar
        label="Progress of import"
        value={62.5}
        max={100}
        valueText="62.5 % · step 25 of 40"
        shortText="62.5 %"
        inline
        detail={<time dateTime="2026-11-05T10:00:05Z">5 s ago</time>}
      />,
    )

    const bar = screen.getByRole('progressbar', { name: 'Progress of import' })
    // The value and the detail share one text run, so a narrow cell cuts them together with one ellipsis.
    const line = screen.getByText((_, element) => element?.textContent === '62.5 % · 5 s ago' && element.tagName === 'SPAN')
    expect(bar.contains(line)).toBe(true)
    // The row shows the short form; a screen reader still hears the handler's message.
    expect(bar.getAttribute('aria-valuetext')).toBe('62.5 % · step 25 of 40')
  })
})
