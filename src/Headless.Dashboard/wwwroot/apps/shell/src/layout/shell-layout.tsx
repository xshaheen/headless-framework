// The React Compiler's memo caches cost the shell more bytes than these rare renders save, and the shell's assets
// sit near their size budget.
'use no memo'

import { OperationHost } from '@headless/dashboard-sdk/commands'
import { Announcer, Button, Sheet, ToastRegion, useShellKeys, useViewTitle } from '@headless/dashboard-sdk/components'
import { Outlet, useLocation, useNavigate } from '@headless/dashboard-sdk/router'
import { useEffect, useRef, useState } from 'react'
import { ShellPalette } from '../palette/shell-palette.tsx'
import { productHome, productOf } from '../route-handles.ts'
import { useShell } from '../shell-context.tsx'
import { UpdatedNotice } from '../views/module-error.tsx'
import { useBelow, useFlyout } from './flyout.tsx'
import { currentEntry, ProductSidebar } from './product-sidebar.tsx'
import { ShortcutList } from './shortcut-list.tsx'
import { TopBar } from './top-bar.tsx'

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
  const location = useLocation()
  const { pathname } = location
  const phone = useBelow('phone')
  const goTo = useRef<HTMLInputElement>(null)
  const canvas = useRef<HTMLElement>(null)
  const menuButton = useRef<HTMLButtonElement>(null)
  const attention = useFlyout()
  const product = productOf(state, pathname)
  const entry = product ? currentEntry(product, location) : undefined
  // The served title, such as `Dashboard`, ends every view's: `Runs · Jobs · Dashboard`, with no word said twice. A
  // view that names its own page, such as a run, takes the sidebar entry's place: `billing.charge run · Jobs · …`.
  const [served] = useState(() => document.title)
  const view = useViewTitle()
  const titleOf = (label: string | undefined) =>
    [...new Set([label, product?.label, served].filter(Boolean))].join(' · ')
  const title = titleOf(view.title ?? entry?.label)
  const [arrived, setArrived] = useState({ message: '', count: 0 })
  const shownPath = useRef(pathname)
  // A new view whose name is still loading has not been said yet.
  const unsaid = useRef(false)
  // The location the canvas last saw; a page load leaves focus where the browser puts it.
  const seen = useRef(`${pathname}${location.search}`)
  // A fresh key per opening, so the palette starts from the search it was opened with.
  const openPalette = (query?: string) => setPalette((current) => ({ open: true, query, key: current.key + 1 }))

  // `G R` opens the Jobs runs list. It does not check that the module is registered: without Jobs the route answers
  // with the shell's Not found view.
  useShellKeys({
    palette: () => openPalette(),
    goTo: () => goTo.current?.focus(),
    shortcuts: () => setShortcutsOpen(true),
    go: {
      o: () => void navigate(product ? productHome(product) : '/'),
      n: () => attention.setOpen(true),
      r: () => void navigate('/jobs/runs'),
    },
  })
  useEffect(() => loader.onError(() => setUpdated(true)), [loader])

  useEffect(() => {
    document.title = title
  }, [title])

  // A view the router swaps in is a new page to the operator, but nothing tells a screen reader so: the live region
  // says the new title once. A filter or an open row changes the search alone and is not a new page. A view that names
  // its page is said once its name arrives, read from the store because the view named it after this render.
  useEffect(() => {
    if (pathname !== shownPath.current) {
      shownPath.current = pathname
      unsaid.current = true
    }

    const named = useViewTitle.getState()

    if (unsaid.current && !(named.isNamed && named.title === undefined)) {
      unsaid.current = false
      setArrived((current) => ({ message: titleOf(named.title ?? entry?.label), count: current.count + 1 }))
    }
  }, [pathname, view.isNamed, view.title])

  // A control that took the operator elsewhere, such as a sidebar link in the phone's menu or a panel's Open in its own
  // page, may leave with the old view; focus it dropped to the body lands on the canvas instead. Focus still on a
  // control, such as a filter that changed the search, stays where it is.
  useEffect(() => {
    const at = `${pathname}${location.search}`

    if (at !== seen.current && (!document.activeElement || document.activeElement === document.body)) {
      canvas.current?.focus({ preventScroll: true })
    }

    seen.current = at
  }, [pathname, location.search])

  // A menu left open on a phone stays closed when the window widens past it, where the sidebar is a column again.
  if (menuOpen && !phone) {
    setMenuOpen(false)
  }

  // The sheet traps focus and hands it back to Menu on close; focus starts on the first entry, not on the sheet itself.
  useEffect(() => {
    if (menuOpen) {
      document.getElementById('hd-sidebar')?.querySelector<HTMLElement>('a[href], [tabindex="0"]')?.focus()
    }
  }, [menuOpen])

  return (
    // The bar spans the top row, and the sidebar and the view fill the second in order. Off every product (Not found,
    // no module registered), and below 768 px where the sidebar opens over the view, there is no sidebar column.
    <div
      className={`grid h-dvh grid-cols-1 grid-rows-[var(--topbar-height)_minmax(0,1fr)] ${product ? 'md:grid-cols-[var(--sidebar-width)_minmax(0,1fr)]' : ''}`}
    >
      <a
        className="absolute start-2 top-[-100vh] z-(--z-palette) rounded-md bg-card px-3 py-2 focus:top-2"
        href="#hd-canvas"
      >
        Skip to content
      </a>
      <TopBar
        product={product}
        goTo={goTo}
        attention={attention}
        onMany={openPalette}
        menu={
          phone && product ? (
            <Button
              ref={menuButton}
              aria-expanded={menuOpen}
              aria-controls={menuOpen ? 'hd-sidebar' : undefined}
              onPress={() => setMenuOpen((open) => !open)}
            >
              Menu
            </Button>
          ) : undefined
        }
      />
      {product && !phone ? <ProductSidebar product={product} /> : null}
      {/* On a phone the sidebar is a modal sheet: a scrim over the view, Esc and the scrim close it. */}
      {product && phone ? (
        <Sheet isOpen={menuOpen} onOpenChange={setMenuOpen} label={`${product.label} menu`}>
          <ProductSidebar product={product} onNavigate={() => setMenuOpen(false)} />
        </Sheet>
      ) : null}
      <div className="flex min-h-0 min-w-0 flex-col">
        <OperationHost>
          {updated ? <UpdatedNotice className="mx-5 mt-2" /> : null}
          {/* A view that draws its own frame or panel runs edge to edge. */}
          <main
            ref={canvas}
            id="hd-canvas"
            className="flex min-h-0 flex-1 flex-col gap-5 overflow-auto bg-card p-5 pb-7 focus:outline-none max-md:p-4 max-md:pb-5 has-frame:gap-0 has-frame:p-0"
            tabIndex={-1}
          >
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
      {/* Keyed by arrival, so two pages with one title are each said. */}
      <Announcer message={arrived.message} id={arrived.count} />
    </div>
  )
}
