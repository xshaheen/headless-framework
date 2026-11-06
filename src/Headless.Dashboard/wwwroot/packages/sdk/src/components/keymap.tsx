import { createContext, useContext, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react'

/**
 * Every shell shortcut, in the order the shortcut list shows them. Use `Ctrl` in place of `⌘` off macOS. A `G` chord
 * into a module names it, and the list shows the chord only when that module is registered.
 */
export const shellShortcuts: readonly { keys: readonly string[]; action: string; module?: string }[] = [
  { keys: ['⌘K'], action: 'Command palette' },
  { keys: ['/'], action: 'Go to id' },
  { keys: ['J', 'K'], action: 'Next / previous row' },
  { keys: ['Enter'], action: 'Open the row' },
  { keys: ['X'], action: 'Check or uncheck the row' },
  { keys: ['Esc'], action: 'Close the popover or menu, then the details, then clear checked rows' },
  { keys: ['R'], action: 'Primary recovery action (requeue or re-execute)' },
  { keys: ['T'], action: 'Open the trace' },
  { keys: ['G O'], action: 'Go to the product’s dashboard' },
  { keys: ['G N'], action: 'Open Attention' },
  { keys: ['G R'], action: 'Go to runs', module: 'jobs' },
  { keys: ['G M'], action: 'Go to received messages', module: 'messaging' },
  { keys: ['[', ']'], action: 'Widen / narrow the strip’s time window' },
  { keys: ['?'], action: 'Shortcut list' },
]

/** The keys the shell owns; a module binding may not reuse one. */
const shellKeys = new Set(['k', 'escape', '/', 'j', 'x', 'r', 't', 'g', '[', ']', '?'])

/**
 * What the mounted view does on a shell key: its list moves and selects, its trace opens,
 * `R` runs the selection's recovery command, and `[`/`]` widen and narrow its strip's window.
 */
export interface ViewKeyHandlers {
  next?: () => void
  previous?: () => void
  toggle?: () => void
  trace?: () => void
  recover?: () => void
  widen?: () => void
  narrow?: () => void
}

/** A server command the selection allows now, offered in the palette beside navigation. */
export interface SelectionCommand {
  id: string
  label: string
  run: () => void
}

/** What the shell itself does on its keys: the palette, go to id, the shortcut list, and the `G` chords by letter. */
export interface ShellKeyHandlers {
  palette: () => void
  goTo: () => void
  shortcuts: () => void
  go: Readonly<Record<string, () => void>>
}

/** A module's own single-key shortcut, listed in the shortcut list beside the shell's. */
export interface ModuleShortcut {
  key: string
  label: string
}

interface Registration<T> {
  read: () => T
}

interface Keymap {
  views: Registration<ViewKeyHandlers>[]
  shell: Registration<ShellKeyHandlers>[]
  modules: Registration<ModuleShortcut & { run: () => void }>[]
  commands: Registration<SelectionCommand | null>[]
}

const KeymapContext = createContext<Keymap | null>(null)

// A select is not a text field: a view's filter is often one, and the `G` chords must still work there.
function isTextField(target: EventTarget | null) {
  return target instanceof HTMLElement && (target.isContentEditable || target.tagName === 'INPUT' || target.tagName === 'TEXTAREA')
}

/** A popover, menu, or dialog above the view keeps its own keys; the view's shortcuts would act behind it. */
function isInOverlay(target: EventTarget | null) {
  return target instanceof Element && target.closest('[role="dialog"], [role="alertdialog"], [role="menu"], [role="listbox"]') !== null
}

/** The newest registration that handles `pick`, so the view mounted last answers its keys. */
function latest<T, R>(registrations: readonly Registration<T>[], pick: (value: T) => R | undefined): R | undefined {
  for (let index = registrations.length - 1; index >= 0; index--) {
    const found = pick(registrations[index]!.read())

    if (found !== undefined) {
      return found
    }
  }

  return undefined
}

/** Registers `value` while the calling component is mounted, always reading its newest render. */
function useRegistration<T>(list: (keymap: Keymap) => Registration<T>[], value: T, enabled = true) {
  const keymap = useContext(KeymapContext)
  const current = useRef(value)

  useLayoutEffect(() => {
    current.current = value
  })

  useEffect(() => {
    if (!keymap || !enabled) {
      return undefined
    }

    const registrations = list(keymap)
    const registration: Registration<T> = { read: () => current.current }
    registrations.push(registration)

    return () => {
      registrations.splice(registrations.indexOf(registration), 1)
    }
    // The list selector is a fixed accessor per hook; the value is read through the ref.
  }, [keymap, enabled])
}

/**
 * The dashboard's one keymap: a single listener that dispatches every shell key. No shortcut fires while a text field
 * has focus, except `⌘K` (`Ctrl+K`); `Esc` is left to the popover, list, or inspector that has focus.
 */
export function KeymapProvider({ children }: { children: ReactNode }) {
  const [keymap] = useState<Keymap>(() => ({ views: [], shell: [], modules: [], commands: [] }))

  useEffect(() => {
    let chord: ReturnType<typeof setTimeout> | undefined
    let armed = false

    function run(event: KeyboardEvent, action: (() => void) | undefined) {
      if (!action) {
        return
      }

      // Taken before the focused list sees it, so a list's type-to-select never jumps on the same letter.
      event.preventDefault()
      event.stopPropagation()
      action()
    }

    function onKeyDown(event: KeyboardEvent) {
      const key = event.key.toLowerCase()

      if ((event.metaKey || event.ctrlKey) && !event.altKey && key === 'k') {
        run(event, latest(keymap.shell, (shell) => shell.palette))
        return
      }

      if (event.metaKey || event.ctrlKey || event.altKey || isTextField(event.target) || isInOverlay(event.target)) {
        return
      }

      if (armed) {
        armed = false
        clearTimeout(chord)
        run(event, latest(keymap.shell, (shell) => shell.go[key]))
        return
      }

      switch (key) {
        case '?':
          run(event, latest(keymap.shell, (shell) => shell.shortcuts))
          return
        case '/':
          run(event, latest(keymap.shell, (shell) => shell.goTo))
          return
        case 'j':
          run(event, latest(keymap.views, (view) => view.next))
          return
        case 'k':
          run(event, latest(keymap.views, (view) => view.previous))
          return
        case 'x':
          run(event, latest(keymap.views, (view) => view.toggle))
          return
        case 't':
          run(event, latest(keymap.views, (view) => view.trace))
          return
        case 'r':
          run(event, latest(keymap.views, (view) => view.recover))
          return
        case '[':
          run(event, latest(keymap.views, (view) => view.widen))
          return
        case ']':
          run(event, latest(keymap.views, (view) => view.narrow))
          return
        case 'g':
          armed = true
          chord = setTimeout(() => {
            armed = false
          }, 1_000)
          return
        default:
          run(event, latest(keymap.modules, (binding) => (binding.key === key ? binding.run : undefined)))
      }
    }

    // Capture phase: the keymap hears a key before any list or field under the document handles it.
    document.addEventListener('keydown', onKeyDown, true)

    return () => {
      clearTimeout(chord)
      document.removeEventListener('keydown', onKeyDown, true)
    }
  }, [keymap])

  return <KeymapContext.Provider value={keymap}>{children}</KeymapContext.Provider>
}

/**
 * Answers the shell keys for the calling view while it is mounted. Outside the dashboard (a component rendered on
 * its own) there is no keymap, and the keys do nothing.
 */
export function useViewKeys(handlers: ViewKeyHandlers, enabled = true) {
  useRegistration((keymap) => keymap.views, handlers, enabled)
}

/** The shell's own keys: the palette, go to id, the shortcut list, and the `G` chords. */
export function useShellKeys(handlers: ShellKeyHandlers) {
  useRegistration((keymap) => keymap.shell, handlers)
}

/**
 * A module's single-key shortcut while the calling view is mounted. A key the shell owns is refused, because the
 * shell's binding must mean the same thing on every screen.
 */
export function useShortcut(shortcut: ModuleShortcut, run: () => void) {
  const key = shortcut.key.toLowerCase()

  if (key.length !== 1 || shellKeys.has(key)) {
    throw new Error(
      shellKeys.has(key)
        ? `The shortcut "${shortcut.key}" is a shell key; a module's shortcuts may not reuse one.`
        : `The shortcut "${shortcut.key}" is not a single key.`,
    )
  }

  useRegistration((keymap) => keymap.modules, { key, label: shortcut.label, run })
}

/** The module shortcuts bound now, for the shortcut list. */
export function useModuleShortcuts(): () => readonly ModuleShortcut[] {
  const keymap = useContext(KeymapContext)

  return () => (keymap?.modules ?? []).map((registration) => {
    const { key, label } = registration.read()

    return { key: key.toUpperCase(), label }
  })
}

/** Offers `command` in the palette while the calling component is mounted; `null` offers nothing. */
export function useSelectionCommand(command: SelectionCommand | null) {
  useRegistration((keymap) => keymap.commands, command, command !== null)
}

/** The selection's commands offered now, for the palette. */
export function useSelectionCommands(): () => readonly SelectionCommand[] {
  const keymap = useContext(KeymapContext)

  return () => (keymap?.commands ?? []).flatMap((registration) => registration.read() ?? [])
}
