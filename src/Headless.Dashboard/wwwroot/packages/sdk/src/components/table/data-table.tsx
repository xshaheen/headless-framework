import { useRef, useState, type FocusEvent, type ReactNode } from 'react'
import { Cell, Column, Row, Table, TableBody, TableHeader, type Key, type Selection } from 'react-aria-components'
import { Button } from '../button.tsx'
import { useViewKeys } from '../keymap.tsx'

export interface DataTableColumn<T> {
  id: string
  header: string
  cell: (row: T) => ReactNode
  /** `end` for numbers, which also read in tabular figures. */
  align?: 'start' | 'end'
  /** The column that names the row for assistive technology; the first column when none says so. */
  isRowHeader?: boolean
}

export interface DataTableProps<T extends { id: string }> {
  /** Names the grid, such as `Failed runs`. */
  label: string
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
  /** What the empty list says, and how rows get there. */
  emptyState: ReactNode
}

/**
 * The dashboard's list of one object kind, as a WAI-ARIA grid: one tab stop, arrow keys and `J`/`K` move the focus and
 * the selection, `Enter` selects the focused row into the inspector without leaving the list, `X` toggles the focused
 * row in a multi-selection, and `Esc` clears the selection. A row never opens a page; the inspector links to it.
 */
export function DataTable<T extends { id: string }>({
  label,
  columns,
  rows,
  selectionMode = 'single',
  selectedIds,
  onSelectionChange,
  matchingCount,
  onSelectAllMatching,
  emptyState,
}: DataTableProps<T>) {
  const [ownSelection, setOwnSelection] = useState<ReadonlySet<string>>(new Set())
  const wrap = useRef<HTMLDivElement>(null)
  const focusedId = useRef<string | undefined>(undefined)
  const selected = selectedIds ?? ownSelection
  const allLoadedSelected = rows.length > 0 && rows.every((row) => selected.has(row.id))
  const offerAllMatching =
    selectionMode === 'multiple' && allLoadedSelected && matchingCount !== undefined && matchingCount > rows.length

  function change(keys: Selection) {
    commit(new Set<string>(keys === 'all' ? rows.map((row) => row.id) : [...keys].map(String)))
  }

  function commit(ids: ReadonlySet<string>) {
    if (!selectedIds) {
      setOwnSelection(ids)
    }

    onSelectionChange?.(ids)
  }

  // `Enter` (and a double click) puts the row alone in the selection, which the inspector shows; focus stays here.
  function selectOnly(key: Key) {
    commit(new Set([String(key)]))
  }

  function onFocus(event: FocusEvent<HTMLDivElement>) {
    const row = event.target.closest<HTMLElement>('[data-row-id]')

    if (row) {
      focusedId.current = row.dataset.rowId
    }
  }

  // The row `J`/`K` and `X` act on: the focused one, else the selected one, as after a click in the inspector.
  function currentIndex() {
    const id = focusedId.current ?? rows.find((row) => selected.has(row.id))?.id

    return rows.findIndex((row) => row.id === id)
  }

  function moveTo(index: number) {
    const row = rows[index]

    if (!row) {
      return
    }

    commit(new Set([row.id]))
    focusedId.current = row.id
    wrap.current?.querySelector<HTMLElement>(`[data-row-id="${CSS.escape(row.id)}"]`)?.focus()
  }

  useViewKeys({
    next: () => {
      const index = currentIndex()

      // At the last row `J` does nothing.
      if (index < rows.length - 1) {
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
            const row = rows[currentIndex()]

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

  const headerIndex = Math.max(
    0,
    columns.findIndex((column) => column.isRowHeader),
  )

  return (
    <div ref={wrap} className="hd-table-wrap" onFocus={onFocus}>
      {offerAllMatching ? (
        <div className="hd-banner">
          <span>{`All ${rows.length} rows on this page are selected.`}</span>
          <Button variant="ghost" onPress={onSelectAllMatching}>
            {`Select all ${matchingCount} matching`}
          </Button>
        </div>
      ) : null}
      <Table
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
          {columns.map((column, index) => (
            <Column key={column.id} id={column.id} isRowHeader={index === headerIndex} className={column.align === 'end' ? 'hd-end' : undefined}>
              {column.header}
            </Column>
          ))}
        </TableHeader>
        <TableBody renderEmptyState={() => <div className="hd-empty">{emptyState}</div>}>
          {rows.map((row) => (
            <Row key={row.id} id={row.id} className="hd-row" data-row-id={row.id}>
              {columns.map((column) => (
                <Cell key={column.id} className={column.align === 'end' ? 'hd-end' : undefined}>
                  {column.cell(row)}
                </Cell>
              ))}
            </Row>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
