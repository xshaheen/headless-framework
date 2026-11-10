import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { createCommandsApi, type ArgumentSchema } from '../../src/commands/api.ts'
import { checkArguments, SchemaForm } from '../../src/commands/schema-form.tsx'
import type { ApiRequest } from '../../src/data/api.ts'
import { LocaleProvider } from '../../src/locale/locale.tsx'

const duplicate: ArgumentSchema = {
  type: 'object',
  required: ['runAt'],
  properties: {
    runAt: { type: 'string', title: 'Run at', format: 'date-time' },
    request: { type: 'string', title: 'Request', contentMediaType: 'application/json', maxLength: 40 },
  },
}

describe('date-time and JSON arguments', () => {
  it('submits a date and time typed in Cairo as the matching UTC instant', async () => {
    localStorage.setItem('headless-dashboard:time-zone', 'Africa/Cairo')
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(
      <LocaleProvider>
        <SchemaForm schema={duplicate} submitLabel="Duplicate run" onSubmit={onSubmit} />
      </LocaleProvider>,
    )

    expect(await screen.findByText('In Africa/Cairo')).toBeTruthy()
    await user.click(screen.getByRole('spinbutton', { name: /month/i }))
    await user.keyboard('12152026')
    await user.keyboard('1000')
    await user.click(screen.getByRole('button', { name: 'Duplicate run' }))

    // Cairo is two hours ahead of UTC in December 2026, so a runner in any other zone would send another instant.
    expect(onSubmit).toHaveBeenCalledWith({ runAt: '2026-12-15T08:00:00.000Z' })
  })

  it.each([
    ['an optional', []],
    ['a required', ['runAt']],
  ])('refuses %s date-time typed without its time instead of treating it as not given', async (_, required) => {
    localStorage.setItem('headless-dashboard:time-zone', 'UTC')
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(
      <LocaleProvider>
        <SchemaForm schema={{ ...duplicate, required }} submitLabel="Duplicate run" onSubmit={onSubmit} />
      </LocaleProvider>,
    )

    await user.click(await screen.findByRole('spinbutton', { name: /month/i }))
    await user.keyboard('12152026')
    await user.click(screen.getByRole('button', { name: 'Duplicate run' }))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('Run at must be a complete date and time.')).toBeTruthy()
    expect(screen.queryByText('Run at is required.')).toBeNull()
  })

  it('blocks JSON that does not parse and colours JSON that does', async () => {
    localStorage.setItem('headless-dashboard:time-zone', 'UTC')
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(
      <LocaleProvider>
        <SchemaForm schema={{ ...duplicate, required: [] }} submitLabel="Duplicate run" onSubmit={onSubmit} />
      </LocaleProvider>,
    )
    const request = screen.getByRole('textbox', { name: /Request/ })

    // JSON is code: monospace and never spell-checked.
    expect(request.getAttribute('spellcheck')).toBe('false')
    expect(request.className).toContain('hd-mono')

    await user.type(request, '{{"id":')
    await user.click(screen.getByRole('button', { name: 'Duplicate run' }))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('Request must be valid JSON.')).toBeTruthy()

    await user.type(request, '1}')
    await user.click(screen.getByRole('button', { name: 'Duplicate run' }))

    expect(screen.getByRole('figure')).toBeTruthy()
    expect(onSubmit).toHaveBeenCalledWith({ request: '{"id":1}' })
  })

  it('refuses a date-time in the past when the argument asks for one not in the past, and JSON over its max length', () => {
    const schema: ArgumentSchema = {
      type: 'object',
      required: [],
      properties: { ...duplicate.properties, runAt: { ...duplicate.properties.runAt!, notBeforeNow: true } },
    }

    expect(checkArguments(schema, { runAt: '2000-01-01T00:00:00.000Z', request: `[${'1,'.repeat(30)}1]` })).toEqual({
      ok: false,
      errors: { runAt: 'Run at must not be in the past.', request: 'Request must be at most 40 characters.' },
    })
  })
})

describe('command client', () => {
  it('passes a row result created object through and leaves it absent on a row without one', async () => {
    const created = { module: 'jobs', kind: 'run', id: 'run-2' }
    const request = vi.fn<(request: ApiRequest) => Promise<unknown>>(async () => ({
      rows: [
        { id: 'run-1', outcome: 'done', created },
        { id: 'run-3', outcome: 'done' },
      ],
    }))
    const commands = createCommandsApi({ request: request as <T>(call: ApiRequest) => Promise<T> }, 'jobs')

    const rows = await commands.run('jobs.duplicate', { ids: ['run-1', 'run-3'], arguments: { runAt: '2026-12-15T08:00:00.000Z' } })

    expect(rows[0]!.created).toEqual(created)
    expect(rows[1]!.created).toBeUndefined()
    expect(request).toHaveBeenCalledWith(expect.objectContaining({ method: 'POST', path: 'jobs/commands/jobs.duplicate' }))
  })
})
