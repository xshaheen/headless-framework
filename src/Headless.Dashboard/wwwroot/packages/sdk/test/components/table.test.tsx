import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState, type ReactNode } from 'react'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { TextInputField } from '../../src/components/form.tsx'
import { useViewKeys } from '../../src/components/keymap.tsx'
import { DataTable, type DataTableProps } from '../../src/components/table/data-table.tsx'
import { DashboardProvider } from '../../src/context.tsx'
import { createDashboardClient } from '../../src/data/client.tsx'
import { useDashboardKeysetList } from '../../src/data/keyset.ts'
import type { DashboardListPage } from '../../src/data/patch.ts'
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

interface Run {
  id: string
  status: string
  version: string
}

/** Runs `r{from}` onward, newest first, as a keyset list returns them. */
function runs(count: number, from = 1): Run[] {
  return Array.from({ length: count }, (_, index) => ({ id: `r${from + index}`, status: 'Failed', version: '1' }))
}

const runColumns = [
  { id: 'id', header: 'Run', cell: (run: Run) => run.id, isRowHeader: true },
  { id: 'status', header: 'Status', cell: (run: Run) => run.status },
  { id: 'note', header: 'Note', cell: () => 'note', priority: 2 as const },
]

/** The ids of the row elements in the page: the rows the virtualizer renders, not the rows the list holds. */
function renderedIds() {
  return [...document.querySelectorAll<HTMLElement>('[data-row-id]')].map((element) => element.dataset.rowId)
}

function grid() {
  return screen.getByRole('grid')
}

/** Scrolls the grid as a browser does: the offset changes, then a scroll event follows. */
function scrollTo(top: number) {
  act(() => {
    grid().scrollTop = top
    fireEvent.scroll(grid())
  })
}

