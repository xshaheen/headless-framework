import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { InspectorField, InspectorFields } from '../../src/components/inspector/inspector.tsx'
import { DashboardApiError } from '../../src/data/api.ts'

/** An object with one stored value: the server's save is `save`, and the field shows what was stored. */
function Stored({ initial, save, options, onDraftChange }: {
  initial: string
  save: (value: string) => Promise<void>
  options?: { value: string; label: string }[]
  onDraftChange?: (value: string) => void
}) {
  const [stored, setStored] = useState(initial)

  return (
    <>
      <InspectorFields>
        <InspectorField
          label="Setting"
          edit={{
            value: stored,
            options,
            onDraftChange,
            onCommit: async (value) => {
              await save(value)
              setStored(value)
            },
          }}
        />
      </InspectorFields>
      <p>{`Stored: ${stored}`}</p>
    </>
  )
}

describe('InspectorField', () => {
  it('shows a read-only value', () => {
    render(
      <InspectorFields>
        <InspectorField label="Attempts">3</InspectorField>
      </InspectorFields>,
    )

    expect(screen.getByText('Attempts').nextElementSibling?.textContent).toBe('3')
  })

  it('commits a select on change', async () => {
    const user = userEvent.setup()
    const save = vi.fn(async () => undefined)
    const options = [
      { value: 'low', label: 'Low' },
      { value: 'high', label: 'High' },
    ]
    render(<Stored initial="low" save={save} options={options} />)

    await user.selectOptions(screen.getByRole('combobox', { name: 'Setting' }), 'high')

    expect(save).toHaveBeenCalledExactlyOnceWith('high')
    expect(await screen.findByText('Stored: high')).toBeTruthy()
  })

  it('commits text on Enter, not on each keystroke, and hears each keystroke for a preview', async () => {
    const user = userEvent.setup()
    const save = vi.fn(async () => undefined)
    const onDraftChange = vi.fn()
    render(<Stored initial="" save={save} onDraftChange={onDraftChange} />)

    await user.type(screen.getByRole('textbox', { name: 'Setting' }), '0 * * * *')

    expect(save).not.toHaveBeenCalled()
    expect(onDraftChange).toHaveBeenLastCalledWith('0 * * * *')

    await user.keyboard('{Enter}')

    expect(save).toHaveBeenCalledExactlyOnceWith('0 * * * *')
    expect(await screen.findByText('Stored: 0 * * * *')).toBeTruthy()
  })

  it('shows the server’s message inline and keeps the stored value when it refuses the text', async () => {
    const user = userEvent.setup()
    const save = vi.fn(async () => {
      throw new DashboardApiError(400, 'Not a cron expression: 61 * * * *')
    })
    render(<Stored initial="0 * * * *" save={save} />)

    const field = screen.getByRole('textbox', { name: 'Setting' })
    await user.clear(field)
    await user.type(field, '61 * * * *{Enter}')

    expect((await screen.findByRole('alert')).textContent).toBe('Not a cron expression: 61 * * * *')
    expect(field.getAttribute('aria-invalid')).toBe('true')
    expect((field as HTMLInputElement).value).toBe('61 * * * *')
    expect(screen.getByText('Stored: 0 * * * *')).toBeTruthy()
  })

  it('commits text on blur', async () => {
    const user = userEvent.setup()
    const save = vi.fn(async () => undefined)
    render(<Stored initial="" save={save} />)

    await user.type(screen.getByRole('textbox', { name: 'Setting' }), 'nightly')
    await user.tab()

    expect(save).toHaveBeenCalledExactlyOnceWith('nightly')
  })
})
