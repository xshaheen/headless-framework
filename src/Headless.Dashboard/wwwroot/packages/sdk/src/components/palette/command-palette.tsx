// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import {
  Autocomplete,
  Dialog as AriaDialog,
  Input,
  Menu,
  MenuItem,
  Modal,
  ModalOverlay,
  SearchField,
  Text,
  useFilter,
} from 'react-aria-components'
import { dialogScrim, dialogSurface } from '../dialog.tsx'
import { Kbd, Mono } from '../marks.tsx'

export interface PaletteItem {
  id: string
  label: string
  /** What the item is: `View`, `Command`, `Run`. */
  kind?: string
  /** The object's id, shown mono and bidi-isolated after the label, so a right-to-left label never reorders it. */
  objectId?: string
  shortcut?: string
  onAction: () => void
}

export interface CommandPaletteProps {
  isOpen: boolean
  onOpenChange: (isOpen: boolean) => void
  items: readonly PaletteItem[]
  /** The search it opens with, such as an id the top bar's go to id matched more than once. */
  defaultQuery?: string
}

/** The command palette: views, commands, and objects, filtered as the operator types. */
export function CommandPalette({ isOpen, onOpenChange, items, defaultQuery }: CommandPaletteProps) {
  const { contains } = useFilter({ sensitivity: 'base' })

  return (
    <ModalOverlay className={dialogScrim} isOpen={isOpen} onOpenChange={onOpenChange} isDismissable>
      <Modal className={dialogSurface}>
        <AriaDialog aria-label="Command palette" className="flex flex-col outline-none">
          <Autocomplete filter={contains} defaultInputValue={defaultQuery}>
            <SearchField aria-label="Search commands, views, and objects" autoFocus>
              <Input
                className="h-(--palette-input-height) w-full border-b border-border bg-transparent px-4 outline-none placeholder:text-faint"
                placeholder="Search commands, views, and objects" />
            </SearchField>
            <Menu
              className="overflow-auto p-1.5 outline-none"
              items={items}
              renderEmptyState={() => <p className="mx-auto max-w-[46ch] py-5 text-center text-muted-foreground">No match. Paste an id to open it.</p>}
              onAction={(key) => {
                const item = items.find((candidate) => candidate.id === key)
                onOpenChange(false)
                item?.onAction()
              }}
            >
              {(item) => (
                <MenuItem id={item.id} textValue={item.objectId ? `${item.label} ${item.objectId}` : item.label} className="flex cursor-pointer items-center justify-between gap-3 rounded-md px-3 py-2 outline-none data-focused:bg-primary-soft"
                >
                  <Text slot="label">
                    {item.label}
                    {item.objectId ? (
                      <>
                        {' '}
                        <Mono>{item.objectId}</Mono>
                      </>
                    ) : null}
                  </Text>
                  <span className="inline-flex items-center gap-2">
                    {item.kind ? <span className="hd-hint">{item.kind}</span> : null}
                    {item.shortcut ? <Kbd>{item.shortcut}</Kbd> : null}
                  </span>
                </MenuItem>
              )}
            </Menu>
          </Autocomplete>
        </AriaDialog>
      </Modal>
    </ModalOverlay>
  )
}
