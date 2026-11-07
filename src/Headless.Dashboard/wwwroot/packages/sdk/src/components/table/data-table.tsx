import { useState, type ReactNode } from 'react'
import { Cell, Column, Row, Table, TableBody, TableHeader, type Key, type Selection } from 'react-aria-components'
import { Button } from '../button.tsx'

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
  onSelectionChange?: (ids: ReadonlySet<string>) => void
  /** Opens the row in the inspector: `Enter` on the focused row, or a double click. */
  onOpen?: (row: T) => void
  /** Rows the filter matches past the loaded page; offers `Select all matching` once every loaded row is selected. */
  matchingCount?: number
  onSelectAllMatching?: () => void
  /** What the empty list says, and how rows get there. */
  emptyState: ReactNode
}

/**
 * The dashboard's list of one object kind, as a WAI-ARIA grid: one tab stop, arrow keys move the selection, `Enter`
 * opens the row, and `Esc` clears the selection.
 */
export function DataTable<T extends { id: string }>({
  label,
  columns,
  rows,
  selectionMode = 'single',
  selectedIds,
  onSelectionChange,
  onOpen,
  matchingCount,
  onSelectAllMatching,
  emptyState,
}: DataTableProps<T>) {
  const [ownSelection, setOwnSelection] = useState<ReadonlySet<string>>(new Set())
  const selected = selectedIds ?? ownSelection
  const allLoadedSelected = rows.length > 0 && rows.every((row) => selected.has(row.id))
  const offerAllMatching =
    selectionMode === 'multiple' && allLoadedSelected && matchingCount !== undefined && matchingCount > rows.length

  function change(keys: Selection) {
    const ids = new Set<string>(keys === 'all' ? rows.map((row) => row.id) : [...keys].map(String))

    if (!selectedIds) {
      setOwnSelection(ids)
    }

    onSelectionChange?.(ids)
  }

  function open(key: Key) {
    const row = rows.find((candidate) => candidate.id === String(key))

    if (row) {
      onOpen?.(row)
    }
  }

  const headerIndex = Math.max(
    0,
    columns.findIndex((column) => column.isRowHeader),
  )

  return (
    <div className="hd-table-wrap">
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
        onRowAction={open}
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
            <Row key={row.id} id={row.id} className="hd-row">
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
