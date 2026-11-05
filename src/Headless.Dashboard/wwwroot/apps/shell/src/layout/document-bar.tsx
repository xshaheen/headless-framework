import { Button, LiveIndicator } from '@headless/dashboard-sdk/components'
import { useLocation } from '@headless/dashboard-sdk/router'
import { routeHandleFor } from '../route-handles.ts'
import { useShell } from '../shell-context.tsx'
import { useCanOperate } from '../shell-data.ts'

export interface DocumentBarProps {
  isRailOpen: boolean
  onToggleRail: () => void
  onOpenPalette: () => void
}

/**
 * The document bar: the breadcrumb, the display time zone, the live indicator, and `View only` for a viewer of the
 * module on screen, or of every module on the shell's own views.
 */
export function DocumentBar({ isRailOpen, onToggleRail, onOpenPalette }: DocumentBarProps) {
  const { state } = useShell()
  const route = routeHandleFor(state, useLocation().pathname)
  const canOperate = useCanOperate(route.module)
  const crumb = route.crumb
  const title = crumb.at(-1) ?? ''

  return (
    <header className="hd-docbar">
      <Button
        variant="secondary"
        className="hd-menu-button"
        aria-expanded={isRailOpen}
        aria-controls="hd-rail"
        onPress={onToggleRail}
      >
        Menu
      </Button>
      <nav className="hd-crumb" aria-label="Breadcrumb">
        {crumb.slice(0, -1).map((part) => (
          <span key={part} className="hd-crumb-part">
            {part}
            <span className="hd-crumb-sep" aria-hidden="true">
              ›
            </span>
          </span>
        ))}
        <h1 className="hd-page-title">{title}</h1>
      </nav>
      <span className="hd-docbar-spacer" />
      <span className="hd-chip" title="Display time zone">
        UTC
      </span>
      <LiveIndicator />
      {canOperate ? null : <span className="hd-view-only">View only</span>}
      <Button variant="ghost" className="hd-palette-button" onPress={onOpenPalette} aria-keyshortcuts="Meta+K Control+K">
        Search
      </Button>
    </header>
  )
}
