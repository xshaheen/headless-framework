import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { afterAll, beforeAll, describe, expect, it } from 'vitest'
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

  it('stays one tab stop, its times showing the exact moment on a press rather than taking a stop each', async () => {
    const user = userEvent.setup()
    render(
      <View />,
    )

    await user.tab()
    expect(document.activeElement).toBe(screen.getAllByRole('row')[0])
    await user.tab()
    expect(screen.getByRole('grid').contains(document.activeElement)).toBe(false)

    await user.click(screen.getAllByRole('button', { hidden: false })[0]!)
    expect((await screen.findByRole('tooltip')).textContent).toBe('Nov 5, 2026, 09:50:00 UTC')
  })

  it('shows each event’s actor and detail', () => {
    render(<View />)

    expect(screen.getByText('by node-a', { exact: false })).toBeTruthy()
    expect(screen.getByText('Gateway timeout')).toBeTruthy()
  })
})

/** `count` trace rows, each one level under the one before, until the chain restarts every five rows. */
function treeEvents(count: number): TimelineEvent[] {
  return Array.from({ length: count }, (_, index) => ({
    id: `e${index}`,
    label: `Row ${index}`,
    at: '2026-11-05T09:50:00Z',
    depth: index % 5,
    tags: index === 3 ? ['clock skew'] : undefined,
  }))
}

describe('Timeline with depth', () => {
  // jsdom lays nothing out: every element reports a 560 px viewport, room for ten rows, and keeps its scroll offset.
  const offsets = new WeakMap<Element, number>()

  beforeAll(() => {
    Object.defineProperty(HTMLElement.prototype, 'clientHeight', { configurable: true, get: () => 560 })
    Object.defineProperty(HTMLElement.prototype, 'scrollTop', {
      configurable: true,
      get(this: Element) {
        return offsets.get(this) ?? 0
      },
      set(this: Element, value: number) {
        offsets.set(this, value)
      },
    })
  })

  afterAll(() => {
    for (const name of ['clientHeight', 'scrollTop']) {
      Reflect.deleteProperty(HTMLElement.prototype, name)
    }
  })

  function renderTree(events: TimelineEvent[], onSelect?: (id: string) => void) {
    return render(
      <LocaleProvider>
        <Timeline label="Trace" events={events} onSelect={onSelect} />
      </LocaleProvider>,
    )
  }

  it('is a treegrid whose rows state their level and read their tags', () => {
    renderTree(treeEvents(5))

    const tree = screen.getByRole('treegrid', { name: 'Trace' })
    const rows = screen.getAllByRole('row')
    expect(tree.getAttribute('aria-rowcount')).toBe('5')
    expect(rows.map((row) => row.getAttribute('aria-level'))).toEqual(['1', '2', '3', '4', '5'])
    expect(rows[3]!.textContent).toContain('level 4')
    expect(rows[3]!.textContent).toContain('clock skew')
  })

  it('renders a bounded number of 1,000 rows, and End reaches the last with focus on it', async () => {
    const user = userEvent.setup()
    renderTree(treeEvents(1000))

    expect(screen.getByRole('treegrid').getAttribute('aria-rowcount')).toBe('1000')
    expect(screen.getAllByRole('row').length).toBeLessThan(40)

    await user.tab()
    await user.keyboard('{End}')

    expect(document.activeElement?.getAttribute('aria-rowindex')).toBe('1000')
    expect(document.activeElement?.textContent).toContain('Row 999')
    expect(screen.getAllByRole('row').length).toBeLessThan(40)
  })

  it('moves with the arrow keys and opens the focused row with Enter', async () => {
    const user = userEvent.setup()
    const opened: string[] = []
    renderTree(treeEvents(5), (id) => opened.push(id))

    await user.tab()
    await user.keyboard('{ArrowDown}{ArrowDown}{ArrowUp}{Enter}')

    expect(document.activeElement?.getAttribute('aria-rowindex')).toBe('2')
    expect(opened).toEqual(['e1'])
  })
})
