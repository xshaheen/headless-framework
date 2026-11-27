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

/**
 * The sidebar's stroke icons by the icon key a module declares, each one path on the 16-unit grid the entry's box
 * draws at 15 px; an unknown key draws none.
 */
const icons: Record<string, string> = {
  dashboard: 'M2.5 2.5h4.5v4.5h-4.5zM9 2.5h4.5v4.5H9zM2.5 9h4.5v4.5h-4.5zM9 9h4.5v4.5H9z',
  runs: 'M3 4h10M3 8h10M3 12h6',
  recurring: 'M8 2.5a5.5 5.5 0 1 1 0 11a5.5 5.5 0 0 1 0-11zM8 5v3l2 1.5',
  function:
    'M5.5 3.5C4 3.5 4 5 4 6s-1 2-1.5 2C3 8 4 9 4 10s0 2.5 1.5 2.5M10.5 3.5C12 3.5 12 5 12 6s1 2 1.5 2c-.5 0-1.5 1-1.5 2s0 2.5-1.5 2.5',
  server: 'M2.5 2.5h11v4.5h-11zM2.5 9h11v4.5h-11zM5 4.75h.01M5 11.25h.01',
  lock: 'M4.5 7h7a1.5 1.5 0 0 1 1.5 1.5v4a1.5 1.5 0 0 1-1.5 1.5h-7A1.5 1.5 0 0 1 3 12.5v-4A1.5 1.5 0 0 1 4.5 7zM5.5 7V5a2.5 2.5 0 0 1 5 0v2',
  gauge: 'M2.5 11a5.5 5.5 0 0 1 11 0M8 11l2.5-3.5',
  repeat: 'M3 8a5 5 0 0 1 8.5-3.5L13 6M13 2.5V6H9.5M13 8a5 5 0 0 1-8.5 3.5L3 10M3 13.5V10h3.5',
  hash: 'M6.5 2.5L5 13.5M11.5 2.5L10 13.5M3 6h10.5M2.5 10h10.5',
  mail: 'M4.5 3.5h7a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-7a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2zM2.5 5.5L8 9l5.5-3.5',
  message: 'M2.5 3.5h11v7h-6l-3 2.5v-2.5h-2z',
  bell: 'M4 11.5V7a4 4 0 0 1 8 0v4.5l1.2 1.2H2.8zM6.5 14a1.6 1.6 0 0 0 3 0',
  activity: 'M2.5 8h2.5l1.5-4 3 8 1.5-4h2.5',
  'heart-pulse':
    'M8 13.5C5 11.5 2.5 9.3 2.5 6.5a3 3 0 0 1 5.5-1.7a3 3 0 0 1 5.5 1.7c0 2.8-2.5 5-5.5 7zM4 8.5h2l1-1.5 1.5 3 1-1.5h2.5',
  nodes:
    'M9.5 4a1.5 1.5 0 1 1-3 0a1.5 1.5 0 1 1 3 0zM5.5 12a1.5 1.5 0 1 1-3 0a1.5 1.5 0 1 1 3 0zM13.5 12a1.5 1.5 0 1 1-3 0a1.5 1.5 0 1 1 3 0zM7.33 5.34L4.67 10.66M8.67 5.34l2.66 5.32M5.5 12h5',
}

function Lead({ entry }: { entry: BootstrapSidebarEntry }) {
  const path = entry.icon ? icons[entry.icon] : undefined

  return entry.state ? (
    // A dot sits in the 15 px box an icon takes, so labels align whichever an entry leads with.
    <span className={`hd-mark mx-[calc((var(--icon)-var(--sp-3))/2)] hd-tone-${entry.state}`} aria-hidden="true" />
  ) : (
    <svg viewBox="0 0 16 16" aria-hidden="true">
      {path ? <path d={path} /> : null}
    </svg>
  )
}

const sideItem = 'flex h-(--field-height) items-center gap-2.5 rounded-md px-2.5 type-nav no-underline hover:bg-accent'
// Weight and an inline-start accent bar mark the current entry, so the fill is never the only cue.
const sideLink = `${sideItem} text-muted-foreground hover:text-foreground current:bg-muted current:font-medium current:text-foreground current:accent-bar-start`

