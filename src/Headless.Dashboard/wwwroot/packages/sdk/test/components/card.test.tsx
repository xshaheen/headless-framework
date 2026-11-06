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

  it('shows its empty text when it holds no row', () => {
    render(<Card title="Next recurring runs" empty="No recurring jobs yet.">{[]}</Card>)

    expect(screen.queryByRole('list')).toBeNull()
    expect(screen.getByText('No recurring jobs yet.')).toBeTruthy()
  })
})

describe('ExceptionBlock without a served type', () => {
  it('renders the message and its frames with no type line', () => {
    render(<ExceptionBlock message="Card declined" frames={['Billing.Charge()']} />)

    expect(screen.getByText(/Card declined/).closest('pre')?.textContent).toBe('Card declined\n   at Billing.Charge()')
    expect(document.querySelector('.hd-exception b')).toBeNull()
  })
})
