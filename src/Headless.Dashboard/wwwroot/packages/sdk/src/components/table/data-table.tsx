import { useEffect, useLayoutEffect, useRef, useState, type FocusEvent, type KeyboardEvent, type ReactNode, type SyntheticEvent } from 'react'
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
  type SortDescriptor,
} from 'react-aria-components'
import type { ListSort } from '../../data/keyset.ts'
import { below, useBelow } from '../breakpoints.ts'
import { Button } from '../button.tsx'
import { Banner } from '../display/banner.tsx'
import { EmptyState } from '../display/empty-state.tsx'
import { Tag } from '../display/tag.tsx'
import { Text } from '../display/text.tsx'
import type { PageFrameState } from '../frame/page-frame.tsx'
import { useViewKeys } from '../keymap.tsx'
import { Announcer, newRowsAnnouncement } from '../live.tsx'
import { DensityToggle, rowHeights, useDensity } from './density.tsx'
import { loadAheadRows, useHeldRows } from './paging.ts'

export interface DataTableColumn<T> {
  id: string
  header: string
  cell: (row: T) => ReactNode
  /**
   * What the cells hold, which sets their face and alignment: `time` (a `Timestamp`), `number`, and `duration`
   * read in mono with tabular figures, the last two at the inline end; `id` is a mono identifier kept left to right;
   * `status` is a dot and a word; `prose`, the default, is sans text such as a reason or a kind.
   */
  kind?: DataTableCellKind
  /** `end` for a value read against the others, such as a count; a `number` or `duration` column ends by default. */
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
  /**
   * The key the server sorts this column by, such as `executionTime`. The header becomes a button that asks for that
   * order, ascending first and descending on the next press, when the table has `onSortChange`.
   */
  sortKey?: string
}

/** What a column's cells hold; see `DataTableColumn.kind`. */
export type DataTableCellKind = 'time' | 'number' | 'duration' | 'id' | 'status' | 'prose'

// Times, numbers, durations, and ids are mono, with tabular figures where digits line up down a column, as every
// figure on the dashboard is; a `Mono` inside an id cell keeps it left to right. A status and prose keep the sans face.
const kindClasses: Readonly<Record<DataTableCellKind, string>> = {
  time: 'type-mono tabular-nums',
  number: 'type-mono tabular-nums',
  duration: 'type-mono tabular-nums',
  id: 'type-mono',
  status: 'type-small',
  prose: 'type-small',
}

function ends(column: { kind?: DataTableCellKind; align?: 'start' | 'end' }) {
  return column.align ? column.align === 'end' : column.kind === 'number' || column.kind === 'duration'
}

/**
 * Pins the checkbox column and the first data column (state and name) to the start, and the row actions to the end,
 * while the rest scroll sideways inside the table, so a wide list never scrolls the page and a row's actions never
 * need a sideways scroll to come into view.
 */
class PinnedLayout<T> extends TableLayout<T> {
  pinned = 1
  pinsEnd = false

  protected override isStickyColumn(node: { index: number }) {
    return node.index < this.pinned || (this.pinsEnd && node.index === this.collection.columns.length - 1)
  }

  // A sticky cell sticks to its nearest clipping ancestor. React Aria clips the header and the body, so without these
  // a pinned cell would hold still inside a box that scrolls away with the rest.
  protected override buildTableHeader() {
    const node = super.buildTableHeader()
    node.layoutInfo.allowOverflow = true
    return node
  }

  protected override buildRowGroup(...args: Parameters<TableLayout<T>['buildRowGroup']>) {
    const node = super.buildRowGroup(...args)
    node.layoutInfo.allowOverflow = true
    return node
  }
}

// A checkbox or button in a row is its own control: the row's press would otherwise open the row as well.
function isolate(event: SyntheticEvent) {
  if (!('key' in event) || event.key === ' ' || event.key === 'Enter') {
    event.stopPropagation()
  }
}

const isolated = { onPointerDown: isolate, onClick: isolate, onKeyDown: isolate }

