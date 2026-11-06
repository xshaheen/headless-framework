import { useLayoutEffect, useRef, useState, useSyncExternalStore, type FocusEvent, type KeyboardEvent, type ReactNode, type SyntheticEvent } from 'react'
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
import { below } from '../breakpoints.ts'
import { Button } from '../button.tsx'
import { Tag } from '../display/tag.tsx'
import type { PageFrameState } from '../frame/page-frame.tsx'
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

/**
 * Pins the checkbox column and the first data column (state and name) while the rest scroll sideways inside the
 * table, so a wide list never scrolls the page.
 */
class PinnedLayout<T> extends TableLayout<T> {
  pinned = 1

  protected override isStickyColumn(node: { index: number }) {
    return node.index < this.pinned
  }
}

// A checkbox or button in a row is its own control: the row's press would otherwise open the row as well.
function isolate(event: SyntheticEvent) {
  if (!('key' in event) || event.key === ' ' || event.key === 'Enter') {
    event.stopPropagation()
  }
}

const isolated = { onPointerDown: isolate, onClick: isolate, onKeyDown: isolate }

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
  /**
   * The list's page frame. The open row is then the frame's (`aria-selected`), and a `multiple` list adds a checkbox
   * column bound to the frame's checked rows, tags the rows a bulk action refused, and scrolls to them on `View`.
   * The frame's keys replace the list's own.
   */
  frame?: PageFrameState
  /** A row's actions, in the last column: shown on hover or focus with a fine pointer, always with a coarse one. */
  rowActions?: (row: T) => ReactNode
}

const narrowQuery = below.phone

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
  frame,
  rowActions,
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
  const selected = frame ? new Set(frame.openId ? [frame.openId] : []) : (selectedIds ?? ownSelection)
  const checks = frame !== undefined && selectionMode === 'multiple'
  const bulk = checks ? frame.checked : selected
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
  const allLoaded = listed.length > 0 && listed.every((row) => bulk.has(row.id))
  const someLoaded = listed.some((row) => bulk.has(row.id))
  const more = matchingCount !== undefined && matchingCount > listed.length
  const revealed = frame?.revealed ?? 0
  const [layout] = useState(() => new PinnedLayout<T>())
  const headerIndex = Math.max(
    0,
    columns.findIndex((column) => column.isRowHeader),
  )
  const shown = columns.filter((column, index) => !narrow || index === headerIndex || (column.priority ?? 1) === 1)
  const rowHeaderId = columns[headerIndex]?.id
  layout.pinned = shown.findIndex((column) => column.id === rowHeaderId) + (checks ? 2 : 1)

  // `View` on a bulk result: the refused rows light up and the first scrolls into the list's view.
  useLayoutEffect(() => {
    const index = revealed > 0 ? listed.findIndex((row) => frame?.refused.has(row.id)) : -1

    if (index >= 0 && scroller.current) {
      scroller.current.scrollTop = index * rowHeight
    }
    // Only a new `View` scrolls; a live update to the rows must leave the operator's place alone.
  }, [revealed])

  function check(ids: ReadonlySet<string>) {
    frame?.setChecked(ids)
  }

  function change(keys: Selection) {
    if (frame) {
      const id = keys === 'all' ? undefined : [...keys][0]

      // An arrow key moves the focus and React Aria selects the row it lands on: that opens a row only while one is open.
      if (!moving.current || frame.openId) {
        if (id === undefined) {
          frame.close()
        } else {
          frame.open(String(id))
        }
      }

      return
    }

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

  useViewKeys(
    {
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
    },
    !frame,
  )

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
      {selectionMode === 'multiple' && allLoaded && (checks || more) ? (
        <div className="hd-banner">
          <span>{checks ? `All ${listed.length} loaded rows are checked.` : `All ${listed.length} rows on this page are selected.`}</span>
          {more ? (
            <Button variant="ghost" onPress={onSelectAllMatching}>
              {`Select all ${matchingCount} matching`}
            </Button>
          ) : null}
        </div>
      ) : null}
      {/* The empty state sits outside the grid: inside it the virtualizer gives it no height, so its next step
          (`Clear filters`) could not be seen or clicked. */}
      {listed.length === 0 && !hasMore ? (
        <div className="hd-empty">{emptyState}</div>
      ) : (
        <Virtualizer layout={layout} layoutOptions={{ rowHeight, headingHeight, loaderHeight: rowHeight }}>
          <Table
            ref={scroller}
            aria-label={label}
            className="hd-table"
            selectionMode={frame ? 'single' : selectionMode}
            selectionBehavior="replace"
            selectedKeys={selected as Set<Key>}
            onSelectionChange={change}
            onRowAction={frame ? (key) => frame.open(String(key)) : selectOnly}
            // The frame closes the panel, then clears the checked rows, on its own Esc.
            escapeKeyBehavior={frame ? 'none' : 'clearSelection'}
          >
            <TableHeader>
              {checks ? (
                <Column id="hd-check" width={44} className="hd-check">
                  <input
                    type="checkbox"
                    aria-label="Select all loaded"
                    checked={allLoaded}
                    ref={(input) => {
                      if (input) {
                        input.indeterminate = someLoaded && !allLoaded
                      }
                    }}
                    onChange={() => check(new Set(allLoaded ? [] : listed.map((row) => row.id)))}
                    {...isolated}
                  />
                </Column>
              ) : null}
              {shown.map((column) => (
                <Column
                  key={column.id}
                  id={column.id}
                  isRowHeader={column.id === rowHeaderId}
                  defaultWidth={column.width}
                  minWidth={96}
                  className={column.align === 'end' ? 'hd-end' : undefined}
                >
                  {column.header}
                </Column>
              ))}
              {rowActions ? (
                <Column id="hd-actions" width={96}>
                  <span className="hd-visually-hidden">Actions</span>
                </Column>
              ) : null}
            </TableHeader>
            <TableBody>
              {listed.map((row) => {
                const stale = matches !== undefined && !matches(row)
                const refusal = frame?.refused.get(row.id)

                return (
                  <Row
                    key={row.id}
                    id={row.id}
                    className="hd-row"
                    data-row-id={row.id}
                    data-no-longer-matches={stale || undefined}
                    data-highlighted={(revealed > 0 && refusal !== undefined) || undefined}
                  >
                    {checks ? (
                      <Cell className="hd-check">
                        <input
                          type="checkbox"
                          aria-label={`Select ${row.id}`}
                          checked={bulk.has(row.id)}
                          onChange={(event) => {
                            const next = new Set(bulk)

                            if (event.target.checked) {
                              next.add(row.id)
                            } else {
                              next.delete(row.id)
                            }

                            check(next)
                          }}
                          {...isolated}
                        />
                      </Cell>
                    ) : null}
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
                        {refusal !== undefined && column.id === rowHeaderId ? (
                          <>
                            {' '}
                            <Tag>{refusal}</Tag>
                          </>
                        ) : null}
                      </Cell>
                    ))}
                    {rowActions ? (
                      <Cell className="hd-row-actions">
                        <span {...isolated}>{rowActions(row)}</span>
                      </Cell>
                    ) : null}
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
