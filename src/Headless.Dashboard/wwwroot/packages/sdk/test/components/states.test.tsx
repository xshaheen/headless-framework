import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { EmptyState } from '../../src/components/display/empty-state.tsx'
import { Section } from '../../src/components/display/section.tsx'
import { SegmentedControl } from '../../src/components/display/segmented-control.tsx'
import { Skeleton } from '../../src/components/display/skeleton.tsx'
import { SelectField } from '../../src/components/form.tsx'
import { SearchField } from '../../src/components/table/search-field.tsx'
import { RunsIcon } from '../../src/icons/index.ts'

describe('EmptyState', () => {
  it('reads its title, explanation, and next step, and names a gone object as an alert', () => {
    render(
      <EmptyState role="alert" placement="page" icon={RunsIcon} title="This run no longer exists." action={<a href="/jobs/runs">Back to runs</a>}>
        It was deleted.
      </EmptyState>,
    )

    const alert = screen.getByRole('alert')
    expect(alert.textContent).toBe('This run no longer exists.It was deleted.Back to runs')
    expect(within(alert).getByRole('link', { name: 'Back to runs' })).toBeTruthy()
    expect(alert.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true')
  })

  it('is plain text with no role when it only says a list is empty', () => {
    render(<EmptyState>No runs yet.</EmptyState>)

    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.queryByRole('status')).toBeNull()
    expect(screen.getByText('No runs yet.')).toBeTruthy()
  })
})

describe('Skeleton', () => {
  it('is one busy status region whatever shape it draws', () => {
    const { rerender } = render(<Skeleton label="Loading the run" shape="fields" rows={2} />)
    const status = screen.getByRole('status', { name: 'Loading the run' })
    expect(status.getAttribute('aria-busy')).toBe('true')
    expect(status.textContent).toBe('')

    for (const shape of ['rows', 'lines', 'chart'] as const) {
      rerender(<Skeleton label="Loading the run" shape={shape} />)
      expect(screen.getByRole('status', { name: 'Loading the run' }).getAttribute('aria-busy')).toBe('true')
    }
  })
})

describe('Section', () => {
  it('names its region by its heading and shows its caption and action', () => {
    render(
      <Section label="Circuit breakers" caption="2" action={<a href="#all">All</a>}>
        <p>rows</p>
      </Section>,
    )

    const section = screen.getByRole('region', { name: 'Circuit breakers' })
    expect(within(section).getByRole('heading', { level: 2, name: 'Circuit breakers' })).toBeTruthy()
    expect(within(section).getByText('2')).toBeTruthy()
    expect(within(section).getByRole('link', { name: 'All' })).toBeTruthy()
    expect(within(section).getByText('rows')).toBeTruthy()
  })
})

describe('SegmentedControl', () => {
  it('marks the chosen option pressed and reports a press on another', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(
      <SegmentedControl
        label="Days shown"
        options={[
          { value: '7', label: '7 days' },
          { value: '30', label: '30 days' },
        ]}
        value="7"
        onChange={onChange}
      />,
    )

    const group = screen.getByRole('group', { name: 'Days shown' })
    expect(within(group).getByRole('button', { name: '7 days' }).getAttribute('aria-pressed')).toBe('true')
    expect(within(group).getByRole('button', { name: '30 days' }).getAttribute('aria-pressed')).toBe('false')

    await user.click(within(group).getByRole('button', { name: '30 days' }))
    expect(onChange).toHaveBeenCalledWith('30')
  })
})

describe('compact fields', () => {
  it('keeps a compact select labelled for assistive technology while its label is not drawn', () => {
    render(
      <SelectField
        label="Kind"
        emptyLabel="Any kind"
        compact
        value=""
        onChange={() => undefined}
        options={[{ value: 'TimeJob', label: 'Time jobs' }]}
      />,
    )

    const select = screen.getByRole('combobox', { name: 'Kind' })
    expect(within(select).getByRole('option', { name: 'Any kind' })).toBeTruthy()
    expect(screen.getByText('Kind').classList.contains('sr-only')).toBe(true)
  })

  it('searches from a compact search field the same as from the full one', async () => {
    const user = userEvent.setup()
    const onSearch = vi.fn()
    render(<SearchField label="Search runs" compact mode="submit" onSearch={onSearch} />)

    await user.type(screen.getByRole('searchbox', { name: 'Search runs' }), 'billing{Enter}')
    expect(onSearch).toHaveBeenCalledWith('billing')
  })
})
