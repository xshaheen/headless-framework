import { OperationHost } from '@headless/dashboard-sdk/commands'
import { ToastRegion, usePaletteShortcut } from '@headless/dashboard-sdk/components'
import { Outlet, useNavigate } from '@headless/dashboard-sdk/router'
import { useCallback, useEffect, useState } from 'react'
import { ShellPalette } from '../palette/shell-palette.tsx'
import { useShell } from '../shell-context.tsx'
import { UpdatedNotice } from '../views/module-error.tsx'
import { DocumentBar } from './document-bar.tsx'
import { DockProvider } from './inspector-dock.tsx'
import { Rail } from './rail.tsx'
import './shell.css'

function isTyping(target: EventTarget | null) {
  return (
    target instanceof HTMLElement &&
    (target.isContentEditable || target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.tagName === 'SELECT')
  )
}

/** `G` then a letter goes to a view: `G O` Overview, `G N` Needs you. No shortcut fires while a field has focus. */
function useGoShortcuts() {
  const navigate = useNavigate()

  useEffect(() => {
    let pending: ReturnType<typeof setTimeout> | undefined
    let armed = false

    function onKeyDown(event: KeyboardEvent) {
      if (event.metaKey || event.ctrlKey || event.altKey || isTyping(event.target)) {
        return
      }

      const key = event.key.toLowerCase()

      if (armed) {
        armed = false
        clearTimeout(pending)
        const to = key === 'o' ? '/' : key === 'n' ? '/needs-you' : undefined

        if (to) {
          event.preventDefault()
          void navigate(to)
        }

        return
      }

      if (key === 'g') {
        armed = true
        pending = setTimeout(() => {
          armed = false
        }, 1_000)
      }
    }

    document.addEventListener('keydown', onKeyDown)

    return () => {
      clearTimeout(pending)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [navigate])
}

/**
 * The shell's layout around every signed-in view: the rail, the document bar, Operations docked below it, the
 * canvas, and the inspector dock. Operations live here, outside any route, so a running one keeps its chunks going
 * and its Stop in view on every view.
 */
export function ShellLayout() {
  const { loader } = useShell()
  const [railOpen, setRailOpen] = useState(false)
  const [paletteOpen, setPaletteOpen] = useState(false)
  const [dock, setDock] = useState<HTMLElement | null>(null)
  const [updated, setUpdated] = useState(false)

  const openPalette = useCallback(() => setPaletteOpen(true), [])

  usePaletteShortcut(openPalette)
  useGoShortcuts()
  useEffect(() => loader.onError(() => setUpdated(true)), [loader])

  return (
    <div className="hd-shell">
      <a className="hd-skip" href="#hd-canvas">
        Skip to content
      </a>
      <Rail isOpen={railOpen} onNavigate={() => setRailOpen(false)} />
      {railOpen ? <div className="hd-rail-scrim" aria-hidden="true" onClick={() => setRailOpen(false)} /> : null}
      <div className="hd-main">
        <OperationHost>
          <DocumentBar isRailOpen={railOpen} onToggleRail={() => setRailOpen((open) => !open)} onOpenPalette={openPalette} />
          {updated ? <UpdatedNotice /> : null}
          <div className="hd-body">
            <main id="hd-canvas" className="hd-canvas" tabIndex={-1}>
              <DockProvider element={dock}>
                <Outlet />
              </DockProvider>
            </main>
            <div ref={setDock} className="hd-dock" />
          </div>
          <ShellPalette isOpen={paletteOpen} onOpenChange={setPaletteOpen} />
        </OperationHost>
      </div>
      <ToastRegion />
    </div>
  )
}
