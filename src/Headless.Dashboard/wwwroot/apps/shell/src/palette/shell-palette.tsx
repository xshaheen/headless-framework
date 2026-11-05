import { CommandPalette, type PaletteItem } from '@headless/dashboard-sdk/components'
import { useDashboardApi } from '@headless/dashboard-sdk/data'
import { useNavigate } from '@headless/dashboard-sdk/router'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { BootstrapState } from '../bootstrap.ts'
import { useOpenObject, useShell } from '../shell-context.tsx'
import type { ModuleSection, ResolvedObject } from '../shell-data.ts'

/** How long typing settles before a pasted id is looked up in every module. */
const resolveDelayMs = 250

/** A query that may be an id: one token, long enough not to ask every module about each keystroke. */
function looksLikeId(query: string) {
  const text = query.trim()

  return text.length >= 3 && text.length <= 256 && !/\s/.test(text)
}

function modulePath(id: string, to: string) {
  const rest = to.replace(/^\/+|\/+$/g, '')

  return rest ? `/${id}/${rest}` : `/${id}`
}

/** The palette's static items: the shell's views, every module's nav, and every client command the user may run. */
function staticItems(
  state: BootstrapState,
  navigate: (to: string) => void,
  runCommand: (moduleId: string, commandId: string) => void,
): PaletteItem[] {
  const items: PaletteItem[] = [
    { id: 'shell.overview', label: 'Go to Overview', kind: 'View', shortcut: 'G O', onAction: () => navigate('/') },
    { id: 'shell.needs-you', label: 'Go to Needs you', kind: 'View', shortcut: 'G N', onAction: () => navigate('/needs-you') },
  ]

  // A command labelled like a view is a second way to the same place; the palette lists each destination once.
  const labels = new Set(items.map((item) => item.label.toLocaleLowerCase()))

  for (const module of state.modules) {
    if (module.refusal) {
      continue
    }

    for (const entry of module.nav) {
      if (entry.requires === 'operate' && !module.canOperate) {
        continue
      }

      const label = entry.label === module.displayName ? `Go to ${entry.label}` : `Go to ${module.displayName} ${entry.label}`
      labels.add(label.toLocaleLowerCase())
      items.push({ id: `nav:${entry.id}`, label, kind: 'View', onAction: () => navigate(modulePath(module.id, entry.to)) })
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

/**
 * The shell's command palette: views, module nav, and client commands from the inlined manifest, plus the objects a
 * pasted id resolves to in any module, looked up once typing settles.
 */
export function ShellPalette({ isOpen, onOpenChange }: { isOpen: boolean; onOpenChange: (isOpen: boolean) => void }) {
  const { state, loader } = useShell()
  const api = useDashboardApi()
  const navigate = useNavigate()
  const openObject = useOpenObject()
  const [query, setQuery] = useState('')
  const [found, setFound] = useState<{ query: string; objects: ResolvedObject[] }>({ query: '', objects: [] })
  const latest = useRef(0)

  useEffect(() => {
    if (!isOpen || !looksLikeId(query)) {
      return undefined
    }

    const request = ++latest.current
    const timer = setTimeout(() => {
      api
        .request<{ modules: ModuleSection<ResolvedObject>[] }>({ method: 'GET', path: 'resolve', query: { q: query.trim() } })
        .then((answer) => {
          // Only the answer to the newest query may land; an older one would show objects for text no longer typed.
          if (request === latest.current) {
            setFound({ query, objects: answer.modules.flatMap((section) => section.items) })
          }
        })
        .catch(() => {
          if (request === latest.current) {
            setFound({ query, objects: [] })
          }
        })
    }, resolveDelayMs)

    return () => clearTimeout(timer)
  }, [api, isOpen, query])

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

  const objects: PaletteItem[] =
    found.query === query
      ? found.objects.map((object) => ({
          id: `object:${object.target.module}:${object.target.kind}:${object.target.id}`,
          label: object.title,
          // The palette's filter matches the id too, so it keeps the object the pasted id found.
          objectId: object.target.id,
          kind: state.modules.find((module) => module.id === object.target.module)?.displayName ?? object.target.module,
          onAction: () => void openObject(object.target).catch(() => undefined),
        }))
      : []

  const items = [...objects, ...staticItems(state, (to) => void navigate(to), runCommand)]

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
      />
    </div>
  )
}
