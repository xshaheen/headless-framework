// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import {
  Button as AriaButton,
  Label,
  ListBox,
  ListBoxItem,
  Popover as AriaPopover,
  Select,
  SelectValue,
  type Key,
} from 'react-aria-components'
import { DropdownIcon } from '../../icons/index.ts'
import { cn, floatingSurface } from '../cn.ts'

export interface FilterSelectProps {
  /** What the filter is on: `Kind`, `Function`. It is read, and drawn faint before a chosen value. */
  label: string
  /** The value in the list's filter; `''` when the filter is off. */
  value: string
  onChange: (value: string) => void
  options: readonly { value: string; label: string }[]
  /** What the chip reads while the filter is off, and the choice that turns it off: `Any kind`. */
  emptyLabel?: string
  /** A choice that is always made, such as the node a view reads: no empty choice, and the chip reads the value. */
  isRequired?: boolean
}

/** No option has this key, so the empty choice stays apart from a served value that happens to be empty. */
const emptyKey = '\u0000any'

// One control height, the chip's border and radius, and the set state of a pressed filter chip, so a chosen filter
// reads as on beside the chips that toggle.
const chipClass =
  'inline-flex h-(--control-height) max-w-(--filter-max-width) cursor-pointer items-center gap-1.5 rounded-md border border-border bg-transparent px-2 type-small whitespace-nowrap text-muted-foreground transition-fast outline-none hover:border-edge hover:text-foreground data-focus-visible:outline-2 data-focus-visible:outline-offset-2 data-focus-visible:outline-ring data-[set]:border-primary data-[set]:bg-primary-soft data-[set]:text-foreground'
const optionClass =
  'flex cursor-pointer items-center gap-2 rounded-sm px-2 py-1 type-small text-foreground outline-none data-focused:bg-accent data-selected:font-medium'

/**
 * A list filter as a chip that opens a list of choices: the platform's own select would match nothing else in a
 * page header. Off, it reads its `emptyLabel`; on, it reads the filter's name faint and the chosen value, in the set
 * state a pressed chip has. It is a React Aria select, so arrow keys move through the choices, typing jumps to one,
 * and `Esc` closes the list and returns focus to the chip.
 */
export function FilterSelect({ label, value, onChange, options, emptyLabel = 'Any', isRequired = false }: FilterSelectProps) {
  const chosen = options.find((option) => option.value === value)
  const set = chosen !== undefined && (!isRequired || value !== '')

  function change(key: Key | null) {
    onChange(key === null || key === emptyKey ? '' : String(key))
  }

  return (
    <Select
      className="inline-flex min-w-0"
      selectedKey={chosen ? chosen.value : emptyKey}
      onSelectionChange={change}
      placeholder={emptyLabel}
    >
      <Label className="sr-only">{label}</Label>
      <AriaButton className={chipClass} data-set={set && !isRequired ? '' : undefined}>
        <SelectValue className="flex min-w-0 items-baseline gap-1.5 [&>span]:truncate">
          {({ isPlaceholder, selectedText }) =>
            isPlaceholder || !chosen ? (
              <span>{emptyLabel}</span>
            ) : isRequired ? (
              <span>{selectedText}</span>
            ) : (
              <>
                <span className="text-faint">{label}</span>
                <span>{selectedText}</span>
              </>
            )
          }
        </SelectValue>
        <DropdownIcon className="size-(--icon-sm) text-current" />
      </AriaButton>
      <AriaPopover className={cn('z-(--z-popover) min-w-(--trigger-width) max-w-(--filter-max-width) rounded-lg p-1', floatingSurface)}>
        <ListBox className="max-h-[50vh] overflow-auto outline-none">
          {isRequired ? null : (
            <ListBoxItem id={emptyKey} className={optionClass}>
              {emptyLabel}
            </ListBoxItem>
          )}
          {options.map((option) => (
            <ListBoxItem key={option.value} id={option.value} className={optionClass} textValue={option.label}>
              {option.label}
            </ListBoxItem>
          ))}
        </ListBox>
      </AriaPopover>
    </Select>
  )
}
