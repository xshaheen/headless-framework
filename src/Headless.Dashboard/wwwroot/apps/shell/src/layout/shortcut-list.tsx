// The React Compiler's memo caches cost the shell more bytes than these rare renders save, and the shell's assets
// sit near their size budget.
'use no memo'

import { Button, Dialog, Kbd, shellShortcuts, useModuleShortcuts } from '@headless/dashboard-sdk/components'
import { Fragment } from 'react'
import { useShell } from '../shell-context.tsx'

/** Every shortcut on one sheet: the shell's, then those the open view's module binds now. `?` opens it. */
export function ShortcutList({ isOpen, onOpenChange }: { isOpen: boolean; onOpenChange: (isOpen: boolean) => void }) {
  const moduleShortcuts = useModuleShortcuts()
  const { modules } = useShell().state
  const rows = [
    ...shellShortcuts.filter((row) => !row.module || modules.some((module) => module.id === row.module)),
    ...(isOpen ? moduleShortcuts().map((shortcut) => ({ keys: [shortcut.key], action: shortcut.label })) : []),
  ]

  return (
    <Dialog isOpen={isOpen} onOpenChange={onOpenChange} title="Keyboard shortcuts">
      <dl className="hd-kv">
        {rows.map((row) => (
          <Fragment key={`${row.keys.join(' ')} ${row.action}`}>
            <dt>
              {row.keys.map((key, index) => (
                <Fragment key={key}>
                  {index > 0 ? ' / ' : null}
                  <Kbd>{key}</Kbd>
                </Fragment>
              ))}
            </dt>
            <dd>{row.action}</dd>
          </Fragment>
        ))}
      </dl>
      <p className="hd-hint">Use Ctrl in place of ⌘ off macOS. Shortcuts wait while a text field has focus, except Esc and ⌘K.</p>
      <div className="hd-actions">
        <Button onPress={() => onOpenChange(false)}>Close</Button>
      </div>
    </Dialog>
  )
}
