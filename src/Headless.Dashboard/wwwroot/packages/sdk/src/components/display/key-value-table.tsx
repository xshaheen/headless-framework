// The React Compiler's memo caches cost the SDK more bytes than this small table saves, and the shell's assets sit
// near their size budget.
'use no memo'

import { CopyIcon } from '../../icons/index.ts'
import { Button } from '../button.tsx'

export interface KeyValueRow {
  name: string
  /** The value as stored; `null` for a name stored with no value. */
  value: string | null
}

export interface KeyValueTableProps {
  /** Names the table, such as `Headers`. */
  label: string
  rows: readonly KeyValueRow[]
  /** What a `null` value reads as; `No value` by default. */
  emptyValue?: string
}

/**
 * Names and their values in two aligned mono columns, such as a message's headers: the name column as wide as the
 * longest name, the value wrapping beside it with its copy control right after it. The header row is for assistive
 * technology only; the columns read as what they hold.
 */
export function KeyValueTable({ label, rows, emptyValue = 'No value' }: KeyValueTableProps) {
  return (
    // Container queries, not the viewport: the table is narrow in a side panel on a wide screen, where each row
    // stacks its name over its value instead of squeezing the value into a sliver.
    <div className="@container">
      <table aria-label={label} className="w-full border-collapse type-mono-small">
        <thead className="sr-only">
          <tr>
            <th scope="col">Name</th>
            <th scope="col">Value</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.name} className="border-b border-border last:border-b-0 @max-md:block @max-md:py-1">
              <th
                scope="row"
                className="py-1 pe-4 text-start align-top font-normal whitespace-nowrap text-faint @max-md:block @max-md:py-0"
              >
                <span className="inline-block min-h-(--control-height) leading-(--control-height) @max-md:min-h-0 @max-md:leading-[inherit]">
                  <bdi dir="ltr">{row.name}</bdi>
                </span>
              </th>
              <td className="w-full py-1 align-top wrap-anywhere @max-md:block @max-md:py-0">
                {/* The copy control sits right after its value, so the eye never crosses the table to find it. */}
                <span className="flex min-h-(--control-height) items-center gap-1">
                  {row.value === null ? (
                    <span className="text-faint">{emptyValue}</span>
                  ) : (
                    <>
                      <bdi dir="ltr">{row.value}</bdi>
                      <Button
                        variant="ghost"
                        icon={CopyIcon}
                        aria-label={`Copy ${row.name}`}
                        tooltip="Copy value"
                        onPress={() => void navigator.clipboard?.writeText(row.value!)}
                      />
                    </>
                  )}
                </span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