function mostSpecificEntry<T extends { to: string }>(entries: readonly T[], search: URLSearchParams, pathname: string) {
  let best: T | undefined
  let score = -1

  for (const entry of entries) {
    const [path = '', query] = entry.to.split('?')
    const params = [...new URLSearchParams(query)]
    const next =
      path === pathname && params.every(([name, value]) => search.get(name) === value)
        ? 1e4 + params.length
        : !query && pathname.startsWith(`${path}/`)
          ? path.length
          : -1

    if (next > score) {
      best = entry
      score = next
    }
  }

  return best
}

/** A product's sidebar entries, with same-label sections merged in registration order. */
function productSections(product: BootstrapProduct) {
  const merged: {
    key: string
    label?: string | null
    entries: (BootstrapSidebarEntry & { module: string; to: string })[]
  }[] = []
  const byLabel = new Map<string, (typeof merged)[number]>()

  for (const [index, section] of product.sections.entries()) {
    const entries = section.entries.map((entry) => ({
      ...entry,
      module: section.module,
      to: modulePath(section.module, entry.route),
    }))

    if (section.label) {
      const existing = byLabel.get(section.label)

      if (existing) {
        existing.entries.push(...entries)
        continue
      }

      const group = { key: `label:${section.label}`, label: section.label, entries }
      byLabel.set(section.label, group)
      merged.push(group)
    } else {
      merged.push({ key: `${section.module}:${index}`, label: section.label, entries })
    }
  }

  return merged
}

/** The sidebar entry the location is on, such as `Runs` on a run's own page; the shell titles the page with it. */
export function currentEntry(product: BootstrapProduct, location: { pathname: string; search: string }) {
  return mostSpecificEntry(
    productSections(product).flatMap((section) => section.entries),
    new URLSearchParams(location.search),
    location.pathname,
  )
}

/**
 * The active product's sidebar: its modules' sections in order, each entry with an icon or a state dot and, when it
 * declares a count key, its count from one request. A count reads an en dash while loading or failed, never 0, and
 * `N+` when some of its sources did not answer, which its accessible name lists.
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
  const sections = productSections(product)
  const current = currentEntry(product, location)

  return (
    // Below 768 px the sidebar fills the shell's sheet, which draws the edge.
    <nav
      id="hd-sidebar"
      className="flex min-h-0 flex-1 flex-col gap-0.5 overflow-y-auto border-e bg-deep px-2 py-2.5 line-icons max-md:border-e-0"
      aria-label={product.label}
    >
      {sections.map((section) => (
        <div key={section.key} className="flex flex-col gap-0.5">
          {section.label ? <h2 className="px-2.5 pt-4 pb-1.5 type-label text-faint">{section.label}</h2> : null}
          {section.entries.map((entry) => {
            const count = entry.countKey ? counts.data?.counts[entry.countKey] : undefined
            const body = (
              <>
                <Lead entry={entry} />
                <span className="min-w-0 flex-1 truncate" title={entry.label}>
                  {entry.label}
                </span>
                {entry.countKey ? (
                  // A partial count's accessible name lists what it leaves out, in one text node that keeps its spaces.
                  count?.unavailable ? (
                    <span className="type-count">
                      <span aria-hidden="true">{format.count(count)}</span>
                      <span className="sr-only">{`${format.count(count)} · ${count.unavailable.join(', ')} unavailable`}</span>
                    </span>
                  ) : (
                    <span className="type-count">{count ? format.count(count) : '–'}</span>
                  )
                ) : null}
              </>
            )
            const locked =
              entry.requires === 'operate' && !state.modules.find((module) => module.id === entry.module)?.canOperate

            return locked ? (
              <span
                key={entry.id}
                className={`${sideItem} cursor-not-allowed text-faint`}
                role="link"
                aria-disabled="true"
                tabIndex={0}
                aria-describedby={reasonId}
              >
                {body}
              </span>
            ) : (
              <Link
                key={entry.id}
                to={entry.to}
                className={sideLink}
                aria-current={entry.to === current?.to ? 'page' : undefined}
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
