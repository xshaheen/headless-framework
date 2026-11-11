import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { Button } from '../../src/components/button.tsx'
import { KeyValueTable } from '../../src/components/display/key-value-table.tsx'
import { DeleteIcon, EditIcon } from '../../src/icons/index.ts'

describe('icon actions', () => {
  it('names an icon-only action and shows its name in a tooltip on keyboard focus', async () => {
    const user = userEvent.setup()
    render(<Button icon={EditIcon} aria-label="Edit" />)

    const button = screen.getByRole('button', { name: 'Edit' })
    expect(screen.queryByRole('tooltip')).toBeNull()

    await user.tab()
    expect(document.activeElement).toBe(button)
    expect((await screen.findByRole('tooltip')).textContent).toBe('Edit')
  })

  it('keeps a disabled icon action focusable and says why in its description and its tooltip', async () => {
    const user = userEvent.setup()
    render(<Button icon={DeleteIcon} aria-label="Delete" disabledReason="Needs operate permission" />)

    const button = screen.getByRole('button', { name: 'Delete' })
    expect(button.getAttribute('aria-disabled')).toBe('true')
    expect(document.getElementById(button.getAttribute('aria-describedby')!)?.textContent).toBe('Needs operate permission')

    await user.tab()
    expect((await screen.findByRole('tooltip')).textContent).toBe('DeleteNeeds operate permission')
  })

  it('shows a shorter tooltip when the label carries an id', async () => {
    const user = userEvent.setup()
    render(<Button icon={EditIcon} aria-label="Copy id 01a1c7dd" tooltip="Copy id" />)

    expect(screen.getByRole('button', { name: 'Copy id 01a1c7dd' })).toBeTruthy()
    await user.tab()
    expect((await screen.findByRole('tooltip')).textContent).toBe('Copy id')
  })
})

describe('KeyValueTable', () => {
  it('lays names and values out as rows with a copy control per stored value', async () => {
    const user = userEvent.setup()
    const copied: string[] = []
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: (text: string) => Promise.resolve(copied.push(text)) },
    })
    render(
      <KeyValueTable
        label="Headers"
        rows={[
          { name: 'headless-msg-id', value: '01a1c7cb' },
          { name: 'headless-tenant', value: null },
        ]}
      />,
    )

    const table = screen.getByRole('table', { name: 'Headers' })
    expect(screen.getByRole('rowheader', { name: 'headless-msg-id' }).nextElementSibling?.textContent).toBe('01a1c7cb')
    expect(screen.getByRole('rowheader', { name: 'headless-tenant' }).nextElementSibling?.textContent).toBe('No value')
    expect(screen.getAllByRole('button', { name: /^Copy / }).map((button) => button.getAttribute('aria-label'))).toEqual([
      'Copy headless-msg-id',
    ])
    expect(table.querySelectorAll('tbody tr')).toHaveLength(2)

    await user.click(screen.getByRole('button', { name: 'Copy headless-msg-id' }))
    expect(copied).toEqual(['01a1c7cb'])
  })
})
