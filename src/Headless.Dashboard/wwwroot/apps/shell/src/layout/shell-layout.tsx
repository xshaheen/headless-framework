import { OperationHost } from '@headless/dashboard-sdk/commands'
import { PrimaryActionSlot, ToastRegion, useShellKeys } from '@headless/dashboard-sdk/components'
import { Outlet, useNavigate } from '@headless/dashboard-sdk/router'
import { useCallback, useEffect, useState } from 'react'
import { ShellPalette } from '../palette/shell-palette.tsx'
import { useShell } from '../shell-context.tsx'
import { UpdatedNotice } from '../views/module-error.tsx'
import { DocumentBar } from './document-bar.tsx'
import { DockProvider } from './inspector-dock.tsx'
import { Rail } from './rail.tsx'
import { ShortcutList } from './shortcut-list.tsx'
import './shell.css'

/**
 * The shell's layout around every signed-in view: the rail, the document bar, Operations docked below it, the
 * canvas, and the inspector dock. Operations live here, outside any route, so a running one keeps its chunks going
 * and its Stop in view on every view.
 */
export function ShellLayout() {
  const { loader } = useShell()
  const [railOpen, setRailOpen] = useState(false)
  const [paletteOpen, setPaletteOpen] = useState(false)
  const [shortcutsOpen, setShortcutsOpen] = useState(false)
  const navigate = useNavigate()
  const [dock, setDock] = useState<HTMLElement | null>(null)
  const [primary, setPrimary] = useState<HTMLElement | null>(null)
  const [updated, setUpdated] = useState(false)

  const openPalette = useCallback(() => setPaletteOpen(true), [])

  // `G` then a letter goes to a view: `G R` opens the Jobs runs list, the Jobs module's home route. It does not check
  // that the module is registered: the check costs more than the shell's bundle budget leaves, and without Jobs the
  // route answers with the shell's Not found view.
  useShellKeys({
    palette: openPalette,
    shortcuts: () => setShortcutsOpen(true),
    go: { o: () => void navigate('/'), n: () => void navigate('/needs-you'), r: () => void navigate('/jobs') },
  })
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
          <DocumentBar isRailOpen={railOpen} onToggleRail={() => setRailOpen((open) => !open)} onOpenPalette={openPalette} primarySlot={setPrimary} />
          {updated ? <UpdatedNotice /> : null}
          <div className="hd-body">
            <main id="hd-canvas" className="hd-canvas" tabIndex={-1}>
              <DockProvider element={dock}>
                <PrimaryActionSlot element={primary}>
                  <Outlet />
                </PrimaryActionSlot>
              </DockProvider>
            </main>
            <div ref={setDock} className="hd-dock" />
          </div>
          <ShellPalette isOpen={paletteOpen} onOpenChange={setPaletteOpen} />
          <ShortcutList isOpen={shortcutsOpen} onOpenChange={setShortcutsOpen} />
        </OperationHost>
      </div>
      <ToastRegion />
    </div>
  )
}
