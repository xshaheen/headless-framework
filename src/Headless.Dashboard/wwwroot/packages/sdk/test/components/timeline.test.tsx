import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { Timeline, type TimelineEvent } from '../../src/components/timeline/timeline.tsx'
import { LocaleProvider } from '../../src/locale/locale.tsx'

const events: TimelineEvent[] = [
  { id: 'queued', label: 'Queued', at: '2026-11-05T09:50:00Z' },
  { id: 'started', label: 'Started', at: '2026-11-05T09:51:00Z', actor: 'node-a', tone: 'proc' },
  { id: 'failed', label: 'Failed', at: '2026-11-05T09:52:00Z', text: 'Gateway timeout', tone: 'fail' },
]

/** A view that shows the selected event the way a module's inspector would. */
function View() {
  const [selected, setSelected] = useState<string>()

  return (
    <LocaleProvider>
      <Timeline label="Run timeline" events={events} selectedId={selected} onSelect={setSelected} />
      <p>{selected ? `Inspecting ${selected}` : 'Nothing selected'}</p>
    </LocaleProvider>
  )
}

describe('Timeline', () => {
  it('moves between events with the arrow keys and selects the focused one with Enter, keeping focus', async () => {
    const user = userEvent.setup()
    render(<View />)

    const rows = screen.getAllByRole('row')
    expect(rows.map((row) => row.getAttribute('tabindex'))).toEqual(['0', '-1', '-1'])

    await user.tab()
    expect(document.activeElement).toBe(rows[0])

    await user.keyboard('{ArrowDown}{ArrowDown}')
    expect(document.activeElement).toBe(rows[2])
    expect(screen.getByText('Nothing selected')).toBeTruthy()

    await user.keyboard('{ArrowUp}{Enter}')

    expect(screen.getByText('Inspecting started')).toBeTruthy()
    expect(rows[1]!.getAttribute('aria-selected')).toBe('true')
    expect(document.activeElement).toBe(rows[1])
  })

  it('shows each event’s actor and detail', () => {
    render(<View />)

    expect(screen.getByText('by node-a', { exact: false })).toBeTruthy()
    expect(screen.getByText('Gateway timeout')).toBeTruthy()
  })
})