describe('virtualized keyset table', () => {
  // jsdom lays nothing out. As React Aria's own tests do, every element reports a fixed client size, so the
  // virtualizer sees a 360 px viewport: 9 rows of 36 px under a 32 px header. Scroll offsets are kept per element.
  const offsets = new WeakMap<Element, number>()
  const observers = new Set<{ callback: IntersectionObserverCallback }>()

  /** Reports the load-more sentinel as in view, as the browser does once the scroll comes near the end. */
  function sentinelInView() {
    act(() => {
      for (const observer of [...observers]) {
        observer.callback([{ isIntersecting: true } as IntersectionObserverEntry], {} as IntersectionObserver)
      }
    })
  }

  class FakeIntersectionObserver {
    private readonly entry: { callback: IntersectionObserverCallback }

    constructor(callback: IntersectionObserverCallback) {
      this.entry = { callback }
      observers.add(this.entry)
    }

    observe() {}

    disconnect() {
      observers.delete(this.entry)
    }
  }

  beforeAll(() => {
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => 1000 })
    Object.defineProperty(HTMLElement.prototype, 'clientHeight', { configurable: true, get: () => 360 })
    Object.defineProperty(HTMLElement.prototype, 'scrollTop', {
      configurable: true,
      get(this: Element) {
        return offsets.get(this) ?? 0
      },
      set(this: Element, value: number) {
        offsets.set(this, value)
      },
    })
  })

  afterAll(() => {
    for (const name of ['clientWidth', 'clientHeight', 'scrollTop']) {
      Reflect.deleteProperty(HTMLElement.prototype, name)
    }
  })

  beforeEach(() => {
    localStorage.clear()
    vi.stubGlobal('IntersectionObserver', FakeIntersectionObserver)
  })

  afterEach(() => {
    localStorage.clear()
    vi.unstubAllGlobals()
  })

  function SelectableRuns({ rows: list }: { rows: Run[] }) {
    const [selected, setSelected] = useState<ReadonlySet<string>>(new Set())

    return (
      <DataTable label="Runs" rows={list} columns={runColumns} selectedIds={selected} onSelectionChange={setSelected} emptyState="No runs yet." />
    )
  }

  it('renders a bounded number of rows from 5,000, and End reaches the last loaded row with focus on it', async () => {
    const user = userEvent.setup()
    render(withDashboard(<SelectableRuns rows={runs(5_000)} />))

    expect(renderedIds().length).toBeGreaterThan(0)
    expect(renderedIds().length).toBeLessThan(30)

    act(() => row(/^r1\b/).focus())
    await user.keyboard('{End}')

    await waitFor(() => expect(document.activeElement).toBe(row(/^r5000\b/)))
    expect(renderedIds().length).toBeLessThan(30)
  })

  it('asks for the next page with the last row keyset cursor once, however often the scroll nears the end', async () => {
    const fetchPage = vi.fn(
      async (cursor: string | undefined): Promise<DashboardListPage<Run>> =>
        cursor === undefined
          ? { items: runs(100), nextCursor: 'cursor-of-r100', count: { value: 10_000, capped: true } }
          : { items: runs(100, 101), nextCursor: null },
    )

    function KeysetRuns() {
      const list = useDashboardKeysetList<Run>({ topic: 'diag.run', params: {}, fetchPage })

      return (
        <DataTable
          label="Runs"
          rows={list.rows}
          columns={runColumns}
          hasMore={list.hasMore}
          isLoadingMore={list.isLoadingMore}
          onLoadMore={list.loadMore}
          emptyState="No runs yet."
        />
      )
    }

    render(withDashboard(<KeysetRuns />))
    await screen.findByRole('row', { name: /^r1\b/ })

    scrollTo(100 * 36 - 360)
    sentinelInView()
    scrollTo(100 * 36 - 300)
    sentinelInView()
    sentinelInView()

    await waitFor(() => expect(fetchPage).toHaveBeenCalledTimes(2))
    expect(fetchPage.mock.calls.map(([cursor]) => cursor)).toEqual([undefined, 'cursor-of-r100'])

    await screen.findByRole('row', { name: /^r101\b/ })
    sentinelInView()
    scrollTo(200 * 36 - 360)
    expect(fetchPage).toHaveBeenCalledTimes(2)
  })

  it('asks for the next page again after it failed and the list was retried', async () => {
    const user = userEvent.setup()
    let failNext = true
    const fetchPage = vi.fn(async (cursor: string | undefined): Promise<DashboardListPage<Run>> => {
      if (cursor === undefined) {
        return { items: runs(100), nextCursor: 'cursor-of-r100' }
      }

      if (failNext) {
        failNext = false
        throw new Error('Page 2 failed')
      }

      return { items: runs(100, 101), nextCursor: null }
    })

    function RetriedRuns() {
      const list = useDashboardKeysetList<Run>({ topic: 'diag.run', params: {}, fetchPage })

      return (
        <>
          {list.query.isError ? (
            <button type="button" onClick={() => void list.query.refetch()}>
              Retry
            </button>
          ) : null}
          <DataTable
            label="Runs"
            rows={list.rows}
            columns={runColumns}
            hasMore={list.hasMore}
            isLoadingMore={list.isLoadingMore}
            onLoadMore={list.loadMore}
            emptyState="No runs yet."
          />
        </>
      )
    }

    render(withDashboard(<RetriedRuns />))
    await screen.findByRole('row', { name: /^r1\b/ })

    scrollTo(100 * 36 - 360)
    sentinelInView()
    await user.click(await screen.findByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull())

    sentinelInView()

    await screen.findByRole('row', { name: /^r101\b/ })
    expect(fetchPage.mock.calls.map(([cursor]) => cursor).filter((cursor) => cursor !== undefined)).toEqual([
      'cursor-of-r100',
      'cursor-of-r100',
    ])
  })

  it('holds the rows in view when three rows arrive above them, and shows 3 new', async () => {
    const user = userEvent.setup()
    const view = render(withDashboard(<SelectableRuns rows={runs(5_000, 4)} />))

    scrollTo(1_000 * 36)
    const before = renderedIds()
    expect(before).toContain('r1004')

    view.rerender(withDashboard(<SelectableRuns rows={[...runs(3, 1), ...runs(5_000, 4)]} />))
    // The browser follows the table's own scroll correction with a scroll event.
    act(() => {
      fireEvent.scroll(grid())
    })

    expect(grid().scrollTop).toBe(1_003 * 36)
    expect(renderedIds()).toEqual(before)
    expect(screen.queryByRole('row', { name: /^r1\b/ })).toBeNull()

    await user.click(screen.getByRole('button', { name: '3 new' }))

    expect(grid().scrollTop).toBe(0)
    expect(screen.queryByRole('button', { name: /new$/ })).toBeNull()
  })

  it('keeps a selected failed run that a live change marks succeeded selected, read as no longer matching', async () => {
    const user = userEvent.setup()
    const stream = createFakeStream()
    const filter = { status: 'Failed' }
    const fetchPage = vi.fn(async (): Promise<DashboardListPage<Run>> => ({ items: runs(3), nextCursor: null }))

    function FailedRuns() {
      const list = useDashboardKeysetList<Run>({ topic: 'diag.run', params: filter, fetchPage })
      const [selected, setSelected] = useState<ReadonlySet<string>>(new Set())

      return (
        <DataTable
          label="Failed runs"
          rows={list.rows}
          columns={runColumns}
          selectedIds={selected}
          onSelectionChange={setSelected}
          matches={(run) => run.status === filter.status}
          emptyState="No failed runs."
        />
      )
    }

    render(
      <DashboardProvider
        client={createDashboardClient()}
        environment={{
          basePath: '/headless',
          apiBase: '/headless/api',
          session: { authenticated: true },
          permissions: {},
          stream,
          api: { request: vi.fn() },
        }}
      >
        <FailedRuns />
      </DashboardProvider>,
    )
    await user.click(await screen.findByRole('row', { name: /^r2\b/ }))

    const succeeded = (id: string) => ({ module: 'diag', kind: 'run', id, topic: 'diag.run', version: '2', projection: { status: 'Succeeded' } })
    act(() => {
      stream.push('change', succeeded('r2'))
      stream.push('change', succeeded('r3'))
    })

    await waitFor(() => expect(screen.queryByRole('row', { name: /^r3\b/ })).toBeNull())
    const kept = row(/^r2\b/)
    expect(kept.getAttribute('aria-selected')).toBe('true')
    expect(within(kept).getByText('No longer matches')).toBeTruthy()
    expect(within(kept).getByText('Succeeded')).toBeTruthy()

    await user.keyboard('{Escape}')

    expect(screen.queryByRole('row', { name: /^r2\b/ })).toBeNull()
    expect(fetchPage).toHaveBeenCalledTimes(1)
  })

  it('drops a low-priority column at 400 px and keeps the row selectable', async () => {
    const user = userEvent.setup()
    // A 400 px screen: a max-width query matches when its width is at least 400 px.
    vi.stubGlobal('matchMedia', (query: string) => {
      const width = /max-width: (\d+)px/.exec(query)?.[1]

      return { matches: width !== undefined && Number(width) >= 400, media: query }
    })
    render(withDashboard(<SelectableRuns rows={runs(5)} />))

    expect(screen.queryByRole('columnheader', { name: 'Note' })).toBeNull()
    expect(screen.getByRole('columnheader', { name: 'Run' })).toBeTruthy()
    expect(screen.getByRole('columnheader', { name: 'Status' })).toBeTruthy()

    await user.click(row(/^r3\b/))

    expect(row(/^r3\b/).getAttribute('aria-selected')).toBe('true')
  })

  it('remembers compact density for the view across a reload', async () => {
    const user = userEvent.setup()
    const table = () => <DataTable label="Runs" viewId="diag.runs" rows={runs(5)} columns={runColumns} emptyState="No runs yet." />
    const first = render(withDashboard(table()))

    await user.click(screen.getByRole('button', { name: 'Compact' }))
    expect(row(/^r1\b/).parentElement?.style.height).toBe('28px')
    first.unmount()

    render(withDashboard(table()))

    expect(screen.getByRole('button', { name: 'Compact' }).getAttribute('aria-pressed')).toBe('true')
    expect(row(/^r1\b/).parentElement?.style.height).toBe('28px')
  })
})
