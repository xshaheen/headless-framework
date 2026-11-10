import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Card, CardRow, Cards } from '../../src/components/display/card.tsx'
import { ExceptionBlock } from '../../src/components/frame/exception-block.tsx'

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

  it('lets an empty card keep its own height while cards with rows share the row’s', () => {
    render(
      <Cards>
        <Card title="Needs attention" empty="Nothing needs attention.">
          {[]}
        </Card>
        <Card title="Processing now">
          <CardRow name="a" />
        </Card>
      </Cards>,
    )

    expect(screen.getByRole('region', { name: 'Needs attention' }).classList.contains('self-start')).toBe(true)
    expect(screen.getByRole('region', { name: 'Processing now' }).classList.contains('self-start')).toBe(false)
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
