import { Button, Kbd } from '@headless/dashboard-sdk/components'
import { TimeZoneField, useFormat } from '@headless/dashboard-sdk/locale'
import { NavLink } from '@headless/dashboard-sdk/router'
import { useId, useState } from 'react'
import { moduleSidebarEntries, type BootstrapModule, type BootstrapSidebarEntry } from '../bootstrap.ts'
import { useShell } from '../shell-context.tsx'
import { attentionTotal, useNeedsYou } from '../shell-data.ts'
import { refusalText } from '../views/module-error.tsx'
import { ThemeControl } from './theme.tsx'

const operateReason = 'Needs operate permission'

function modulePath(module: BootstrapModule, entry: BootstrapSidebarEntry) {
  const rest = entry.route.replace(/^\/+|\/+$/g, '')

  return rest ? `/${module.id}/${rest}` : `/${module.id}`
}

function navClass({ isActive }: { isActive: boolean }) {
  return isActive ? 'hd-nav hd-nav-current' : 'hd-nav'
}

/** A sidebar entry the user may not open: focusable, so hover and focus show whom to ask. */
function LockedEntry({ label }: { label: string }) {
  const reasonId = useId()

  return (
    <span className="hd-nav hd-nav-locked" role="link" aria-disabled="true" tabIndex={0} aria-describedby={reasonId}>
      {label}
      <span id={reasonId} className="hd-why">
        {operateReason}
      </span>
    </span>
  )
}

function ModuleGroup({ module, onNavigate }: { module: BootstrapModule; onNavigate: () => void }) {
  const labelId = useId()
  const { state } = useShell()

  return (
    <div className="hd-rail-group" role="group" aria-labelledby={labelId}>
      <span id={labelId} className="hd-rail-label">
        {module.displayName}
      </span>
      {module.refusal ? (
        <p role="alert" className="hd-rail-error">
          {refusalText(module)}
        </p>
      ) : (
        moduleSidebarEntries(state, module.id).map((entry) =>
          entry.requires === 'operate' && !module.canOperate ? (
            <LockedEntry key={entry.id} label={entry.label} />
          ) : (
            <NavLink key={entry.id} to={modulePath(module, entry)} end className={navClass} onClick={onNavigate}>
              {entry.label}
            </NavLink>
          ),
        )
      )}
    </div>
  )
}

/**
 * The rail: Overview, Needs you with its count, one group per module in registration order, then the signed-in
 * operator, the theme, the display time zone, and Sign out. A failed sign-out shows beside Sign out, never over it, and the operator stays
 * signed in.
 */
export function Rail({ isOpen, onNavigate }: { isOpen: boolean; onNavigate: () => void }) {
  const { state, signOut } = useShell()
  const needsYou = useNeedsYou()
  const [signOutState, setSignOutState] = useState<'idle' | 'busy' | 'failed'>('idle')
  const total = attentionTotal(needsYou.data)
  const format = useFormat()
  const signsIn = state.authMode !== 'none'

  async function onSignOut() {
    setSignOutState('busy')

    try {
      await signOut()
    } catch {
      setSignOutState('failed')
    }
  }

  return (
    <nav id="hd-rail" className={`hd-rail${isOpen ? ' hd-rail-open' : ''}`} aria-label="Dashboard">
      <div className="hd-brand">
        <b>Headless</b>
        <span className="hd-hint">Operations dashboard</span>
      </div>
      <NavLink to="/" end className={navClass} onClick={onNavigate}>
        Overview
      </NavLink>
      <NavLink to="/needs-you" className={navClass} onClick={onNavigate}>
        Needs you
        {total.value > 0 ? (
          <span className="hd-nav-count" aria-label={`${format.count(total)} items`}>
            {format.count(total)}
          </span>
        ) : null}
      </NavLink>
      {state.modules.map((module) => (
        <ModuleGroup key={module.id} module={module} onNavigate={onNavigate} />
      ))}
      <div className="hd-rail-foot">
        {state.session.name ? <span className="hd-rail-who">{state.session.name}</span> : null}
        <ThemeControl />
        <TimeZoneField />
        {signsIn ? (
          <div className="hd-rail-signout">
            <Button variant="ghost" isPending={signOutState === 'busy'} onPress={() => void onSignOut()}>
              Sign out
            </Button>
            {signOutState === 'failed' ? (
              <span role="alert" className="hd-error">
                Sign-out failed. You are still signed in; try again.
              </span>
            ) : null}
          </div>
        ) : null}
        <span className="hd-hint">
          Press <Kbd>⌘K</Kbd> for commands
        </span>
      </div>
    </nav>
  )
}
