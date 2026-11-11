import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { FilterCombobox, type FilterComboboxOption } from '../../src/components/display/filter-combobox.tsx'

// jsdom lays nothing out; the virtualizer needs a viewport to decide which options to render.
beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => 300 })
  Object.defineProperty(HTMLElement.prototype, 'clientHeight', { configurable: true, get: () => 320 })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'clientWidth')
  Reflect.deleteProperty(HTMLElement.prototype, 'clientHeight')
})

const options: FilterComboboxOption[] = [
  ...Array.from({ length: 3 }, (_, at) => ({ value: `billing.f${at}`, label: `billing.f${at}`, section: 'Billing' })),
  ...Array.from({ length: 300 }, (_, at) => ({ value: `reports.r${at}`, label: `reports.r${at}`, section: 'Reports' })),
]

function Held({ onChange }: { onChange: (value: string) => void }) {
  const [value, setValue] = useState('')

  return (
    <FilterCombobox
      label="Function"
      emptyLabel="Any function"
      value={value}
      options={options}
      onChange={(next) => {
        setValue(next)
        onChange(next)
      }}
    />
  )
}

describe('FilterCombobox', () => {
  it('narrows the list as the operator types and picks from the keyboard alone', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Held onChange={onChange} />)

    await user.tab()
    const input = screen.getByRole('combobox', { name: 'Function' })
    expect(document.activeElement).toBe(input)

    await user.keyboard('r29')
    const list = await screen.findByRole('listbox')
    await waitFor(() =>
      expect(within(list).getAllByRole('option').map((option) => option.textContent)).toEqual([
        'reports.r29',
        'reports.r290',
        'reports.r291',
        'reports.r292',
        'reports.r293',
        'reports.r294',
        'reports.r295',
        'reports.r296',
        'reports.r297',
        'reports.r298',
        'reports.r299',
      ]),
    )

    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}')

    expect(onChange).toHaveBeenLastCalledWith('reports.r290')
    expect((input as HTMLInputElement).value).toBe('reports.r290')
  })

  it('renders only the options in view and gives each its place in the whole list, across sections', async () => {
    const user = userEvent.setup()
    render(<Held onChange={() => undefined} />)

    await user.click(screen.getByRole('button'))
    const list = await screen.findByRole('listbox')
    const shown = within(list).getAllByRole('option')

    // The empty choice leads, then three Billing options, then the Reports section's first.
    expect(shown.length).toBeLessThan(40)
    expect(within(list).getAllByRole('group').map((group) => group.getAttribute('aria-labelledby') !== null)).toContain(true)
    const firstReport = within(list).getByRole('option', { name: 'reports.r0' })
    expect(firstReport.getAttribute('aria-posinset')).toBe('5')
    expect(firstReport.getAttribute('aria-setsize')).toBe(String(options.length + 1))
  })

  it('turns the filter off through its empty choice', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Held onChange={onChange} />)

    await user.click(screen.getByRole('button'))
    await user.click(await screen.findByRole('option', { name: 'billing.f1' }))
    expect(onChange).toHaveBeenLastCalledWith('billing.f1')

    await user.click(screen.getByRole('button'))
    await user.click(await screen.findByRole('option', { name: 'Any function' }))
    expect(onChange).toHaveBeenLastCalledWith('')
  })
})
