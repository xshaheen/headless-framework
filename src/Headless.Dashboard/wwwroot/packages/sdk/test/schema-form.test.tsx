import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { ArgumentSchema } from '../src/commands/api.ts'
import { checkArguments, SchemaForm } from '../src/commands/schema-form.tsx'

const schema: ArgumentSchema = {
  type: 'object',
  required: ['note', 'priority'],
  properties: {
    note: { type: 'string', title: 'Note', maxLength: 20 },
    attempts: { type: 'integer', title: 'Attempts', minimum: 1, maximum: 5 },
    priority: { type: 'string', title: 'Priority', enum: ['low', 'high'] },
    notify: { type: 'boolean', title: 'Notify the owner' },
  },
}

describe('schema form', () => {
  it('renders a field for string, number, enum, and boolean arguments', () => {
    render(<SchemaForm schema={schema} submitLabel="Requeue run" onSubmit={vi.fn()} />)

    expect(screen.getByRole('textbox', { name: /Note/ })).toBeTruthy()
    expect(screen.getByRole('textbox', { name: /Attempts/ }).getAttribute('inputmode')).toBe('numeric')
    expect(screen.getByRole('combobox', { name: 'Priority' })).toBeTruthy()
    expect(screen.getByRole('checkbox', { name: 'Notify the owner' })).toBeTruthy()
  })

  it('blocks submit on a violation and shows it on the field', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(<SchemaForm schema={schema} submitLabel="Requeue run" onSubmit={onSubmit} />)

    await user.type(screen.getByRole('textbox', { name: /Attempts/ }), '9')
    await user.click(screen.getByRole('button', { name: 'Requeue run' }))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('Note is required.')).toBeTruthy()
    expect(screen.getByText('Attempts must be at most 5.')).toBeTruthy()
    expect(screen.getByText('Priority is required.')).toBeTruthy()
  })

  it('submits typed values once every rule holds', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(<SchemaForm schema={schema} submitLabel="Requeue run" onSubmit={onSubmit} />)

    await user.type(screen.getByRole('textbox', { name: /Note/ }), 'retry after fix')
    await user.type(screen.getByRole('textbox', { name: /Attempts/ }), '3')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Priority' }), 'high')
    await user.click(screen.getByRole('checkbox', { name: 'Notify the owner' }))
    await user.click(screen.getByRole('button', { name: 'Requeue run' }))

    expect(onSubmit).toHaveBeenCalledWith({ note: 'retry after fix', attempts: 3, priority: 'high', notify: true })
  })

  it('refuses a fractional whole number and an enum value the schema does not list', () => {
    const result = checkArguments(schema, { note: 'x', attempts: '2.5', priority: 'urgent' })

    expect(result).toEqual({
      ok: false,
      errors: { attempts: 'Attempts must be a whole number.', priority: 'Choose one of the listed values for Priority.' },
    })
  })
})
