import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState, type ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { TextInputField } from '../../src/components/form.tsx'
import { useViewKeys } from '../../src/components/keymap.tsx'
import { DataTable, type DataTableProps } from '../../src/components/table/data-table.tsx'
import { DashboardProvider } from '../../src/context.tsx'
import { createDashboardClient } from '../../src/data/client.tsx'
import { createFakeStream } from '../../src/testing/fake-stream.ts'

interface Row {
  id: string
  title: string
}

const rows: Row[] = [
  { id: 'r1', title: 'billing.charge #1' },
  { id: 'r2', title: 'billing.charge #2' },
  { id: 'r3', title: 'billing.charge #3' },
]

function withDashboard(children: ReactNode) {
  return (
    <DashboardProvider
      client={createDashboardClient()}
      environment={{
        basePath: '/headless',
        apiBase: '/headless/api',
        session: { authenticated: true },
        permissions: {},
        stream: createFakeStream(),
        api: { request: vi.fn() },
      }}
    >
      {children}
    </DashboardProvider>
  )
}

/** A view as a module writes one: a filter, the list, and an inspector that shows the selection. */
function ListView({ selectionMode = 'single' }: Pick<DataTableProps<Row>, 'selectionMode'>) {
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set())
  const [filter, setFilter] = useState('')

  useViewKeys({ filter: () => document.querySelector<HTMLInputElement>('input[name="filter"]')?.focus() })

  return (
    <>
      <TextInputField label="Filter" name="filter" value={filter} onChange={setFilter} />
      <DataTable
        label="Runs"
        rows={rows}
        selectionMode={selectionMode}
        selectedIds={selected}
        onSelectionChange={setSelected}
        columns={[{ id: 'title', header: 'Run', cell: (row) => row.title }]}
        emptyState="No runs yet."
      />
      <aside aria-label="Inspector">{selected.size === 0 ? 'Nothing selected' : [...selected].join(', ')}</aside>
    </>
  )
}

function row(name: RegExp) {
  return screen.getByRole('row', { name })
}

function inspector() {
  return screen.getByRole('complementary', { name: 'Inspector' }).textContent
}

describe('data table', () => {
  it('selects the focused row into the inspector on Enter and keeps focus in the list', async () => {
    const user = userEvent.setup()
    render(withDashboard(<ListView />))

    act(() => row(/#2/).focus())
    await user.keyboard('{Enter}')

    expect(row(/#2/).getAttribute('aria-selected')).toBe('true')
    expect(inspector()).toBe('r2')
    expect(document.activeElement).toBe(row(/#2/))
  })

  it('moves the focus and the selection with J and K, and J does nothing at the last row', async () => {
    const user = userEvent.setup()
    render(withDashboard(<ListView />))

    act(() => row(/#1/).focus())
    await user.keyboard('j')
    expect(inspector()).toBe('r2')
    expect(document.activeElement).toBe(row(/#2/))

    await user.keyboard('j')
    expect(inspector()).toBe('r3')

    await user.keyboard('j')
    expect(inspector()).toBe('r3')
    expect(document.activeElement).toBe(row(/#3/))

    await user.keyboard('k')
    expect(inspector()).toBe('r2')
    expect(document.activeElement).toBe(row(/#2/))
  })

  it('toggles the focused row in a multi-selection with X', async () => {
    const user = userEvent.setup()
    render(withDashboard(<ListView selectionMode="multiple" />))

    await user.click(row(/#1/))
    act(() => row(/#3/).focus())
    await user.keyboard('x')
    expect(inspector()).toBe('r1, r3')

    await user.keyboard('x')
    expect(inspector()).toBe('r1')
  })

  it('focuses the filter on /, where J types a letter and moves no row', async () => {
    const user = userEvent.setup()
    render(withDashboard(<ListView />))

    await user.click(row(/#1/))
    await user.keyboard('/')
    const filter = screen.getByRole('textbox', { name: 'Filter' })
    expect(document.activeElement).toBe(filter)

    await user.keyboard('j')

    expect(filter).toHaveProperty('value', 'j')
    expect(inspector()).toBe('r1')
  })

  it('clears the selection on Esc', async () => {
    const user = userEvent.setup()
    render(withDashboard(<ListView />))

    await user.click(row(/#2/))
    await user.keyboard('{Escape}')

    expect(row(/#2/).getAttribute('aria-selected')).toBe('false')
    expect(inspector()).toBe('Nothing selected')
  })

  it('says what will appear when empty', () => {
    render(<DataTable label="Runs" rows={[]} columns={[{ id: 'title', header: 'Run', cell: () => '' }]} emptyState="No runs yet." />)

    expect(screen.getByText('No runs yet.')).toBeTruthy()
  })

  it('offers select all matching once every loaded row is selected', () => {
    render(
      <DataTable
        label="Runs"
        rows={rows}
        columns={[{ id: 'title', header: 'Run', cell: (row) => row.title }]}
        emptyState="No runs yet."
        selectionMode="multiple"
        selectedIds={new Set(['r1', 'r2', 'r3'])}
        matchingCount={312}
        onSelectAllMatching={vi.fn()}
      />,
    )

    expect(screen.getByRole('button', { name: 'Select all 312 matching' })).toBeTruthy()
  })
})
