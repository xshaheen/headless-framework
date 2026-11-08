import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { ExceptionBlock } from '../../src/components/frame/exception-block.tsx'
import { FieldGrid, GridField } from '../../src/components/frame/field-grid.tsx'
import { HistoryList } from '../../src/components/frame/history-list.tsx'

function Tenant({ save }: { save: (value: string) => Promise<void> }) {
  const [stored, setStored] = useState('acme')

  return (
    <FieldGrid footnote="Counted since the last restart.">
      <GridField label="Tries">3 of 5</GridField>
      <GridField label="Correlation" span={2} action={<a href="/trace/7f2e">Open trace</a>}>
        7f2e8c04
      </GridField>
      <GridField
        label="Tenant"
        edit={{
          value: stored,
          onCommit: async (value) => {
            await save(value)
            setStored(value)
          },
        }}
      />
    </FieldGrid>
  )
}

describe('FieldGrid', () => {
  it('lays out labels over values, a long value across two columns, a mini action, and a footnote', () => {
    render(<Tenant save={vi.fn(async () => undefined)} />)

    expect(screen.getByText('Tries').nextElementSibling?.textContent).toBe('3 of 5')
    expect(screen.getByText('Correlation').parentElement?.className).toBe('hd-span-2')
    expect(screen.getByRole('link', { name: 'Open trace' })).toBeTruthy()
    expect(screen.getByText('Counted since the last restart.')).toBeTruthy()
  })

  it('commits an edited value on Enter, and shows a refusal where the operator typed', async () => {
    const user = userEvent.setup()
    const save = vi.fn(async (value: string) => {
      if (value === 'bad') {
        throw new Error('Tenant bad does not exist.')
      }
    })
    render(<Tenant save={save} />)

    const field = screen.getByRole('textbox', { name: 'Tenant' })
    await user.clear(field)
    await user.type(field, 'globex{Enter}')

    expect(save).toHaveBeenCalledExactlyOnceWith('globex')

    await user.clear(field)
    await user.type(field, 'bad{Enter}')

    expect(screen.getByRole('alert').textContent).toBe('Tenant bad does not exist.')
    expect(field.getAttribute('aria-invalid')).toBe('true')
  })
})

describe('HistoryList', () => {
  it('lists each event with its time, state, and note under its heading', () => {
    render(
      <HistoryList
        label="History"
        items={[
          { time: '14:20:01', tone: 'enq', state: 'Queued' },
          { time: '14:20:03', tone: 'fail', state: 'Failed', note: 'attempt 3 of 3' },
        ]}
      />,
    )

    const rows = screen.getAllByRole('listitem')
    expect(screen.getByRole('region', { name: 'History' })).toBeTruthy()
    expect(rows.map((row) => row.textContent)).toEqual(['14:20:01Queued', '14:20:03Failed attempt 3 of 3'])
  })
})

describe('ExceptionBlock', () => {
  const stack = ['at Billing.Charge()', 'at Billing.Run()', 'at Jobs.Runner.Run()', 'at Jobs.Host.Loop()'].join('\n')

  it('shows the first lines until Full stack, and copies the whole exception', async () => {
    const user = userEvent.setup()
    const writeText = vi.fn(async () => undefined)
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } })
    render(<ExceptionBlock type="System.TimeoutException" message="The charge timed out." stack={stack} />)

    expect(screen.queryByText(/Jobs\.Host\.Loop/)).toBeNull()

    await user.click(screen.getByRole('button', { name: 'Full stack' }))

    expect(screen.getByRole('button', { name: 'Full stack' }).getAttribute('aria-expanded')).toBe('true')
    expect(screen.getByText(/Jobs\.Host\.Loop/)).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Copy exception' }))

    expect(writeText).toHaveBeenCalledExactlyOnceWith(
      'System.TimeoutException: The charge timed out.\n   at Billing.Charge()\n   at Billing.Run()\n   at Jobs.Runner.Run()\n   at Jobs.Host.Loop()',
    )
    vi.unstubAllGlobals()
  })

  it('offers no Full stack when every line already shows', () => {
    render(
      <ExceptionBlock
        type="System.InvalidOperationException"
        message="No handler."
        stack={'at Billing.Charge()\nat Billing.Run()'}
      />,
    )

    expect(screen.queryByRole('button', { name: 'Full stack' })).toBeNull()
  })

  it('shows an inner exception as its own line, not as a frame', () => {
    const nested = [
      'at Billing.Charge()',
      'TimeoutException: The gateway did not answer.',
      'at Gateway.Send()',
      '--- End of inner exception stack trace ---',
    ].join('\n')
    render(<ExceptionBlock type="System.InvalidOperationException" message="Charge failed." stack={nested} />)

    expect(screen.getByText(/Charge failed/).closest('pre')?.textContent).toBe(
      'System.InvalidOperationException: Charge failed.\n   at Billing.Charge()\nTimeoutException: The gateway did not answer.\n   at Gateway.Send()',
    )
  })
})
