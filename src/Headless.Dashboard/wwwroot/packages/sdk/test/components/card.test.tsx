import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Card, CardRow, Cards } from '../../src/components/display/card.tsx'
import { ExceptionBlock } from '../../src/components/frame/exception-block.tsx'
import { RunsIcon } from '../../src/icons/index.ts'

describe('Card', () => {
  it('names its region by its heading and lists its rows with dot, name, meta, value, and second line', () => {
    render(
      <Cards>
        <Card title="Processing now" caption="2 running" action={<a href="#all">All</a>}>
          <CardRow tone="proc" name="sandbox.progress" meta="node-a" value="24 s">
            48 %
          </CardRow>
        </Card>
      </Cards>,
    )

    const card = screen.getByRole('region', { name: 'Processing now' })
    expect(within(card).getByRole('heading', { name: 'Processing now' })).toBeTruthy()
    expect(within(card).getByText('2 running')).toBeTruthy()
    const row = within(card).getByRole('listitem')
    expect(row.classList.contains('hd-tone-proc')).toBe(true)
    expect(row.textContent).toBe('sandbox.progressnode-a24 s48 %')
  })

  it('keeps the whole name and its facts, with the first line as the hover text, and one value column', () => {
    render(
      <Card title="Next recurring runs">
        <CardRow tone="sched" name="sandbox.overlap-fails" meta="*/10 * * * *" value="in 9 sec." title="sandbox.overlap-fails */10 * * * *" />
        <CardRow tone="sched" name="sandbox.nightly" meta="0 3 * * *" value="in 6 hr." title="sandbox.nightly 0 3 * * *" />
      </Card>,
    )

    const [first, second] = screen.getAllByRole('listitem')
    expect(first!.textContent).toBe('sandbox.overlap-fails*/10 * * * *in 9 sec.')
    expect(screen.getByTitle('sandbox.overlap-fails */10 * * * *').textContent).toBe('sandbox.overlap-fails*/10 * * * *')
    // The values sit in a column of one width, so the times line up down the card.
    expect(within(first!).getByText('in 9 sec.').className).toBe(within(second!).getByText('in 6 hr.').className)
  })

  it('wraps a name or fact too wide for a phone instead of cutting it short, and keeps the ellipsis above it', () => {
    render(
      <Card title="Needs attention">
        <CardRow tone="fail" name="Billing.Orders.OrderPlacedConsumer" meta="37 messages failed for good" value="41 sec. ago" />
        <CardRow tone="off" prose name="Not answering: archive. Items from these may be missing." />
      </Card>,
    )

    // jsdom applies no media query, so the test reads the rule each part carries: one line with an ellipsis from the
    // phone breakpoint up, wrapped lines that break anywhere (an identifier has no spaces) below it.
    const phoneWraps = ['truncate', 'max-md:whitespace-normal', 'max-md:wrap-anywhere']
    for (const text of ['Billing.Orders.OrderPlacedConsumer', '37 messages failed for good', 'Not answering: archive. Items from these may be missing.']) {
      expect(phoneWraps.filter((rule) => screen.getByText(text).classList.contains(rule))).toEqual(phoneWraps)
    }
  })

  it('says a plain empty message under its icon, and shows a block in the empty place as it is', () => {
    render(
      <Cards>
        <Card title="Needs attention" emptyIcon={RunsIcon} empty="Nothing needs attention.">
          {[]}
        </Card>
        <Card title="Processing now" emptyIcon={RunsIcon} empty={<p role="alert">The store is down.</p>}>
          {[]}
        </Card>
      </Cards>,
    )

    const empty = screen.getByRole('region', { name: 'Needs attention' })
    expect(within(empty).getByText('Nothing needs attention.')).toBeTruthy()
    expect(empty.querySelectorAll('svg')).toHaveLength(1)
    // A failed load in the empty place is not an empty state, so it takes no empty icon.
    const failed = screen.getByRole('region', { name: 'Processing now' })
    expect(within(failed).getByRole('alert').textContent).toBe('The store is down.')
    expect(failed.querySelectorAll('svg')).toHaveLength(0)
  })

  it('renders a row with no name as its second line alone', () => {
    render(
      <Card title="Runs by status">
        <CardRow>All stored runs</CardRow>
      </Card>,
    )

    const row = screen.getByRole('listitem')
    expect(row.textContent).toBe('All stored runs')
    expect(row.children).toHaveLength(1)
  })

  it('shows its empty text when it holds no row', () => {
    render(<Card title="Next recurring runs" empty="No recurring jobs yet.">{[]}</Card>)

    expect(screen.queryByRole('list')).toBeNull()
    expect(screen.getByText('No recurring jobs yet.')).toBeTruthy()
  })
})

describe('ExceptionBlock without a served type', () => {
  it('renders the message and its frames with no type line', () => {
    render(<ExceptionBlock message="Card declined" stack="at Billing.Charge()" />)

    expect(screen.getByText(/Card declined/).closest('pre')?.textContent).toBe('Card declined\n   at Billing.Charge()')
    expect(document.querySelector('.hd-exception b')).toBeNull()
  })
})

describe('Card state', () => {
  it('shows a failed load as an alert with Retry in place of the rows, never as an empty card', async () => {
    const user = userEvent.setup()
    const onRetry = vi.fn()
    render(
      <Card title="Processing now" empty="Nothing is running." state={{ status: 'error', message: 'The runs did not load (503).', onRetry }}>
        <CardRow name="stale.row" />
      </Card>,
    )

    const card = screen.getByRole('region', { name: 'Processing now' })
    expect(within(card).getByRole('alert').textContent).toBe('The runs did not load (503).')
    expect(within(card).queryByText('Nothing is running.')).toBeNull()
    expect(within(card).queryByRole('listitem')).toBeNull()

    await user.click(within(card).getByRole('button', { name: 'Retry' }))
    expect(onRetry).toHaveBeenCalledOnce()
  })

  it('marks the card busy and names what loads while it loads', () => {
    render(<Card title="Processing now" state={{ status: 'loading', label: 'Loading the runs in progress' }} />)

    const card = screen.getByRole('region', { name: 'Processing now' })
    expect(card.getAttribute('aria-busy')).toBe('true')
    expect(within(card).getByRole('status', { name: 'Loading the runs in progress' })).toBeTruthy()
    expect(within(card).queryByRole('alert')).toBeNull()
  })

  it('says what would appear when it is empty', () => {
    render(<Card title="Processing now" state={{ status: 'empty', message: 'Nothing is running.' }} />)

    const card = screen.getByRole('region', { name: 'Processing now' })
    expect(within(card).getByText('Nothing is running.')).toBeTruthy()
    expect(card.hasAttribute('aria-busy')).toBe(false)
  })
})

