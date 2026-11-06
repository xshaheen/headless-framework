// The React Compiler's memo caches cost the shell more bytes than these rare renders save, and the shell's assets
// sit near their size budget.
'use no memo'

import { Announcer } from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { Link, useLocation } from '@headless/dashboard-sdk/router'
import { useId } from 'react'
import type { BootstrapProduct, BootstrapSidebarEntry } from '../bootstrap.ts'
import { modulePath } from '../route-handles.ts'
import { useShell } from '../shell-context.tsx'
import { useProductCounts } from '../shell-data.ts'

/** The sidebar's 15 px stroke icons by the icon key a module declares; an unknown key draws none. */
const icons: Record<string, string> = {
  dashboard: 'M2.5 2.5h4.5v4.5h-4.5zM9 2.5h4.5v4.5H9zM2.5 9h4.5v4.5h-4.5zM9 9h4.5v4.5H9z',
  runs: 'M3 4h10M3 8h10M3 12h6',
  recurring: 'M8 2.5a5.5 5.5 0 1 1 0 11a5.5 5.5 0 0 1 0-11zM8 5v3l2 1.5',
  function: 'M5.5 3.5C4 3.5 4 5 4 6s-1 2-1.5 2C3 8 4 9 4 10s0 2.5 1.5 2.5M10.5 3.5C12 3.5 12 5 12 6s1 2 1.5 2c-.5 0-1.5 1-1.5 2s0 2.5-1.5 2.5',
  server: 'M2.5 2.5h11v4.5h-11zM2.5 9h11v4.5h-11zM5 4.75h.01M5 11.25h.01',
}

function Lead({ entry }: { entry: BootstrapSidebarEntry }) {
  const path = entry.icon ? icons[entry.icon] : undefined

  return entry.state ? (
    <span className={`hd-mark hd-tone-${entry.state}`} aria-hidden="true" />
  ) : (
    <svg viewBox="0 0 16 16" aria-hidden="true">
      {path ? <path d={path} /> : null}
    </svg>
  )
}

/** The entry the location sits on: an exact path and query first, else the longest list whose page this is. */
function currentEntry(entries: readonly { to: string }[], location: string, pathname: string) {
  return (
    entries.find((entry) => entry.to === location) ??
    entries
      .filter((entry) => !entry.to.includes('?') && pathname.startsWith(`${entry.to}/`))
      .sort((left, right) => right.to.length - left.to.length)[0]
  )
}

/**
 * The active product's sidebar: its modules' sections in order, each entry with an icon or a state dot and, when it
 * declares a count key, its count from one request. A count reads an en dash while loading or failed, never 0.
 */
export function ProductSidebar({ product, onNavigate }: { product: BootstrapProduct; onNavigate?: () => void }) {
  const { state } = useShell()
  const format = useFormat()
  const location = useLocation()
  const reasonId = useId()
  const topics = state.modules
    .filter((module) => product.modules.includes(module.id))
    .flatMap((module) => Object.keys(module.objectKinds ?? {}).map((kind) => `${module.id}.${kind}`))
  const counted = product.sections.some((section) => section.entries.some((entry) => entry.countKey))
  const counts = useProductCounts(counted ? product.id : undefined, topics)
  const sections = product.sections.map((section) => ({
    ...section,
    entries: section.entries.map((entry) => ({ ...entry, module: section.module, to: modulePath(section.module, entry.route) })),
  }))
  const current = currentEntry(sections.flatMap((section) => section.entries), `${location.pathname}${location.search}`, location.pathname)

  return (
    <nav className="hd-sidebar" aria-label={product.label}>
      {sections.map((section, index) => (
        <div key={`${section.module}:${index}`} className="hd-side-section">
          {section.label ? <h2 className="hd-side-head">{section.label}</h2> : null}
          {section.entries.map((entry) => {
              const count = entry.countKey ? counts.data?.counts[entry.countKey] : undefined
              const body = (
                <>
                  <Lead entry={entry} />
                  <span className="hd-side-label" title={entry.label}>
                    {entry.label}
                  </span>
                  {entry.countKey ? <span className="hd-side-count">{count ? format.count(count) : '–'}</span> : null}
                </>
              )
              const locked = entry.requires === 'operate' && !state.modules.find((module) => module.id === entry.module)?.canOperate

              return locked ? (
                <span key={entry.id} className="hd-side-item" role="link" aria-disabled="true" tabIndex={0} aria-describedby={reasonId}>
                  {body}
                </span>
              ) : (
                <Link
                  key={entry.id}
                  to={entry.to}
                  className="hd-side-item"
                  aria-current={entry === current ? 'page' : undefined}
                  onClick={onNavigate}
                >
                  {body}
                </Link>
              )
            })}
        </div>
      ))}
      <span id={reasonId} hidden>
        Needs operate permission
      </span>
      {/* A module that did not answer leaves its counts out of a successful answer, so they read as en dashes too. */}
      <Announcer message={counts.isError || counts.data?.stale.length ? `${product.label} counts did not load.` : ''} />
    </nav>
  )
}
