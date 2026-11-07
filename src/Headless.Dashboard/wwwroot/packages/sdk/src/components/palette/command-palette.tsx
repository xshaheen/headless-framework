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
}

/** The command palette: views, commands, and objects, filtered as the operator types. */
export function CommandPalette({ isOpen, onOpenChange, items }: CommandPaletteProps) {
  const { contains } = useFilter({ sensitivity: 'base' })

  return (
    <ModalOverlay className="hd-scrim" isOpen={isOpen} onOpenChange={onOpenChange} isDismissable>
      <Modal className="hd-dialog hd-palette">
        <AriaDialog aria-label="Command palette" className="hd-palette-body">
          <Autocomplete filter={contains}>
            <SearchField aria-label="Search commands, views, and objects" autoFocus>
              <Input className="hd-palette-input" placeholder="Search commands, views, and objects" />
            </SearchField>
            <Menu
              className="hd-palette-list"
              items={items}
              renderEmptyState={() => <p className="hd-empty">No match. Paste an id to open it.</p>}
              onAction={(key) => {
                const item = items.find((candidate) => candidate.id === key)
                onOpenChange(false)
                item?.onAction()
              }}
            >
              {(item) => (
                <MenuItem id={item.id} textValue={item.objectId ? `${item.label} ${item.objectId}` : item.label} className="hd-palette-item">
                  <Text slot="label">
                    {item.label}
                    {item.objectId ? (
                      <>
                        {' '}
                        <Mono>{item.objectId}</Mono>
                      </>
                    ) : null}
                  </Text>
                  <span className="hd-palette-meta">
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
