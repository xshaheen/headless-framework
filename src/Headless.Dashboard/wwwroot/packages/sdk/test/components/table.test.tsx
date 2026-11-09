import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState, type ReactNode } from 'react'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { useAllowedActions } from '../../src/commands/allowed.ts'
import type { CommandDefinition } from '../../src/commands/api.ts'
import { CommandAction } from '../../src/commands/command-action.tsx'
import { TextInputField } from '../../src/components/form.tsx'
import { PageFrame, usePageFrame } from '../../src/components/frame/page-frame.tsx'
import { ToastRegion } from '../../src/components/live.tsx'
import { DataTable, type DataTableProps } from '../../src/components/table/data-table.tsx'
import { DateRangeField } from '../../src/components/table/date-range-field.tsx'
import { SearchField } from '../../src/components/table/search-field.tsx'
import { rowHeights } from '../../src/components/table/density.tsx'
import { DashboardProvider, ModuleScope } from '../../src/context.tsx'
import type { ApiRequest } from '../../src/data/api.ts'
import { createDashboardClient } from '../../src/data/client.tsx'
import { useDashboardKeysetList, type ListPageRequest, type ListRange, type ListSort } from '../../src/data/keyset.ts'
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

  it('builds a multi-selection with J X J X: J moves the focus and keeps the rows X selected', async () => {
    const user = userEvent.setup()
    render(withDashboard(<ListView selectionMode="multiple" />))

    await user.keyboard('j')
    expect(document.activeElement).toBe(row(/#1/))
    expect(inspector()).toBe('Nothing selected')

    await user.keyboard('x')
    await user.keyboard('j')
    expect(document.activeElement).toBe(row(/#2/))
    expect(inspector()).toBe('r1')

    await user.keyboard('x')
    expect(inspector()).toBe('r1, r2')
  })

  it('moves the focus without selecting on Tab and the arrow keys in a multi-selection list', async () => {
    const user = userEvent.setup()
    render(withDashboard(<ListView selectionMode="multiple" />))

    // The filter, then the density toggle's two buttons, then the grid's one tab stop.
    await user.click(screen.getByRole('textbox', { name: 'Filter' }))
    await user.tab()
    await user.tab()
    await user.tab()
    expect(document.activeElement).toBe(row(/#1/))
    expect(inspector()).toBe('Nothing selected')

    await user.keyboard('{ArrowDown}')
    expect(document.activeElement).toBe(row(/#2/))
    expect(inspector()).toBe('Nothing selected')

    await user.keyboard('x{Shift>}{ArrowDown}{/Shift}')
    expect(inspector()).toBe('r2, r3')
  })

  it('runs a button in the empty state when clicked', async () => {
    const user = userEvent.setup()
    const clear = vi.fn()
    render(
      withDashboard(
        <DataTable
          label="Runs"
          rows={[]}
          selectionMode="multiple"
          columns={[{ id: 'title', header: 'Run', cell: () => '' }]}
          emptyState={
            <button type="button" onClick={clear}>
              Clear filters
            </button>
          }
        />,
      ),
    )

    await user.click(screen.getByRole('button', { name: 'Clear filters' }))

    expect(clear).toHaveBeenCalledOnce()
  })

  it('types J into a focused filter and moves no row', async () => {
    const user = userEvent.setup()
    render(withDashboard(<ListView />))

    await user.click(row(/#1/))
    const filter = screen.getByRole('textbox', { name: 'Filter' })
    await user.click(filter)

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

const rowHeight = rowHeights.comfortable

describe('virtualized keyset table', () => {
  // jsdom lays nothing out. As React Aria's own tests do, every element reports a fixed client size, so the
  // virtualizer sees a 360 px viewport: rows of the comfortable height under a 32 px header. Scroll offsets are kept per element.
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

    scrollTo(100 * rowHeight - 360)
    sentinelInView()
    scrollTo(100 * rowHeight - 300)
    sentinelInView()
    sentinelInView()

    await waitFor(() => expect(fetchPage).toHaveBeenCalledTimes(2))
    expect(fetchPage.mock.calls.map(([cursor]) => cursor)).toEqual([undefined, 'cursor-of-r100'])

    await screen.findByRole('row', { name: /^r101\b/ })
    sentinelInView()
    scrollTo(200 * rowHeight - 360)
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

    scrollTo(100 * rowHeight - 360)
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

    scrollTo(1_000 * rowHeight)
    const before = renderedIds()
    expect(before).toContain('r1004')

    view.rerender(withDashboard(<SelectableRuns rows={[...runs(3, 1), ...runs(5_000, 4)]} />))
    // The browser follows the table's own scroll correction with a scroll event.
    act(() => {
      fireEvent.scroll(grid())
    })

    expect(grid().scrollTop).toBe(1_003 * rowHeight)
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

  /** A page that resolves when the test says so, as a slow server answers. */
  function deferred<T>() {
    let resolve!: (value: T) => void
    const promise = new Promise<T>((done) => (resolve = done))

    return { promise, resolve }
  }

  type FetchPage = (cursor: string | undefined, request: ListPageRequest) => Promise<DashboardListPage<Run>>

  /** A list as a module writes one with server order, text search, and a day range, all fed to the keyset hook. */
  function ScopedRuns({ fetchPage, searchMode }: { fetchPage: FetchPage; searchMode?: 'debounce' | 'submit' }) {
    const [sort, setSort] = useState<ListSort>()
    const [search, setSearch] = useState('')
    const [range, setRange] = useState<ListRange>()
    const list = useDashboardKeysetList<Run>({ topic: 'diag.run', params: { status: 'Failed' }, sort, search, range, fetchPage })

    return (
      <>
        <SearchField label="Search runs" mode={searchMode} onSearch={setSearch} />
        <DateRangeField label="Executed" onChange={setRange} />
        <DataTable
          label="Runs"
          rows={list.rows}
          columns={[{ ...runColumns[0]!, sortKey: 'executionTime' }, runColumns[1]!]}
          sort={sort}
          onSortChange={setSort}
          hasMore={list.hasMore}
          isLoadingMore={list.isLoadingMore}
          onLoadMore={list.loadMore}
          emptyState="No runs yet."
        />
      </>
    )
  }

  /** What each page request asked for, in order: its cursor and the scope it carried. */
  function asked(fetchPage: ReturnType<typeof vi.fn<FetchPage>>) {
    return fetchPage.mock.calls.map(([cursor, { sort, search, range }]) => ({ cursor, sort, search, range }))
  }

  it('sorts on a header press, ascending then descending, reading page 1 again with no cursor each time', async () => {
    const user = userEvent.setup()
    const fetchPage = vi.fn<FetchPage>(async (cursor) =>
      cursor === undefined ? { items: runs(100), nextCursor: 'cursor-of-r100' } : { items: runs(100, 101), nextCursor: null },
    )
    render(withDashboard(<ScopedRuns fetchPage={fetchPage} />))
    await screen.findByRole('row', { name: /^r1\b/ })

    // A loaded page 2 shows the cursor is dropped, not merely unused, when the order changes.
    scrollTo(100 * rowHeight - 360)
    sentinelInView()
    await waitFor(() => expect(fetchPage).toHaveBeenCalledTimes(2))

    const header = screen.getByRole('columnheader', { name: 'Run' })
    expect(header.getAttribute('aria-sort')).toBe('none')

    const button = within(header).getByRole('button', { name: 'Run' })
    // The virtualizer stops pointer events while it scrolls, as a browser shows it; the press comes once it settles.
    await waitFor(() => expect(button.closest('[style*="pointer-events: none"]')).toBeNull(), { timeout: 2_000 })
    await user.click(button)

    await waitFor(() => expect(header.getAttribute('aria-sort')).toBe('ascending'))
    await waitFor(() => expect(fetchPage).toHaveBeenCalledTimes(3))
    expect(asked(fetchPage)[2]).toEqual({ cursor: undefined, sort: { key: 'executionTime', direction: 'ascending' }, search: undefined, range: undefined })

    act(() => header.focus())
    await user.keyboard('{Enter}')

    await waitFor(() => expect(header.getAttribute('aria-sort')).toBe('descending'))
    await waitFor(() => expect(fetchPage).toHaveBeenCalledTimes(4))
    expect(asked(fetchPage)[3]).toEqual({ cursor: undefined, sort: { key: 'executionTime', direction: 'descending' }, search: undefined, range: undefined })
    // The header keeps the focus while the new order loads, rather than the grid giving way to the empty state.
    expect(header.contains(document.activeElement)).toBe(true)
    // Each press asked once: the button inside the header adds no second press of its own.
    await new Promise((done) => setTimeout(done, 50))
    expect(fetchPage).toHaveBeenCalledTimes(4)
    expect(header.getAttribute('aria-sort')).toBe('descending')
  })

  it('gives a column without a sort key no button and no aria-sort', async () => {
    const fetchPage = vi.fn<FetchPage>(async () => ({ items: runs(3), nextCursor: null }))
    render(withDashboard(<ScopedRuns fetchPage={fetchPage} />))
    await screen.findByRole('row', { name: /^r1\b/ })

    const header = screen.getByRole('columnheader', { name: 'Status' })
    expect(header.hasAttribute('aria-sort')).toBe(false)
    expect(within(header).queryByRole('button')).toBeNull()
  })

  it('discards page 2 of the old search when the search changes while it loads', async () => {
    const user = userEvent.setup()
    const page2 = deferred<DashboardListPage<Run>>()
    let page2Signal: AbortSignal | undefined
    const fetchPage = vi.fn<FetchPage>(async (cursor, request) => {
      if (cursor !== undefined) {
        page2Signal = request.signal

        return page2.promise
      }

      return request.search ? { items: runs(2, 900), nextCursor: null } : { items: runs(100), nextCursor: 'cursor-of-r100' }
    })
    render(withDashboard(<ScopedRuns fetchPage={fetchPage} searchMode="submit" />))
    await screen.findByRole('row', { name: /^r1\b/ })

    scrollTo(100 * rowHeight - 360)
    sentinelInView()
    await waitFor(() => expect(page2Signal).toBeDefined())

    await user.type(screen.getByRole('searchbox', { name: 'Search runs' }), 'timeout{Enter}')
    await screen.findByRole('row', { name: /^r900\b/ })
    page2.resolve({ items: runs(100, 101), nextCursor: null })

    await waitFor(() => expect(page2Signal?.aborted).toBe(true))
    expect(renderedIds()).toEqual(['r900', 'r901'])
    expect(asked(fetchPage).at(-1)).toEqual({ cursor: undefined, sort: undefined, search: 'timeout', range: undefined })
  })

  it('sends nothing while typing in a submit search, and one request with the text on Enter', async () => {
    const user = userEvent.setup()
    const fetchPage = vi.fn<FetchPage>(async () => ({ items: runs(3), nextCursor: null }))
    render(withDashboard(<ScopedRuns fetchPage={fetchPage} searchMode="submit" />))
    await screen.findByRole('row', { name: /^r1\b/ })

    await user.type(screen.getByRole('searchbox', { name: 'Search runs' }), 'order 42')
    // A debounced search would have sent by now.
    await new Promise((done) => setTimeout(done, 400))
    expect(fetchPage).toHaveBeenCalledTimes(1)

    await user.keyboard('{Enter}')

    await waitFor(() => expect(fetchPage).toHaveBeenCalledTimes(2))
    expect(asked(fetchPage)[1]).toEqual({ cursor: undefined, sort: undefined, search: 'order 42', range: undefined })
  })

  it('sends a debounced search once typing pauses, with the whole text', async () => {
    const user = userEvent.setup()
    const fetchPage = vi.fn<FetchPage>(async () => ({ items: runs(3), nextCursor: null }))
    render(withDashboard(<ScopedRuns fetchPage={fetchPage} />))
    await screen.findByRole('row', { name: /^r1\b/ })

    await user.type(screen.getByRole('searchbox', { name: 'Search runs' }), 'order')
    expect(fetchPage).toHaveBeenCalledTimes(1)

    await waitFor(() => expect(fetchPage).toHaveBeenCalledTimes(2))
    expect(asked(fetchPage).map((request) => request.search)).toEqual([undefined, 'order'])
  })

  it('sends a range ending 10 November 2026 in Cairo as the next midnight there, in UTC', async () => {
    localStorage.setItem('headless-dashboard:time-zone', 'Africa/Cairo')
    const user = userEvent.setup()
    const fetchPage = vi.fn<FetchPage>(async () => ({ items: runs(3), nextCursor: null }))
    render(withDashboard(<ScopedRuns fetchPage={fetchPage} />))
    await screen.findByRole('row', { name: /^r1\b/ })

    await user.click(screen.getAllByRole('spinbutton', { name: /month/i })[0]!)
    await user.keyboard('11012026')
    await user.keyboard('11102026')

    // Cairo is two hours ahead of UTC in November 2026: midnight on 1 and 11 November there.
    await waitFor(() => expect(asked(fetchPage).at(-1)?.range).toEqual({ start: '2026-10-31T22:00:00.000Z', end: '2026-11-10T22:00:00.000Z' }))
  })
})

const requeue: CommandDefinition = {
  id: 'diag.requeue',
  label: 'Requeue',
  objectKind: 'run',
  permission: 'operate',
  confirmation: 'none',
  typedConfirmationAbove: 100,
  requiresReason: false,
  supportsFilter: true,
  arguments: { type: 'object', required: [], properties: {} },
  canRun: true,
}

interface BulkOptions {
  /** Passes the per-row refusals (`refused`), as a refused-aware list does; without them any refused row disables. */
  perRow?: boolean
  command?: CommandDefinition
  /** Holds the run's answer until the test releases it. */
  ran?: Promise<void>
}

/** A failed list as a module writes one: the page frame's checked rows, the table, and a bulk Requeue over them. */
function BulkRuns({ rows: list, perRow = true, command = requeue }: { rows: readonly Run[] } & BulkOptions) {
  const frame = usePageFrame({ viewId: 'diag.runs', rows: list })
  const checked = list.filter((run) => frame.checked.has(run.id))
  const allowed = useAllowedActions('run', checked)

  return (
    <PageFrame
      frame={frame}
      header={
        <CommandAction
          module="diag"
          command={command}
          ids={checked.map((run) => run.id)}
          noun="runs"
          recovery
          disabledReason={allowed(command.id)}
          refused={perRow ? allowed.refused(command.id) : undefined}
          onRefused={frame.setRefused}
          onViewRefused={frame.reveal}
        />
      }
    >
      <DataTable label="Runs" rows={list} columns={runColumns} frame={frame} selectionMode="multiple" emptyState="No runs yet." />
    </PageFrame>
  )
}

/** Renders the bulk view; the server refuses requeue for each id in `refusals`, with its reason. */
function renderBulk(list: readonly Run[], refusals: Record<string, string>, options: BulkOptions = {}) {
  const requests: ApiRequest[] = []
  const router = createMemoryRouter([{ path: '/', element: <BulkRuns rows={list} {...options} /> }])

  render(
    <DashboardProvider
      client={createDashboardClient()}
      environment={{
        basePath: '/headless',
        apiBase: '/headless/api',
        session: { authenticated: true },
        permissions: { diag: { canView: true, canOperate: true } },
        stream: createFakeStream(),
        api: {
          async request<T>(request: ApiRequest) {
            requests.push(request)
            const ids = (request.query?.id ?? []) as string[]

            if (request.path === 'diag/commands/allowed') {
              return {
                objects: ids.map((id) => ({
                  kind: 'run',
                  id,
                  commands: [{ id: (options.command ?? requeue).id, allowed: !refusals[id], disabledReason: refusals[id] }],
                })),
              } as T
            }

            await options.ran

            return { rows: (request.body as { ids: string[] }).ids.map((id) => ({ id, outcome: 'done' })) } as T
          },
        },
      }}
    >
      <ModuleScope id="diag">
        <RouterProvider router={router} />
        <ToastRegion />
      </ModuleScope>
    </DashboardProvider>,
  )

  return { requests, router }
}

/** The run requests the view sent, by their ids. */
function sentIds(requests: readonly ApiRequest[]) {
  return requests.filter((request) => request.path !== 'diag/commands/allowed').map((request) => (request.body as { ids: string[] }).ids)
}

describe('checked rows and bulk refusals', () => {
  const offsets = new WeakMap<Element, number>()

  beforeAll(() => {
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
    Reflect.deleteProperty(HTMLElement.prototype, 'scrollTop')
  })

  it('checks only the loaded rows with Select all loaded, says so, and opens none of them', async () => {
    const user = userEvent.setup()
    renderBulk(runs(3), {})

    await user.click(screen.getByRole('checkbox', { name: 'Select all loaded' }))

    expect(screen.getByText('All 3 loaded rows are checked.')).toBeTruthy()
    expect(screen.getAllByRole('checkbox', { checked: true }).map((box) => box.getAttribute('aria-label'))).toEqual([
      'Select all loaded',
      'Select r1',
      'Select r2',
      'Select r3',
    ])
    expect(screen.getAllByRole('row').some((element) => element.getAttribute('aria-selected') === 'true')).toBe(false)
  })

  it('marks only the open row aria-selected, apart from the checked rows', async () => {
    const user = userEvent.setup()
    renderBulk(runs(3), {})

    await user.click(screen.getByRole('checkbox', { name: 'Select r1' }))
    await user.click(row(/^r2\b/))

    expect(row(/^r2\b/).getAttribute('aria-selected')).toBe('true')
    expect(row(/^r1\b/).getAttribute('aria-selected')).not.toBe('true')
    expect((screen.getByRole('checkbox', { name: 'Select r1' }) as HTMLInputElement).checked).toBe(true)
  })

  it('requeues the 3 allowed of 5, tags the 2 refused in their rows, and View scrolls to and highlights them', async () => {
    const user = userEvent.setup()
    const { requests } = renderBulk(runs(5), { r3: 'Running', r5: 'Held' })

    await user.click(screen.getByRole('checkbox', { name: 'Select all loaded' }))
    await user.click(await screen.findByRole('button', { name: 'Requeue 3 of 5' }))

    const toast = await screen.findByText('Requeue: 3 done · 2 refused')
    expect(requests.find((request) => request.path === 'diag/commands/diag.requeue')?.body).toMatchObject({ ids: ['r1', 'r2', 'r4'] })
    expect(row(/^r3\b/).textContent).toContain('Running')
    expect(row(/^r5\b/).textContent).toContain('Held')
    expect(row(/^r1\b/).textContent).not.toContain('Running')

    await user.click(within(toast.closest('[role="status"]') as HTMLElement).getByRole('button', { name: 'View' }))

    expect(row(/^r3\b/).hasAttribute('data-highlighted')).toBe(true)
    expect(row(/^r5\b/).hasAttribute('data-highlighted')).toBe(true)
    expect(row(/^r1\b/).hasAttribute('data-highlighted')).toBe(false)
    expect(grid().scrollTop).toBe(2 * rowHeight)
  })

  it('disables the action with the shared reason when every checked row is refused', async () => {
    const user = userEvent.setup()
    renderBulk(runs(3), { r1: 'Running', r2: 'Running', r3: 'Running' })

    await user.click(screen.getByRole('checkbox', { name: 'Select all loaded' }))

    const button = await screen.findByRole('button', { name: 'Requeue 3 runs', description: 'Running' })
    expect(button.getAttribute('aria-disabled')).toBe('true')
  })

  it('says why on R when every checked row is refused, and sends nothing', async () => {
    const user = userEvent.setup()
    const { requests } = renderBulk(runs(2), { r1: 'Running', r2: 'Running' })

    await user.click(screen.getByRole('checkbox', { name: 'Select all loaded' }))
    await user.click(await screen.findByRole('button', { name: 'Requeue 2 runs', description: 'Running' }))
    await user.keyboard('r')

    expect(screen.getAllByRole('status').some((region) => region.textContent === 'Running')).toBe(true)
    expect(sentIds(requests)).toEqual([])
  })

  it('without per-row refusals, disables the action on a mixed selection with the server’s reason', async () => {
    const user = userEvent.setup()
    const { requests } = renderBulk(runs(5), { r3: 'Running', r5: 'Held' }, { perRow: false })

    await user.click(screen.getByRole('checkbox', { name: 'Select all loaded' }))

    const button = await screen.findByRole('button', { name: 'Requeue 5 runs', description: 'Running' })
    expect(button.getAttribute('aria-disabled')).toBe('true')

    await user.click(button)
    await user.keyboard('r')

    expect(screen.getAllByRole('status').some((region) => region.textContent === 'Running')).toBe(true)
    expect(sentIds(requests)).toEqual([])
  })

  it('confirms and runs the rows it was pressed on, though the selection changed behind the open confirm', async () => {
    const user = userEvent.setup()
    const { requests } = renderBulk(runs(5), { r3: 'Running', r5: 'Held' }, { command: { ...requeue, confirmation: 'confirm' } })

    await user.click(screen.getByRole('checkbox', { name: 'Select all loaded' }))
    await user.click(await screen.findByRole('button', { name: 'Requeue 3 of 5' }))
    const dialog = await screen.findByRole('dialog', { name: 'Requeue 3 of 5' })

    // A change behind the dialog asks the server again; until it answers, every checked row reads as refused.
    act(() => {
      fireEvent.click(screen.getByRole('checkbox', { name: 'Select r1', hidden: true }))
    })

    expect(screen.getByRole('dialog', { name: 'Requeue 3 of 5' })).toBe(dialog)
    await user.click(within(dialog).getByRole('button', { name: 'Requeue 3 of 5' }))

    await waitFor(() => expect(sentIds(requests)).toEqual([['r1', 'r2', 'r4']]))
    // The refusals answered for the pressed rows, not for the selection now, so no row is tagged with them.
    expect(row(/^r3\b/).textContent).not.toContain('Running')
  })

  it('keeps refusals off a selection changed while the run was in flight; the toast still reports the run', async () => {
    const user = userEvent.setup()
    let release!: () => void
    const ran = new Promise<void>((resolve) => (release = resolve))
    renderBulk(runs(5), { r3: 'Running', r5: 'Held' }, { ran })

    await user.click(screen.getByRole('checkbox', { name: 'Select all loaded' }))
    await user.click(await screen.findByRole('button', { name: 'Requeue 3 of 5' }))
    await user.click(screen.getByRole('checkbox', { name: 'Select r1' }))
    await act(async () => release())

    const toast = await screen.findByText('Requeue: 3 done · 2 refused')
    expect(within(toast.closest('[role="status"]') as HTMLElement).queryByRole('button', { name: 'View' })).toBeNull()
    expect(row(/^r3\b/).textContent).not.toContain('Running')
    expect(row(/^r5\b/).textContent).not.toContain('Held')
  })
})

describe('page frame keys on the table', () => {
  it('opens with Enter, follows J/K and the arrows without new history, checks with X, and closes then clears on Esc', async () => {
    const user = userEvent.setup()
    const { router } = renderBulk(runs(5), {})
    const search = () => router.state.location.search

    act(() => row(/^r1\b/).focus())
    await user.keyboard('{Enter}')
    expect(search()).toBe('?open=r1')

    await user.keyboard('{ArrowDown}{ArrowDown}')
    expect(search()).toBe('?open=r3')

    await user.keyboard('j')
    expect(search()).toBe('?open=r4')
    await user.keyboard('k')
    expect(search()).toBe('?open=r3')

    await user.keyboard('x')
    expect((screen.getByRole('checkbox', { name: 'Select r3' }) as HTMLInputElement).checked).toBe(true)
    expect(search()).toBe('?open=r3')

    await user.keyboard('{Escape}')
    expect(search()).toBe('')
    expect((screen.getByRole('checkbox', { name: 'Select r3' }) as HTMLInputElement).checked).toBe(true)

    await user.keyboard('{Escape}')
    expect((screen.getByRole('checkbox', { name: 'Select r3' }) as HTMLInputElement).checked).toBe(false)

    // Enter pushed one entry, the moves replaced it, and the close pushed one: two steps back reach the bare list.
    await act(() => router.navigate(-1))
    expect(search()).toBe('?open=r3')
    await act(() => router.navigate(-1))
    expect(search()).toBe('')
  })
})
