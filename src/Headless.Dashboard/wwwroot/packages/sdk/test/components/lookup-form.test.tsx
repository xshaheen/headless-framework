import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState, type FormEvent } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { LookupForm } from '../../src/components/display/lookup-form.tsx'
import { TextInputField } from '../../src/components/form.tsx'
import { axeViolations } from '../../src/testing/axe.ts'

function KeyLookup({ isPending = false, onSubmit }: { isPending?: boolean; onSubmit: (key: string) => void }) {
  const [key, setKey] = useState('')

  return (
    <LookupForm
      title="Check a key"
      isPending={isPending}
      onSubmit={(event: FormEvent<HTMLFormElement>) => {
        expect(event.defaultPrevented).toBe(true)
        onSubmit(key)
      }}
    >
      <TextInputField label="Key" value={key} onChange={setKey} />
    </LookupForm>
  )
}

describe('LookupForm', () => {
  it('submits the typed value once on Enter, without leaving the page, from a search form in a named region', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    const location = window.location.href
    render(<KeyLookup onSubmit={onSubmit} />)

    const region = screen.getByRole('region', { name: 'Check a key' })
    expect(within(region).getByRole('heading', { name: 'Check a key' })).toBeTruthy()
    const form = within(region).getByRole('search', { name: 'Check a key' })
    await user.type(within(form).getByRole('textbox', { name: 'Key' }), 'order-42{Enter}')

    expect(onSubmit.mock.calls).toEqual([['order-42']])
    expect(window.location.href).toBe(location)
  })

  it('submits on Look up, and while a lookup is pending neither the button nor Enter submits again', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    const { rerender } = render(<KeyLookup onSubmit={onSubmit} />)

    await user.click(screen.getByRole('button', { name: 'Look up' }))
    expect(onSubmit).toHaveBeenCalledTimes(1)

    rerender(<KeyLookup isPending onSubmit={onSubmit} />)
    await user.click(screen.getByRole('button', { name: /Look up/ }))
    await user.type(screen.getByRole('textbox', { name: 'Key' }), 'x{Enter}')

    expect(onSubmit).toHaveBeenCalledTimes(1)
  })

  it('shows the description, hint, and result only when given, and passes axe', async () => {
    const { container, rerender } = render(
      <LookupForm title="Check a key" onSubmit={() => undefined}>
        <TextInputField label="Key" value="" onChange={() => undefined} />
      </LookupForm>,
    )
    const region = screen.getByRole('region', { name: 'Check a key' })
    expect(region.textContent).toBe('Check a keyKeyLook up')

    rerender(
      <LookupForm
        title="Check a key"
        description="Reads the key's current claim."
        hint="Keys are case-sensitive."
        result={<p>Claimed by node-a</p>}
        submitLabel="Check"
        onSubmit={() => undefined}
      >
        <TextInputField label="Key" value="" onChange={() => undefined} />
      </LookupForm>,
    )
    expect(region.textContent).toBe("Check a keyReads the key's current claim.KeyCheckKeys are case-sensitive.Claimed by node-a")
    expect(await axeViolations(container)).toEqual([])
  })
})
