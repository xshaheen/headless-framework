import { useRef, useState, useSyncExternalStore, type FocusEvent, type KeyboardEvent, type ReactNode } from 'react'
import {
  Cell,
  Column,
  Row,
  Table,
  TableBody,
  TableHeader,
  TableLayout,
  TableLoadMoreItem,
  Virtualizer,
  type ColumnProps,
  type Key,
  type Selection,
} from 'react-aria-components'
import { Button } from '../button.tsx'
import { useViewKeys } from '../keymap.tsx'
import { Announcer } from '../live.tsx'
import { DensityToggle, rowHeights, useDensity } from './density.tsx'
import { loadAheadRows, useHeldRows } from './paging.ts'

export interface DataTableColumn<T> {
  id: string
  header: string
  cell: (row: T) => ReactNode
  /** `end` for numbers, which also read in tabular figures. */
  align?: 'start' | 'end'
  /** The column that names the row for assistive technology; the first column when none says so. */
  isRowHeader?: boolean
  /**
   * `1`, the default, keeps the column at every width; `2` drops it below 768 px, where a list keeps its status,
   * name, and time columns. The row header column always shows.
   */
  priority?: 1 | 2
  /** The column's starting width: pixels, or a share such as `2fr`. Columns share the width equally by default. */
  width?: ColumnProps['defaultWidth']
}

/** The arrived rows a live list does not hold yet, and the call that loads them, as `useNewRows` reads them. */
export interface DataTableNewRows {
  count: number
  show: () => unknown
}

export interface DataTableProps<T extends { id: string }> {
  /** Names the grid, such as `Failed runs`. */
  label: string
  /** The view's stable id, such as `diagnostics.runs`; the row density is remembered per browser under it. */
  viewId?: string
  columns: readonly DataTableColumn<T>[]
  rows: readonly T[]
  selectionMode?: 'single' | 'multiple'
  /** The selected row ids, when the view keeps the selection in its URL. */
  selectedIds?: ReadonlySet<string>
  /** The selection changed: a click, the arrow keys, `Enter`, `J`/`K`, `X`, or `Esc`. The inspector shows it. */
  onSelectionChange?: (ids: ReadonlySet<string>) => void
  /** Rows the filter matches past the loaded page; offers `Select all matching` once every loaded row is selected. */
  matchingCount?: number
  onSelectAllMatching?: () => void
  /**
   * Whether a row still matches the view's filter. A live change can move a row out of it: the row leaves the list,
   * unless it is selected, when it stays selected and reads `No longer matches` until the selection drops it.
   */
  matches?: (row: T) => boolean
  /** More rows follow the loaded ones: the table asks for them when the scroll or the focus nears the end. */
  hasMore?: boolean
  isLoadingMore?: boolean
  /** Asks for the next keyset page; called again while one loads, it must do nothing. */
  onLoadMore?: () => void
  /** Rows that arrived for this list and are not loaded yet; they join the `N new` control. */
  newRows?: DataTableNewRows
  /** What the empty list says, and how rows get there. */
  emptyState: ReactNode
}

const narrowQuery = '(max-width: 767px)'

function subscribeNarrow(onChange: () => void) {
  if (typeof window.matchMedia !== 'function') {
    return () => undefined
  }

  const query = window.matchMedia(narrowQuery)
  query.addEventListener?.('change', onChange)

  return () => query.removeEventListener?.('change', onChange)
}

function isNarrow() {
  return typeof window.matchMedia === 'function' && window.matchMedia(narrowQuery).matches
}

/** Keys that move the focus in the grid; without `Shift` they never change a multi-selection. */
const moveKeys = new Set(['ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown'])

/** The header row's height in px, as `components.css` sets it. */
const headingHeight = 32

/**
 * The dashboard's list of one object kind, as a virtualized WAI-ARIA grid: only the rows in view are in the page,
 * and the next keyset page loads as the scroll or the focus nears the end. One tab stop; arrow keys, `Home`/`End`,
 * and `J`/`K` move the focus (and the selection, in a single-selection list), `Enter` selects the focused row into the
 * inspector without leaving the list, `X` toggles the focused row in a multi-selection, `Shift` with the arrows extends
 * it, and `Esc` clears the selection. A row never opens a
 * page; the inspector links to it. Rows that arrive above the ones in view hold the operator's place behind `N new`.
 */