// A cell fills its row's height, so its text centres on the row and the row header's lines up with the rest. The
// virtualizer wraps each cell and the header in an inline block, sticky for the header and the pinned columns, which
// the table marks `data-pinned`, and aligns to the top so it stays on its row. A pinned wrapper must be opaque, so the
// cells that scroll sideways under it stay hidden: in a body row it paints the card and its cell paints the row's
// fill over it, since the failure tone is translucent; elsewhere it paints its parent's fill.
// The virtualizer places a sticky wrapper in flow after the ones before it, so a row with an end pin lays its wrappers
// out as a flex row and pushes that one to the inline end, where it sticks; its inline `left`/`right` gives way to it.
// While content scrolls under the end pin (`data-overflows-end`), a hairline marks its inline-start edge.
// A row's first cell pads to the page gutter (16 px), so the first column's text lines up with the page title and the
// header's controls above it; the rest keep the 10 px cell inset.
const tableClass =
  'relative max-h-[max(16rem,calc(100dvh-18rem))] w-full overflow-auto [overflow-anchor:none] outline-none [&_:has(>[data-pinned])]:bg-inherit [&_[data-row-id]>:has(>[data-pinned])]:bg-card [&_[data-row-id]>*>[data-pinned]]:bg-(--row-fill) [&_[data-row-id]>*>[data-pinned]]:transition-fast [&_:has(>[data-pinned])]:align-top [&_[role=row]:has(>*>[data-pinned=end])]:flex [&_:has(>[data-pinned=end])]:ms-auto [&_:has(>[data-pinned=end])]:start-auto! [&_:has(>[data-pinned=end])]:end-0 [&[data-overflows-end]_[data-pinned=end]]:shadow-[inset_1px_0_0_var(--color-edge)] rtl:[&[data-overflows-end]_[data-pinned=end]]:shadow-[inset_-1px_0_0_var(--color-edge)] [&_[role=row]>:first-child>*]:ps-4'
const headClass = 'flex h-full items-center border-b border-border bg-card px-2.5 type-label whitespace-nowrap text-faint'
// The sort button fills its header, so the whole header is its target; the sorted column reads in the foreground.
const sortClass =
  '-mx-1 inline-flex h-full cursor-pointer items-center gap-1 rounded-sm px-1 text-inherit outline-none hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring group-data-sort-direction:text-foreground'
// The face comes from the column's kind (one `type-*` utility per cell, since each is a `font` shorthand).
const cellClass =
  'flex h-full items-center gap-2 overflow-hidden px-2.5 text-ellipsis whitespace-nowrap [&>span:only-child]:min-w-0 [&>span:only-child]:overflow-hidden [&>span:only-child]:text-ellipsis'
const endClass = 'justify-end text-end tabular-nums'
// Checked state lives on the checkbox alone, never on the row's fill; the margin makes a 24 px target.
const checkClass = 'flex h-full items-center ps-4 [&_input]:m-1 [&_input]:size-4 [&_input]:accent-primary'
// Tailwind orders same-specificity variants by name, not by this list, so no two row states that can hold together set
// the same property: the focus ring is an outline, and each fill excludes the states it yields to (hover yields to
// selection, both to the highlighted failure tone). The fill is a variable so a pinned cell can paint the same one
// over its opaque wrapper.
// The opaque pinned cells would hide anything the row itself draws, so the row only sets its bar and ring colours and
// each pinned cell draws them above its own fill: the first cell, always pinned, carries the inline-start bar, and every
// pinned cell an overlay with the ring's top and bottom, plus its inline-start or inline-end side at the row's ends.
// The row's own outline still rings the cells that scroll between the pins. The next row overlaps this one's bottom
// border by a pixel and hides the outline's lower pixel there, so the overlay's bottom is one pixel to match it.
const rowClass =
  'group/row cursor-pointer border-b border-border [--row-fill:var(--color-card)] [--row-bar:transparent] [--row-ring:transparent] bg-(--row-fill) outline-none transition-fast data-hovered:not-data-highlighted:not-data-selected:[--row-fill:var(--color-accent)] data-selected:not-data-highlighted:[--row-fill:var(--color-primary-soft)] data-selected:not-data-highlighted:[--row-bar:var(--accent)] data-focus-visible:[--row-ring:var(--color-ring)] data-focus-visible:outline-solid data-focus-visible:outline-2 data-focus-visible:-outline-offset-2 data-focus-visible:outline-ring data-highlighted:[--row-fill:var(--color-destructive-soft)] data-highlighted:[--row-bar:var(--fail)] data-no-longer-matches:text-muted-foreground [&>*>[data-pinned]]:relative [&>*>[data-pinned]]:after:pointer-events-none [&>*>[data-pinned]]:after:absolute [&>*>[data-pinned]]:after:inset-0 [&>*>[data-pinned]]:after:border-t-2 [&>*>[data-pinned]]:after:border-b [&>*>[data-pinned]]:after:border-(--row-ring) [&>:first-child>*]:after:border-s-2 [&>*>[data-pinned=end]]:after:border-e-2 [&>:first-child>*]:before:absolute [&>:first-child>*]:before:inset-y-0 [&>:first-child>*]:before:start-0 [&>:first-child>*]:before:w-0.5 [&>:first-child>*]:before:bg-(--row-bar)'
