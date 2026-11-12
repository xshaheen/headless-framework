import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { EmptyState } from '../../src/components/display/empty-state.tsx'
import { QueryState } from '../../src/components/display/query-state.tsx'
import { Section } from '../../src/components/display/section.tsx'
import { SegmentedControl } from '../../src/components/display/segmented-control.tsx'
import { Skeleton } from '../../src/components/display/skeleton.tsx'
import { FilterSelect } from '../../src/components/display/filter-select.tsx'
import { FilterToolbar } from '../../src/components/display/filter-toolbar.tsx'
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

describe('QueryState', () => {
  const rows = (data: string[]) => (
    <ul>
      {data.map((item) => (
        <li key={item}>{item}</li>
      ))}
    </ul>
  )

  it('draws its data once the read answers', () => {
    render(
      <QueryState query={{ isPending: false, isError: false, data: ['a.run'] }} label="Loading runs" rows={2}>
        {rows}
      </QueryState>,
    )

    expect(screen.getByText('a.run')).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('shows only the error and Retry when the first read fails', async () => {
    const refetch = vi.fn(() => Promise.resolve())
    render(
      <QueryState
        query={{ isPending: false, isError: true, error: new Error('The store is down'), refetch }}
        label="Loading runs"
        rows={2}
      >
        {rows}
      </QueryState>,
    )

    expect(screen.getByRole('alert').textContent).toContain('The store is down')
    expect(screen.queryByRole('list')).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(refetch).toHaveBeenCalledOnce()
  })

  it('keeps the last rows under the error when a refresh fails, and says they are from the last read', () => {
    render(
      <QueryState
        query={{
          isPending: false,
          isError: true,
          error: new Error('The store is down'),
          data: ['a.run'],
          refetch: () => Promise.resolve(),
        }}
        label="Loading runs"
        rows={2}
      >
        {rows}
      </QueryState>,
    )

    expect(screen.getByText('a.run')).toBeTruthy()
    expect(screen.getByText(/from the last successful read/)).toBeTruthy()
  })
})

describe('Skeleton', () => {
  it('is one busy status region whatever shape it draws', () => {
    const { rerender } = render(<Skeleton label="Loading the run" shape="fields" rows={2} />)
    const status = screen.getByRole('status', { name: 'Loading the run' })
    expect(status.getAttribute('aria-busy')).toBe('true')
    expect(status.textContent).toBe('')

    for (const shape of ['rows', 'table', 'lines', 'chart'] as const) {
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

describe('FilterSelect', () => {
  it('reads its empty choice while off, lists it with the options, and reports the chosen value', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(
      <FilterSelect
        label="Kind"
        emptyLabel="Any kind"
        value=""
        onChange={onChange}
        options={[{ value: 'TimeJob', label: 'Time jobs' }]}
      />,
    )

    const chip = screen.getByRole('button', { name: /Kind/ })
    expect(chip.textContent).toBe('Any kind')
    await user.click(chip)
    const list = await screen.findByRole('listbox')
    expect(within(list).getAllByRole('option').map((option) => option.textContent)).toEqual(['Any kind', 'Time jobs'])

    await user.click(within(list).getByRole('option', { name: 'Time jobs' }))
    expect(onChange).toHaveBeenCalledWith('TimeJob')
  })

  it('reads the filter and its value once set, and the empty choice turns it off', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(
      <FilterSelect
        label="Kind"
        emptyLabel="Any kind"
        value="TimeJob"
        onChange={onChange}
        options={[{ value: 'TimeJob', label: 'Time jobs' }]}
      />,
    )

    const chip = screen.getByRole('button', { name: /Kind/ })
    expect(chip.textContent).toBe('KindTime jobs')
    expect(chip.hasAttribute('data-set')).toBe(true)
    await user.click(chip)
    await user.click(await screen.findByRole('option', { name: 'Any kind' }))
    expect(onChange).toHaveBeenCalledWith('')
  })

  it('opens from the keyboard, and Esc closes the list back to the chip', async () => {
    const user = userEvent.setup()
    render(
      <FilterSelect
        label="Kind"
        emptyLabel="Any kind"
        value=""
        onChange={() => undefined}
        options={[{ value: 'TimeJob', label: 'Time jobs' }]}
      />,
    )
    const chip = screen.getByRole('button', { name: /Kind/ })

    chip.focus()
    await user.keyboard('{Enter}')
    expect(await screen.findByRole('listbox')).toBeTruthy()

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('listbox')).toBeNull()
    await waitFor(() => expect(document.activeElement).toBe(chip))
  })

  it('offers no empty choice and no set state for a required choice such as the node', async () => {
    const user = userEvent.setup()
    render(
      <FilterSelect
        label="Node"
        isRequired
        value="node-a"
        onChange={() => undefined}
        options={[
          { value: 'node-a', label: 'node-a (this node)' },
          { value: 'node-b', label: 'node-b' },
        ]}
      />,
    )

    const chip = screen.getByRole('button', { name: /Node/ })
    expect(chip.textContent).toBe('node-a (this node)')
    expect(chip.hasAttribute('data-set')).toBe(false)
    await user.click(chip)
    expect((await screen.findAllByRole('option')).map((option) => option.textContent)).toEqual(['node-a (this node)', 'node-b'])
  })
})

describe('FilterToolbar', () => {
  afterEach(() => {
    window.matchMedia = undefined as unknown as typeof window.matchMedia
  })

  it('lays the search and the filters on the row, with the more filters behind a button', async () => {
    const user = userEvent.setup()
    render(
      <FilterToolbar chips={<span>All 9</span>} search={<SearchField label="Search runs" compact onSearch={() => undefined} />} more={<span>Due between</span>} active={2}>
        <span>Any kind</span>
      </FilterToolbar>,
    )

    const group = screen.getByRole('group', { name: 'Filters' })
    expect(group.firstElementChild?.textContent).toBe('All 9')
    expect(within(group).getByRole('searchbox', { name: 'Search runs' })).toBeTruthy()
    expect(within(group).getByText('Any kind')).toBeTruthy()
    expect(screen.queryByText('Due between')).toBeNull()

    await user.click(within(group).getByRole('button', { name: 'More filters 2' }))
    expect(await screen.findByText('Due between')).toBeTruthy()
  })

  it('puts every filter behind one Filters button on a phone', async () => {
    window.matchMedia = ((query: string) => ({ matches: query === '(max-width: 767px)', addEventListener() {}, removeEventListener() {} })) as unknown as typeof window.matchMedia
    const user = userEvent.setup()
    render(
      <FilterToolbar search={<SearchField label="Search runs" compact onSearch={() => undefined} />} more={<span>Due between</span>} active={1}>
        <span>Any kind</span>
      </FilterToolbar>,
    )

    expect(screen.getByRole('searchbox', { name: 'Search runs' })).toBeTruthy()
    expect(screen.queryByText('Any kind')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Filters 1' }))
    const dialog = await screen.findByRole('dialog', { name: 'Filters' })
    expect(within(dialog).getByText('Any kind')).toBeTruthy()
    expect(within(dialog).getByText('Due between')).toBeTruthy()
  })
})

describe('compact fields', () => {
  it('searches from a compact search field the same as from the full one', async () => {
    const user = userEvent.setup()
    const onSearch = vi.fn()
    render(<SearchField label="Search runs" compact mode="submit" onSearch={onSearch} />)

    await user.type(screen.getByRole('searchbox', { name: 'Search runs' }), 'billing{Enter}')
    expect(onSearch).toHaveBeenCalledWith('billing')
  })
})
