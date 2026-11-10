// The React Compiler's memo caches cost the shell more bytes than these rare renders save, and the shell's assets
// sit near their size budget.
'use no memo'

import { Button, Kbd, LiveIndicator } from '@headless/dashboard-sdk/components'
import { TimeZoneField, useFormat, useLocale } from '@headless/dashboard-sdk/locale'
import { useEffect, useState, useSyncExternalStore, type ReactNode, type RefObject } from 'react'
import type { BootstrapProduct } from '../bootstrap.ts'
import { useOpenObject, useShell } from '../shell-context.tsx'
import { attentionTotal, useAttention, useCanOperate, useResolve, type ResolvedObject } from '../shell-data.ts'
import { AttentionPopover, attentionRole } from './attention-popover.tsx'
import { Flyout, useBelow, useFlyout } from './flyout.tsx'
import { ProductTabs } from './product-tabs.tsx'
import { ThemeControl } from './theme.tsx'

function Glyph({ d, fill }: { d: string; fill?: boolean }) {
  return (
    <svg viewBox="0 0 16 16" aria-hidden="true" className={fill ? 'hd-glyph-fill' : undefined}>
      <path d={d} />
    </svg>
  )
}

const searchGlyph = 'M7 2.8a4.2 4.2 0 1 1 0 8.4a4.2 4.2 0 0 1 0-8.4zM10.2 10.2l3.3 3.3'

/** The display time zone's wall clock, `14:20:31`; not a live region, so its ticking is never announced. */
function Clock() {
  const { timeZone } = useLocale()
  const [now, setNow] = useState(() => new Date())

  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 1_000)

    return () => clearInterval(timer)
  }, [])

  return (
    <time className="hd-clock" dateTime={now.toISOString()}>
      {now.toLocaleTimeString('en-GB', { timeZone, hourCycle: 'h23' })}
    </time>
  )
}

/**
 * Go to id: a pasted id is looked up in every module once typing settles, and the matches list below the field. Enter
 * opens the one match; with several it opens the palette on the same search.
 */
function GoTo({ input, onMany }: { input: RefObject<HTMLInputElement | null>; onMany: (query: string) => void }) {
  const [query, setQuery] = useState('')
  const resolved = useResolve(query, true)
  const objects = resolved && !resolved.failed ? resolved.objects : undefined
  const openObject = useOpenObject()

  function open(object: ResolvedObject) {
    setQuery('')
    void openObject(object.target).catch(() => undefined)
  }

  return (
    <div className="hd-goto">
      <label className="hd-goto-field">
        <Glyph d={searchGlyph} />
        <span className="sr-only">Go to id</span>
        <input
          ref={input}
          value={query}
          placeholder="Go to id"
          spellCheck={false}
          autoComplete="off"
          onChange={(event) => setQuery(event.currentTarget.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && objects?.length === 1) {
              open(objects[0]!)
            } else if (event.key === 'Enter' && objects && objects.length > 1) {
              onMany(query.trim())
              setQuery('')
            } else if (event.key === 'Escape') {
              setQuery('')
              event.currentTarget.blur()
            }
          }}
        />
        <span aria-hidden="true">
          <Kbd>/</Kbd>
        </span>
      </label>
      {resolved ? (
        <div className="hd-goto-list" role="region" aria-label="Matches">
          {resolved.failed ? (
            <p className="hd-hint">Lookup failed. Try again.</p>
          ) : resolved.objects.length === 0 ? (
            <p className="hd-hint">
              {resolved.stale.length > 0
                ? `No match in the modules that answered (${resolved.stale.join(', ')} did not answer)`
                : `No object with id ${query.trim()}`}
            </p>
          ) : (
            resolved.objects.map((object) => (
              <Button key={`${object.target.module}:${object.target.id}`} variant="ghost" onPress={() => open(object)}>
                {object.title}
              </Button>
            ))
          )}
        </div>
      ) : null}
    </div>
  )
}

function initials(name: string | null | undefined) {
  const parts = (name ?? '').split(/[\s._@-]+/).filter(Boolean)

  return parts.length === 0 ? '?' : parts.slice(0, 2).map((part) => part[0]!.toUpperCase()).join('')
}

/** Pause or Resume: an icon in the bar, a labelled row in the account menu. */
function PauseButton({ paused, onToggle, labelled }: { paused: boolean; onToggle: () => void; labelled?: boolean }) {
  const label = paused ? 'Resume live updates' : 'Pause live updates'

  return (
    <Button variant="ghost" className={labelled ? undefined : 'hd-icon'} aria-label={labelled ? undefined : label} onPress={onToggle}>
      <Glyph d={paused ? 'M5 3.5v9l7-4.5z' : 'M4.5 3h2.5v10H4.5zM9 3h2.5v10H9z'} fill />
      {labelled ? label : null}
    </Button>
  )
}