export function DataTable<T extends { id: string }>({
  label,
  viewId,
  columns,
  rows,
  selectionMode = 'single',
  selectedIds,
  onSelectionChange,
  matchingCount,
  onSelectAllMatching,
  matches,
  hasMore = false,
  isLoadingMore = false,
  onLoadMore,
  newRows,
  emptyState,
}: DataTableProps<T>) {
  const [ownSelection, setOwnSelection] = useState<ReadonlySet<string>>(new Set())
  const [density, setDensity] = useDensity(viewId)
  const narrow = useSyncExternalStore(subscribeNarrow, isNarrow, () => false)
  const wrap = useRef<HTMLDivElement>(null)
  const scroller = useRef<HTMLDivElement>(null)
  const focusedId = useRef<string | undefined>(undefined)
  // React Aria selects the row the focus lands on in its `replace` behavior; a multi-selection built with `X` must
  // survive Tab and the arrow keys, so a change made while the focus moves is dropped there.
  const moving = useRef(false)
  const selected = selectedIds ?? ownSelection
  const rowHeight = rowHeights[density]
  const listed = matches ? rows.filter((row) => selected.has(row.id) || matches(row)) : rows
  const [ids, setIds] = useState<readonly string[]>([])
  const nextIds = listed.map((row) => row.id)

  // One array per distinct order of ids, so the held-rows effect runs only when the rows themselves change.
  if (nextIds.length !== ids.length || nextIds.some((id, index) => id !== ids[index])) {
    setIds(nextIds)
  }

  const { held, reveal } = useHeldRows(scroller, ids, rowHeight, (id) => selected.has(id))
  const pending = newRows?.count ?? 0
  const arrived = held + pending
  const allLoadedSelected = listed.length > 0 && listed.every((row) => selected.has(row.id))
  const offerAllMatching =
    selectionMode === 'multiple' && allLoadedSelected && matchingCount !== undefined && matchingCount > listed.length
  const headerIndex = Math.max(
    0,
    columns.findIndex((column) => column.isRowHeader),
  )
  const shown = columns.filter((column, index) => !narrow || index === headerIndex || (column.priority ?? 1) === 1)
  const rowHeaderId = columns[headerIndex]?.id

  function change(keys: Selection) {
    if (moving.current && selectionMode === 'multiple') {
      return
    }

    commit(new Set<string>(keys === 'all' ? listed.map((row) => row.id) : [...keys].map(String)))
  }

  function commit(next: ReadonlySet<string>) {
    if (!selectedIds) {
      setOwnSelection(next)
    }

    onSelectionChange?.(next)
  }

  // `Enter` (and a double click) puts the row alone in the selection, which the inspector shows; focus stays here.
  function selectOnly(key: Key) {
    commit(new Set([String(key)]))
  }

  function loadMore() {
    if (hasMore && !isLoadingMore) {
      onLoadMore?.()
    }
  }

  function onFocus(event: FocusEvent<HTMLDivElement>) {
    moving.current = false
    const row = event.target.closest<HTMLElement>('[data-row-id]')

    if (!row) {
      return
    }

    focusedId.current = row.dataset.rowId

    if (listed.findIndex((item) => item.id === row.dataset.rowId) >= listed.length - loadAheadRows) {
      loadMore()
    }
  }

  // The row `J`/`K` and `X` act on: the focused one, else the selected one, as after a click in the inspector.
  function currentIndex() {
    const id = focusedId.current ?? listed.find((row) => selected.has(row.id))?.id

    return listed.findIndex((row) => row.id === id)
  }

  function moveTo(index: number) {
    const row = listed[index]

    if (!row) {
      return
    }

    if (selectionMode === 'single') {
      commit(new Set([row.id]))
    }

    focusedId.current = row.id
    wrap.current?.querySelector<HTMLElement>(`[data-row-id="${CSS.escape(row.id)}"]`)?.focus()
  }

  useViewKeys({
    next: () => {
      const index = currentIndex()

      if (index < listed.length - 1) {
        moveTo(index + 1)
      }
    },
    previous: () => {
      const index = currentIndex()
      moveTo(index === -1 ? 0 : index - 1)
    },
    toggle:
      selectionMode === 'multiple'
        ? () => {
            const row = listed[currentIndex()]

            if (!row) {
              return
            }

            const next = new Set(selected)

            if (next.has(row.id)) {
              next.delete(row.id)
            } else {
              next.add(row.id)
            }

            commit(next)
          }
        : undefined,
  })

  function showArrived() {
    reveal(pending > 0)

    if (pending > 0) {
      void newRows?.show()
    }
  }

  return (
    <div
      ref={wrap}
      className="hd-table-wrap"
      data-density={density}
      onFocusCapture={() => (moving.current = true)}
      onFocus={onFocus}
      onKeyDownCapture={(event: KeyboardEvent) => (moving.current = !event.shiftKey && moveKeys.has(event.key))}
      onKeyDown={() => (moving.current = false)}
    >
      <div className="hd-table-bar">
        {arrived > 0 ? (
          <Button variant="ghost" className="hd-new-rows" onPress={showArrived}>
            {`${arrived} new`}
          </Button>
        ) : null}
        <Announcer message={arrived > 0 ? `${arrived} new ${arrived === 1 ? 'row' : 'rows'}` : ''} />
        <DensityToggle value={density} onChange={setDensity} />
      </div>
      {offerAllMatching ? (
        <div className="hd-banner">
          <span>{`All ${listed.length} rows on this page are selected.`}</span>
          <Button variant="ghost" onPress={onSelectAllMatching}>
            {`Select all ${matchingCount} matching`}
          </Button>
        </div>
      ) : null}
      {/* The empty state sits outside the grid: inside it the virtualizer gives it no height, so its next step
          (`Clear filters`) could not be seen or clicked. */}
      {listed.length === 0 && !hasMore ? (
        <div className="hd-empty">{emptyState}</div>
      ) : (
        <Virtualizer layout={TableLayout} layoutOptions={{ rowHeight, headingHeight, loaderHeight: rowHeight }}>
          <Table
            ref={scroller}
            aria-label={label}
            className="hd-table"
            selectionMode={selectionMode}
            selectionBehavior="replace"
            selectedKeys={selected as Set<Key>}
            onSelectionChange={change}
            onRowAction={selectOnly}
            escapeKeyBehavior="clearSelection"
          >
            <TableHeader>
              {shown.map((column) => (
                <Column
                  key={column.id}
                  id={column.id}
                  isRowHeader={column.id === rowHeaderId}
                  defaultWidth={column.width}
                  className={column.align === 'end' ? 'hd-end' : undefined}
                >
                  {column.header}
                </Column>
              ))}
            </TableHeader>
            <TableBody>
              {listed.map((row) => {
                const stale = matches !== undefined && !matches(row)

                return (
                  <Row
                    key={row.id}
                    id={row.id}
                    className="hd-row"
                    data-row-id={row.id}
                    data-no-longer-matches={stale || undefined}
                  >
                    {shown.map((column) => (
                      <Cell key={column.id} className={column.align === 'end' ? 'hd-end' : undefined}>
                        {column.cell(row)}
                        {/* The space keeps the row's accessible name `r2 No longer matches`, not `r2No longer…`. */}
                        {stale && column.id === rowHeaderId ? (
                          <>
                            {' '}
                            <span className="hd-chip hd-no-match">No longer matches</span>
                          </>
                        ) : null}
                      </Cell>
                    ))}
                  </Row>
                )
              })}
              {hasMore && onLoadMore ? (
                <TableLoadMoreItem className="hd-load-more" isLoading={isLoadingMore} onLoadMore={loadMore}>
                  <span className="hd-hint">Loading more…</span>
                </TableLoadMoreItem>
              ) : null}
            </TableBody>
          </Table>
        </Virtualizer>
      )}
    </div>
  )
}