// Opacity, not visibility, hides the row actions, so they stay in the tab order; a coarse pointer has no hover, so
// it always sees them.
const actionsClass =
  'flex h-full items-center px-2.5 *:flex *:gap-1 *:opacity-0 group-focus-within/row:*:opacity-100 group-data-hovered/row:*:opacity-100 pointer-coarse:*:opacity-100'

/** The arrived rows a live list does not hold yet, and the call that loads them, as `useNewRows` reads them. */
export interface DataTableNewRows {
  count: number
  /** The count is a floor: the topic passed its cap and dropped the oldest arrivals, so the control reads `500+ new`. */
  isCapped?: boolean
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
  /** The server order the rows are in; its key is a column's `sortKey`. */
  sort?: ListSort
  /** A sortable header was pressed: the list reads its first page again in this order. */
  onSortChange?: (sort: ListSort) => void
}

/** The arrow beside a sorted header: up for ascending, down for descending, hidden on a column the list is not sorted by. */
function SortArrow() {
  return (
    <svg viewBox="0 0 16 16" aria-hidden="true" className="hidden size-3 fill-none stroke-current stroke-2 group-data-sort-direction:block group-data-[sort-direction=descending]:rotate-180">
      <path d="M8 13V3M4 7l4-4 4 4" />
    </svg>
  )
}

/** Keys that move the focus in the grid; without `Shift` they never change a multi-selection. */
const moveKeys = new Set(['ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown'])

/** The header row's height in px; the virtualizer lays the header out by it, and its cells fill that height. */
const headingHeight = 32

/** A column's narrowest width in px, past which the table scrolls sideways rather than squeeze it further. */
const columnMinWidth = 96

/**
 * The row header column's narrowest width in px: a pinned name such as `sandbox.overlap-fails` (about 24 mono
 * characters with the gutter) stays whole on a phone, where the other columns share the 96 px floor and scroll under
 * it, so the identifying column is never the one that ends in an ellipsis.
 */