interface TopBarProps {
  product: BootstrapProduct | undefined
  goTo: RefObject<HTMLInputElement | null>
  attention: ReturnType<typeof useFlyout>
  onMany: (query: string) => void
  /** The phone's Menu button, which opens the sidebar; absent above phone width. */
  menu?: ReactNode
}

/**
 * The top bar: the mark, the product tabs, go to id, the Attention pill, the live state and clock, the time zone,
 * pause, and the account menu. As the window narrows, the zone and clock, then pause, fold into the account menu.
 */
export function TopBar({ product, goTo, attention, onMany, menu }: TopBarProps) {
  const { state, signOut, live } = useShell()
  const { timeZone } = useLocale()
  const format = useFormat()
  const narrow = useBelow('wide')
  const medium = useBelow('medium')
  const account = useFlyout()
  const zone = useFlyout()
  const items = useAttention()
  const canOperate = useCanOperate(product?.modules[0])
  const paused = useSyncExternalStore(live.onPauseChange, live.getPending)
  const [signOutState, setSignOutState] = useState<'idle' | 'busy' | 'failed'>('idle')
  // A failed request is no answer, so the pill and the tabs read unknown rather than all clear.
  const sections = items.isError ? undefined : items.data
  const total = attentionTotal(sections)
  const role = attentionRole(sections)
  const togglePause = () => live.setPaused(paused === null)
  const name = state.session.name

  async function onSignOut() {
    setSignOutState('busy')

    try {
      await signOut()
    } catch {
      setSignOutState('failed')
    }
  }

  return (
    <header className="hd-topbar">
      {menu}
      <span className="hd-logo">
        <span className="hd-logo-mark" aria-hidden="true">
          <Glyph d="M4.5 3v10M11.5 3v10M4.5 8h7" />
        </span>
        headless
      </span>
      <ProductTabs current={product} attention={sections} />
      <span className="hd-spacer" />
      <GoTo input={goTo} onMany={onMany} />
      <Button ref={attention.trigger} variant="ghost" className={`hd-quiet hd-tone-${role}`} aria-expanded={attention.isOpen} onPress={attention.toggle}>
        <span className="hd-mark" aria-hidden="true" />
        {total.value > 0 ? `Attention ${format.count(total)}` : 'Attention'}
      </Button>
      <AttentionPopover
        sections={items.data}
        isPending={items.isPending}
        isError={items.isError}
        onRetry={() => void items.refetch()}
        isOpen={attention.isOpen}
        onClose={() => attention.setOpen(false)}
        trigger={attention.trigger}
      />
      <LiveIndicator paused={paused} />
      {narrow ? null : (
        <>
          <Clock />
          <Button ref={zone.trigger} variant="ghost" className="hd-quiet" aria-label={`Display time zone: ${timeZone}`} onPress={zone.toggle}>
            {timeZone}
          </Button>
          <Flyout label="Display time zone" isOpen={zone.isOpen} onClose={() => zone.setOpen(false)} trigger={zone.trigger}>
            <TimeZoneField />
          </Flyout>
        </>
      )}
      {medium ? null : <PauseButton paused={paused !== null} onToggle={togglePause} />}
      {canOperate ? null : <span className="hd-view-only">View only</span>}
      <Button ref={account.trigger} className="hd-avatar" aria-label={`Account: ${name ?? 'signed in'}`} aria-expanded={account.isOpen} onPress={account.toggle}>
        {initials(name)}
      </Button>
      <Flyout label="Account" isOpen={account.isOpen} onClose={() => account.setOpen(false)} trigger={account.trigger}>
        {name ? <b>{name}</b> : null}
        <ThemeControl />
        {narrow ? (
          <>
            <TimeZoneField />
            <span className="hd-hint">
              {'Now '}
              <Clock />
            </span>
          </>
        ) : null}
        {medium ? <PauseButton paused={paused !== null} onToggle={togglePause} labelled /> : null}
        {state.authMode === 'none' ? null : (
          <>
            <Button variant="ghost" isPending={signOutState === 'busy'} onPress={() => void onSignOut()}>
              Sign out
            </Button>
            {signOutState === 'failed' ? (
              <span role="alert" className="hd-error">
                Sign-out failed. You are still signed in; try again.
              </span>
            ) : null}
          </>
        )}
      </Flyout>
    </header>
  )
}
