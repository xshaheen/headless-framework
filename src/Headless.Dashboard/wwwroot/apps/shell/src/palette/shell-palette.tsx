// The React Compiler's memo caches cost the shell more bytes than these rare renders save, and the shell's assets
// sit near their size budget.
'use no memo'

import { CommandPalette, useSelectionCommands, type PaletteItem } from '@headless/dashboard-sdk/components'
import { useNavigate } from '@headless/dashboard-sdk/router'
import { useState, type FormEvent } from 'react'
import { moduleSidebarEntries, type BootstrapState } from '../bootstrap.ts'
import { modulePath, productHome } from '../route-handles.ts'
import { useOpenObject, useShell } from '../shell-context.tsx'
import { useResolve } from '../shell-data.ts'

/** The palette's static items: Attention, each product, every module's sidebar entries, and every client command the user may run. */
function staticItems(
  state: BootstrapState,
  navigate: (to: string) => void,
  runCommand: (moduleId: string, commandId: string) => void,
  openAttention: () => void,
): PaletteItem[] {
  const items: PaletteItem[] = [
    { id: 'shell.attention', label: 'Open Attention', kind: 'View', shortcut: 'G N', onAction: openAttention },
    ...state.products.map((product) => ({
      id: `product:${product.id}`,
      label: `Go to ${product.label}`,
      kind: 'View',
      onAction: () => navigate(productHome(product)),
    })),
  ]

  // A command labelled like a view is a second way to the same place; the palette lists each destination once.
  const labels = new Set(items.map((item) => item.label.toLocaleLowerCase()))

  for (const module of state.modules) {
    if (module.refusal) {
      continue
    }

    for (const entry of moduleSidebarEntries(state, module.id)) {
      if (entry.requires === 'operate' && !module.canOperate) {
        continue
      }

      const label = entry.label === module.displayName ? `Go to ${entry.label}` : `Go to ${module.displayName} ${entry.label}`
      labels.add(label.toLocaleLowerCase())
      items.push({ id: `nav:${entry.id}`, label, kind: 'View', onAction: () => navigate(modulePath(module.id, entry.route)) })
    }
  }

  for (const module of state.modules) {
    if (module.refusal) {
      continue
    }

    for (const command of module.commands) {
      // A viewer is not offered a command the server would refuse; the module's own views show it disabled.
      if ((command.requires === 'operate' && !module.canOperate) || labels.has(command.label.toLocaleLowerCase())) {
        continue
      }

      items.push({
        id: `command:${module.id}:${command.id}`,
        label: command.label,
        kind: module.displayName,
        onAction: () => runCommand(module.id, command.id),
      })
    }
  }

  return items
}

interface ShellPaletteProps {
  isOpen: boolean
  onOpenChange: (isOpen: boolean) => void
  /** The search it opens with: an id go to id matched in more than one place. */
  initialQuery?: string
  onOpenAttention: () => void
}

/**
 * The shell's command palette: views, module sidebar entries, and client commands from the inlined manifest, the
 * server commands the view's selection allows now, and the objects a pasted id resolves to in any module, looked up
 * once typing settles.
 */
export function ShellPalette({ isOpen, onOpenChange, initialQuery = '', onOpenAttention }: ShellPaletteProps) {
  const { state, loader } = useShell()
  const navigate = useNavigate()
  const openObject = useOpenObject()
  const selectionCommands = useSelectionCommands()
  const [query, setQuery] = useState(initialQuery)
  const found = useResolve(query, isOpen) ?? []

  function runCommand(moduleId: string, commandId: string) {
    const module = state.modules.find((candidate) => candidate.id === moduleId)

    if (!module) {
      return
    }

    // The module's entry loads now, on the command's first run; a failed load shows the shell's reload notice.
    void loader
      .loadEntry(module)
      .then((definition) =>
        definition.commands
          ?.find((command) => command.id === commandId)
          ?.run({ navigate: (to) => void navigate(modulePath(module.id, to)) }),
      )
      .catch(() => undefined)
  }

  const objects: PaletteItem[] = found.map((object) => ({
    id: `object:${object.target.module}:${object.target.kind}:${object.target.id}`,
    label: object.title,
    // The palette's filter matches the id too, so it keeps the object the pasted id found.
    objectId: object.target.id,
    kind: state.modules.find((module) => module.id === object.target.module)?.displayName ?? object.target.module,
    onAction: () => void openObject(object.target).catch(() => undefined),
  }))

  // The view offers only the commands the server allows its selection now, so a refused one is absent here.
  const selection: PaletteItem[] = selectionCommands().map((command) => ({
    id: `selection:${command.id}`,
    label: command.label,
    kind: 'Selection',
    onAction: command.run,
  }))
  const items = [...objects, ...selection, ...staticItems(state, (to) => void navigate(to), runCommand, onOpenAttention)]

  function onInput(event: FormEvent<HTMLDivElement>) {
    if (event.target instanceof HTMLInputElement) {
      setQuery(event.target.value)
    }
  }

  return (
    // React events bubble through the palette's portal, so this wrapper hears what the operator types.
    <div className="hd-palette-host" onInput={onInput}>
      <CommandPalette
        isOpen={isOpen}
        onOpenChange={(open) => {
          if (!open) {
            setQuery('')
          }

          onOpenChange(open)
        }}
        items={items}
        defaultQuery={initialQuery}
      />
    </div>
  )
}