const rowHeaderMinWidth = 192

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
  sort,
  onSortChange,
}: DataTableProps<T>) {
  const [ownSelection, setOwnSelection] = useState<ReadonlySet<string>>(new Set())
  const [density, setDensity] = useDensity(viewId)
  const narrow = useBelow(below.phone)
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
  const arrivedText = `${arrived}${newRows?.isCapped ? '+' : ''} new`
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
  const lastPinned = shown.findIndex((column) => column.id === rowHeaderId)
  layout.pinned = lastPinned + (checks ? 2 : 1)
  const pinsEnd = rowActions !== undefined
  layout.pinsEnd = pinsEnd
  const isEmpty = listed.length === 0 && !hasMore

  // Whether content scrolls under the end pin depends on the table's width and its scroll, which no style can read in
  // every browser; the mark goes on the element itself so a scroll costs no render.
  useEffect(() => {
    const table = scroller.current

    if (!pinsEnd || !table) {
      return
    }

    const mark = () =>
      table.toggleAttribute('data-overflows-end', table.scrollWidth - table.clientWidth - Math.abs(table.scrollLeft) > 1)

    mark()
    // Outside a browser (jsdom) nothing lays out, so nothing can overflow.
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(mark)
    observer?.observe(table)
    table.addEventListener('scroll', mark, { passive: true })

    return () => {
      observer?.disconnect()
      table.removeEventListener('scroll', mark)
    }
  }, [pinsEnd, isEmpty])
  // React Aria sorts by column id; the server by the column's sort key.
  const sortedColumn = sort ? columns.find((column) => column.sortKey === sort.key) : undefined
  const sortDescriptor: SortDescriptor | undefined = sort && sortedColumn ? { column: sortedColumn.id, direction: sort.direction } : undefined

  function changeSort(next: SortDescriptor) {
    const key = columns.find((column) => column.id === next.column)?.sortKey

    if (key !== undefined) {
      onSortChange?.({ key, direction: next.direction })
    }
  }

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

      // An arrow key moves the focus and React Aria selects the row it lands on: that opens a row only while one is
      // open, replacing its history entry as `J`/`K` do, so Back leaves the panel rather than walking every row.
      if (!moving.current || frame.openId) {
        if (id === undefined) {
          frame.close()
        } else {
          frame.open(String(id), moving.current)
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
      className="flex flex-col gap-2 border-t border-border"
      data-density={density}
      onFocusCapture={() => (moving.current = true)}
      onFocus={onFocus}
      onKeyDownCapture={(event: KeyboardEvent) => (moving.current = !event.shiftKey && moveKeys.has(event.key))}
      onKeyDown={() => (moving.current = false)}
    >
      {/* The table insets its own first column, so its toolbar takes the same gutter rather than meeting the page edge.
          A table with no view id remembers no density, so it offers no toggle, and a toolbar with nothing in it is
          left out rather than spacing the table down. */}
      {viewId || arrived > 0 ? (
        <div className="flex flex-wrap items-center justify-between gap-2 px-4 pt-2 [&>[role=group]]:ms-auto">
          {arrived > 0 ? (
            <Button variant="ghost" className="self-center text-primary" onPress={showArrived}>
              {arrivedText}
            </Button>
          ) : null}
          {viewId ? <DensityToggle value={density} onChange={setDensity} /> : null}
        </div>
      ) : null}
      {/* The same words while the count climbs, so a busy list is announced once rather than on every arrival. */}
      <Announcer message={arrived > 0 ? newRowsAnnouncement : ''} />
      {selectionMode === 'multiple' && allLoaded && (checks || more) ? (
        <Banner>
          <span>{checks ? `All ${listed.length} loaded rows are checked.` : `All ${listed.length} rows on this page are selected.`}</span>
          {more ? (
            <Button variant="ghost" onPress={onSelectAllMatching}>
              {`Select all ${matchingCount} matching`}
            </Button>
          ) : null}
        </Banner>
      ) : null}
      {/* The empty state sits outside the grid: inside it the virtualizer gives it no height, so its next step
          (`Clear filters`) could not be seen or clicked. */}
      {isEmpty ? (
        // On the table's own gutter, start-aligned as a list's empty state is; only an empty page centres one.
        <div className="px-0.5">
          <EmptyState>{emptyState}</EmptyState>
        </div>
      ) : (
        <Virtualizer layout={layout} layoutOptions={{ rowHeight, headingHeight, loaderHeight: rowHeight }}>
          <Table
            ref={scroller}
            aria-label={label}
            className={tableClass}
            selectionMode={frame ? 'single' : selectionMode}
            selectionBehavior="replace"
            selectedKeys={selected as Set<Key>}
            onSelectionChange={change}
            onRowAction={frame ? (key) => frame.open(String(key)) : selectOnly}
            // The frame closes the panel, then clears the checked rows, on its own Esc.
            escapeKeyBehavior={frame ? 'none' : 'clearSelection'}
            sortDescriptor={sortDescriptor}
            onSortChange={changeSort}
          >
            <TableHeader data-pinned>
              {checks ? (
                <Column id="hd-check" width={44} className={`${headClass} ${checkClass}`} data-pinned>
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
              {shown.map((column, at) => {
                const sortable = column.sortKey !== undefined && onSortChange !== undefined

                return (
                  <Column
                    key={column.id}
                    id={column.id}
                    isRowHeader={column.id === rowHeaderId}
                    defaultWidth={column.width}
                    minWidth={column.id === rowHeaderId ? rowHeaderMinWidth : columnMinWidth}
                    data-pinned={at <= lastPinned || undefined}
                    allowsSorting={sortable}
                    className={`group ${ends(column) ? `${headClass} ${endClass}` : headClass}`}
                  >
                    {/* React Aria's header owns the press and `aria-sort`; the button only names the control for assistive
                        technology, so it takes no tab stop and no handler of its own. */}
                    {sortable ? (
                      <button type="button" tabIndex={-1} className={sortClass}>
                        {column.header}
                        <SortArrow />
                      </button>
                    ) : (
                      column.header
                    )}
                  </Column>
                )
              })}
              {rowActions ? (
                <Column id="hd-actions" width={96} className={headClass} data-pinned="end">
                  <span className="sr-only">Actions</span>
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
                    className={rowClass}
                    data-row-id={row.id}
                    data-no-longer-matches={stale || undefined}
                    data-highlighted={(revealed > 0 && refusal !== undefined) || undefined}
                  >
                    {checks ? (
                      <Cell className={checkClass} data-pinned>
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
                    {shown.map((column, at) => (
                      <Cell
                        key={column.id}
                        className={`${cellClass} ${kindClasses[column.kind ?? 'prose']}${ends(column) ? ` ${endClass}` : ''}`}
                        data-pinned={at <= lastPinned || undefined}
                      >
                        {column.cell(row)}
                        {/* The space keeps the row's accessible name `r2 No longer matches`, not `r2No longer…`. */}
                        {stale && column.id === rowHeaderId ? (
                          <>
                            {' '}
                            <Tag variant="filled">No longer matches</Tag>
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
                      <Cell className={actionsClass} data-pinned="end">
                        <span {...isolated}>{rowActions(row)}</span>
                      </Cell>
                    ) : null}
                  </Row>
                )
              })}
              {hasMore && onLoadMore ? (
                <TableLoadMoreItem className="flex items-center px-2.5" isLoading={isLoadingMore} onLoadMore={loadMore}>
                  <Text tone="hint">Loading more…</Text>
                </TableLoadMoreItem>
              ) : null}
            </TableBody>
          </Table>
        </Virtualizer>
      )}
    </div>
  )
}
