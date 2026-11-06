// The React Compiler's memo caches cost the shell more bytes than these rare renders save, and the shell's assets
// sit near their size budget.
'use no memo'

import { OperationHost } from '@headless/dashboard-sdk/commands'
import { Button, ToastRegion, useShellKeys } from '@headless/dashboard-sdk/components'
import { Outlet, useLocation, useNavigate } from '@headless/dashboard-sdk/router'
import { useEffect, useRef, useState } from 'react'
import { ShellPalette } from '../palette/shell-palette.tsx'
import { productHome, productOf } from '../route-handles.ts'
import { useShell } from '../shell-context.tsx'
import { UpdatedNotice } from '../views/module-error.tsx'
import { useBelow, useFlyout } from './flyout.tsx'
import { ProductSidebar } from './product-sidebar.tsx'
import { ShortcutList } from './shortcut-list.tsx'
import { TopBar } from './top-bar.tsx'
import './shell.css'

/**
 * The frame around every signed-in view: the top bar, the active product's sidebar, Operations docked above the
 * view, and the view. Operations live here, outside any route, so a running one keeps its chunks going and its Stop
 * in view on every view.
 */
export function ShellLayout() {
  const { state, loader } = useShell()
  const [palette, setPalette] = useState<{ open: boolean; query?: string; key: number }>({ open: false, key: 0 })
  const [shortcutsOpen, setShortcutsOpen] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
  const [updated, setUpdated] = useState(false)
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const phone = useBelow('phone')
  const goTo = useRef<HTMLInputElement>(null)
  const attention = useFlyout()
  const product = productOf(state, pathname)
  // A fresh key per opening, so the palette starts from the search it was opened with.
  const openPalette = (query?: string) => setPalette((current) => ({ open: true, query, key: current.key + 1 }))

  // `G R` opens the Jobs runs list, the Jobs module's home route. It does not check that the module is registered:
  // without Jobs the route answers with the shell's Not found view.
  useShellKeys({
    palette: () => openPalette(),
    goTo: () => goTo.current?.focus(),
    shortcuts: () => setShortcutsOpen(true),
    go: {
      o: () => void navigate(product ? productHome(product) : '/'),
      n: () => attention.setOpen(true),
      r: () => void navigate('/jobs'),
    },
  })
  useEffect(() => loader.onError(() => setUpdated(true)), [loader])

  return (
    <div className={product ? 'hd-shell' : 'hd-shell hd-shell-bare'}>
      <a className="hd-skip" href="#hd-canvas">
        Skip to content
      </a>
      <TopBar
        product={product}
        goTo={goTo}
        attention={attention}
        onMany={openPalette}
        menu={
          phone && product ? (
            <Button aria-expanded={menuOpen} onPress={() => setMenuOpen((open) => !open)}>
              Menu
            </Button>
          ) : undefined
        }
      />
      {product && (!phone || menuOpen) ? <ProductSidebar product={product} onNavigate={() => setMenuOpen(false)} /> : null}
      <div className="hd-main">
        <OperationHost>
          {updated ? <UpdatedNotice /> : null}
          <main id="hd-canvas" className="hd-canvas" tabIndex={-1}>
            <Outlet />
          </main>
          <ShellPalette
            key={palette.key}
            isOpen={palette.open}
            initialQuery={palette.query}
            onOpenChange={(open) => setPalette((current) => ({ ...current, open }))}
            onOpenAttention={() => attention.setOpen(true)}
          />
          <ShortcutList isOpen={shortcutsOpen} onOpenChange={setShortcutsOpen} />
        </OperationHost>
      </div>
      <ToastRegion />
    </div>
  )
}
