// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'
import { FilterIcon } from '../../icons/index.ts'
import { below, useBelow } from '../breakpoints.ts'
import { Popover } from '../popover.tsx'

export interface FilterToolbarProps {
  /** Names the group for assistive technology; `Filters` by default. */
  label?: string
  /**
   * `FilterChips` that lead the row and stay in view at every width: the Failed list's causes, and the set filters a
   * link brought with their `Clear`. They wrap onto further lines before anything else does.
   */
  chips?: ReactNode
  /** The list's `SearchField`, which follows the chips and takes the width a phone has. */
  search?: ReactNode
  /** The filters that stay on the row: `FilterSelect`s and the `compact` `DateRangeField`. */
  children?: ReactNode
  /**
   * Filters that sit behind `More filters`, such as a day range, so the row stays one line beside the title at
   * 1024 px. On a phone every filter sits behind one `Filters` button.
   */
  more?: ReactNode
  /** How many filters are set, read on the phone's `Filters` button and the `More filters` button. */
  active?: number
}

/**
 * The row of filters under a page's title: the search, the filter chips, and an overflow. At 1024 px and wider the
 * chips sit on one line, with the `more` filters behind a `More filters` button; below 768 px the search keeps the
 * width and every filter sits behind `Filters`, with its count, so the header never stacks four rows of controls over
 * the list.
 */
export function FilterToolbar({ label = 'Filters', chips, search, children, more, active = 0 }: FilterToolbarProps) {
  const phone = useBelow(below.phone)
  const count = active > 0 ? ` ${active}` : ''
  const hasFilters = children != null || more != null

  return (
    <div role="group" aria-label={label} className="flex min-w-0 flex-wrap items-center gap-2 [&>.hd-search]:max-md:basis-full">
      {chips}
      {search}
      {phone ? (
        hasFilters ? (
          <Popover label={label} icon={FilterIcon} trigger={`Filters${count}`}>
            <div className="flex flex-col items-start gap-2">
              {children}
              {more}
            </div>
          </Popover>
        ) : null
      ) : (
        <>
          {children}
          {more != null ? (
            <Popover label="More filters" icon={FilterIcon} trigger={`More filters${count}`}>
              <div className="flex flex-col items-start gap-2">{more}</div>
            </Popover>
          ) : null}
        </>
      )}
    </div>
  )
}
