// The React Compiler's memo caches cost the shell more bytes than these rare renders save, and the shell's assets
// sit near their size budget.
'use no memo'

import { Button, useDelayed } from '@headless/dashboard-sdk/components'
import type { DashboardStateRole } from '@headless/dashboard-sdk'
import { useFormat } from '@headless/dashboard-sdk/locale'
import type { RefObject } from 'react'
import type { BootstrapProduct, BootstrapState } from '../bootstrap.ts'
import { useOpenObject } from '../shell-context.tsx'
import type { AttentionItem, ModuleSection } from '../shell-data.ts'
import { Flyout } from './flyout.tsx'

/** The worst severity among `items`, as a state role: fail with a critical item, proc with warnings only, else ok. */
function severityRole(items: readonly AttentionItem[]): DashboardStateRole {
  return items.some((item) => item.severity === 'critical') ? 'fail' : items.length > 0 ? 'proc' : 'ok'
}

/**
 * The sections' worst severity as a state role, but `off` instead of `ok` while there is no answer or a module did
 * not answer: only a complete, empty answer is all clear.
 */
export function attentionRole(sections: readonly ModuleSection<AttentionItem>[] | undefined): DashboardStateRole {
  const role = severityRole(sections?.flatMap((section) => section.items) ?? [])

  return role === 'ok' && (!sections || sections.some((section) => section.stale)) ? 'off' : role
}

/** A product tab's dot and the words behind it: `off` when one of its modules did not load or did not answer. */
export function productAttention(state: BootstrapState, product: BootstrapProduct, sections: readonly ModuleSection<AttentionItem>[] | undefined) {
  const failed = state.modules.some((module) => module.refusal && product.modules.includes(module.id))
  const own = sections?.filter((section) => product.modules.includes(section.id))
  const total = (own ?? []).reduce((sum, section) => section.items.reduce((count, item) => count + item.count, sum), 0)

  return failed
    ? { role: 'off' as const, text: `${product.label} did not load` }
    : {
        role: attentionRole(own),
        text: !own
          ? `${product.label}, attention not loaded`
          : total > 0
            ? `${product.label}, ${total} need attention`
            : own.some((section) => section.stale)
              ? `${product.label} did not answer`
              : `${product.label}, nothing needs attention`,
      }
}

function Item({ item, onOpen }: { item: AttentionItem; onOpen: () => void }) {
  const format = useFormat()
  const openObject = useOpenObject()
  const target = item.target

  return (
    <li className={`hd-attn-item hd-tone-${item.severity === 'critical' ? 'fail' : 'proc'}`}>
      <span className="hd-mark" />
      <span className="hd-visually-hidden">{item.severity === 'critical' ? 'Critical: ' : 'Warning: '}</span>
      <span className="hd-attn-text">
        {target ? (
          <a
            href="#"
            onClick={(event) => {
              event.preventDefault()
              onOpen()
              void openObject(target).catch(() => undefined)
            }}
          >
            {item.title}
          </a>
        ) : (
          item.title
        )}
        <span className="hd-hint">
          {`${format.count({ value: item.count, capped: item.capped })} ${item.count === 1 ? 'item' : 'items'}`}
          {item.since ? <span title={format.exactTime(item.since)}>{` · since ${format.relativeTime(item.since)}`}</span> : null}
        </span>
      </span>
    </li>
  )
}

interface AttentionProps {
  sections: ModuleSection<AttentionItem>[] | undefined
  isPending: boolean
  isError: boolean
  onRetry: () => void
  isOpen: boolean
  onClose: () => void
  trigger: RefObject<HTMLButtonElement | null>
}

/**
 * Everything that needs an operator, in every product: each item's severity, title, count, and since when, linking
 * into the module that holds it. A module that did not answer says so beside those that did, with Retry.
 */
export function AttentionPopover({ sections, isPending, isError, onRetry, isOpen, onClose, trigger }: AttentionProps) {
  const showLoading = useDelayed(isPending)
  const listed = (sections ?? []).filter((section) => section.stale || section.items.length > 0)

  return (
    <Flyout label="Attention" isOpen={isOpen} onClose={onClose} trigger={trigger} className="hd-attn">
      <h2 className="hd-title">Attention</h2>
      {isPending ? (
        showLoading ? <p className="hd-hint">Loading what needs you…</p> : null
      ) : isError ? (
        <p role="alert" className="hd-hint">
          Attention did not load. <Button onPress={onRetry}>Retry</Button>
        </p>
      ) : listed.length === 0 ? (
        <p className="hd-hint">Nothing needs you.</p>
      ) : (
        listed.map((section) => (
          <section key={section.id} aria-label={section.displayName}>
            <h3 className="hd-label">{section.displayName}</h3>
            {section.stale ? (
              <p className="hd-hint">
                {`${section.displayName} did not answer. `}
                <Button onPress={onRetry}>Retry</Button>
              </p>
            ) : (
              <ul className="hd-attn-list">
                {section.items.map((item) => (
                  <Item key={item.id} item={item} onOpen={onClose} />
                ))}
              </ul>
            )}
          </section>
        ))
      )}
    </Flyout>
  )
}
