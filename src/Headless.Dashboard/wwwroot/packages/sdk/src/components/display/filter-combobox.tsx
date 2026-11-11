// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { useContext, type ComponentProps, type ContextType } from 'react'
import {
  Button as AriaButton,
  ComboBox,
  Group,
  Header,
  Input,
  Label,
  ListBox,
  ListBoxItem,
  ListBoxSection,
  ListLayout,
  ListStateContext,
  Popover as AriaPopover,
  Virtualizer,
  type Key,
  type ListBoxItemProps,
} from 'react-aria-components'
import { DropdownIcon } from '../../icons/index.ts'
import { cn, floatingSurface } from '../cn.ts'

export interface FilterComboboxOption {
  value: string
  label: string
  /** The heading this option sits under, such as a function's module; options without one lead the list. */
  section?: string
}

export interface FilterComboboxProps {
  /** What the filter is on: `Function`. It names the field and is drawn faint before a chosen value. */
  label: string
  /** The value in the list's filter; `''` when the filter is off. */
  value: string
  onChange: (value: string) => void
  /** Every choice, in order; options that share a `section` are grouped under it in the order it first appears. */
  options: readonly FilterComboboxOption[]
  /** What the field reads while the filter is off, and the choice that turns it off: `Any function`. */
  emptyLabel?: string
}

/** No option has this key, so the empty choice stays apart from a served value that happens to be empty. */
const emptyKey = '\u0000any'

// The virtualizer lays rows out before they render, so the option and heading heights are fixed here and in the
// classes below (`h-7`, `h-6`), and the list's padding is a number it adds rather than a class.
const rowHeight = 28
const headingHeight = 24
const listPadding = 4

// The field matches `FilterSelect`'s chip: the control height, its border and radius, and the set state of a pressed
// filter, so a chosen filter reads as on beside the other chips.
const fieldClass =
  'inline-flex h-(--control-height) w-56 max-w-(--filter-max-width) items-center gap-1 rounded-md border border-border bg-transparent ps-2 pe-0.5 type-small text-muted-foreground transition-fast focus-within:outline-solid focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-ring hover:border-edge data-[set]:border-primary data-[set]:bg-primary-soft data-[set]:text-foreground'
const optionClass =
  'flex h-7 cursor-pointer items-center gap-2 truncate rounded-sm px-2 type-small text-foreground outline-none data-focused:bg-accent data-selected:font-medium'

type ListCollection = NonNullable<ContextType<typeof ListStateContext>>['collection']

const positionsByCollection = new WeakMap<ListCollection, Map<Key, number>>()

/** Each option's place among all the options, counted from 1 across sections and past their headings. */
function optionPositions(collection: ListCollection) {
  let positions = positionsByCollection.get(collection)

  if (positions) {
    return positions
  }

  positions = new Map<Key, number>()
  let count = 0

  for (const node of collection) {
    for (const child of node.type === 'section' ? (collection.getChildren?.(node.key) ?? []) : [node]) {
      if (child.type === 'item') {
        positions.set(child.key, ++count)
      }
    }
  }

  positionsByCollection.set(collection, positions)

  return positions
}

type OptionRender = NonNullable<ListBoxItemProps<object>['render']>
type OptionDomProps = Parameters<OptionRender>[0]

/**
 * An option's element in the virtualized list. Only the options in view are in the page, so React Aria gives each its
 * place for a screen reader (`3 of 1,000`), but inside a section it counts from the section's heading, so the first
 * option of every section reads as the second of the whole list. This puts its place in the whole list there instead.
 */
function VirtualizedOption({ domProps }: { domProps: OptionDomProps }) {
  const state = useContext(ListStateContext)
  const key = (domProps as { 'data-key'?: Key })['data-key']
  const position = state && key !== undefined ? optionPositions(state.collection).get(key) : undefined

  return <div {...(domProps as ComponentProps<'div'>)} aria-posinset={position ?? domProps['aria-posinset']} />
}

function renderOption(domProps: OptionDomProps) {
  return <VirtualizedOption domProps={domProps} />
}

function grouped(options: readonly FilterComboboxOption[]) {
  const loose: FilterComboboxOption[] = []
  const sections = new Map<string, FilterComboboxOption[]>()

  for (const option of options) {
    if (option.section === undefined) {
      loose.push(option)
    } else {
      const section = sections.get(option.section) ?? []

      section.push(option)
      sections.set(option.section, section)
    }
  }

  return { loose, sections: [...sections] }
}

/**
 * A list filter whose choices are too many to scan, such as every function a host runs: type to narrow the list, then
 * pick with the arrow keys and `Enter`, or with a press. It reads as `FilterSelect`'s chip does, its value in the set
 * state, and its list is virtualized, so a thousand choices open as fast as ten. Options group under their `section`
 * heading, and a screen reader hears each one's place in the whole list. `Esc` closes the list, then restores the
 * chosen value's text. The component lives in its own `/combobox` entry, so only a view that filters this way loads
 * React Aria's combobox.
 */
export function FilterCombobox({ label, value, onChange, options, emptyLabel = 'Any' }: FilterComboboxProps) {
  const chosen = options.find((option) => option.value === value)
  const { loose, sections } = grouped(options)

  function item(option: FilterComboboxOption) {
    return (
      <ListBoxItem key={option.value} id={option.value} textValue={option.label} className={optionClass} render={renderOption}>
        {option.label}
      </ListBoxItem>
    )
  }

  return (
    <ComboBox
      className="inline-flex min-w-0"
      selectedKey={chosen ? chosen.value : emptyKey}
      onSelectionChange={(key) => onChange(key === null || key === emptyKey ? '' : String(key))}
    >
      <Label className="sr-only">{label}</Label>
      <Group className={fieldClass} data-set={chosen ? '' : undefined}>
        {chosen ? (
          <span aria-hidden="true" className="shrink-0 text-faint">
            {label}
          </span>
        ) : null}
        <Input
          placeholder={emptyLabel}
          className="h-full min-w-0 flex-1 bg-transparent text-current outline-none placeholder:text-muted-foreground"
        />
        <AriaButton className="grid size-6 shrink-0 cursor-pointer place-content-center rounded-sm text-current outline-none">
          <DropdownIcon className="size-(--icon-sm) text-current" />
        </AriaButton>
      </Group>
      <AriaPopover
        className={cn(
          'z-(--z-popover) w-(--trigger-width) min-w-56 max-w-(--filter-max-width) rounded-lg',
          floatingSurface,
        )}
      >
        <Virtualizer
          layout={ListLayout}
          layoutOptions={{ rowSize: rowHeight, headingSize: headingHeight, padding: listPadding }}
        >
          <ListBox
            className="h-[min(50vh,20rem)] overflow-auto outline-none"
            renderEmptyState={() => <p className="px-2 py-1.5 type-small text-muted-foreground">No matches</p>}
          >
            <ListBoxItem id={emptyKey} textValue={emptyLabel} className={optionClass} render={renderOption}>
              {emptyLabel}
            </ListBoxItem>
            {loose.map(item)}
            {sections.map(([name, members]) => (
              <ListBoxSection key={name} id={`section:${name}`}>
                <Header className="flex h-6 items-center truncate px-2 type-label text-faint">{name}</Header>
                {members.map(item)}
              </ListBoxSection>
            ))}
          </ListBox>
        </Virtualizer>
      </AriaPopover>
    </ComboBox>
  )
}
