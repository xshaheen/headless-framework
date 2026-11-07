import { useOperations } from '@headless/dashboard-sdk/commands'
import { Button, InspectorField, InspectorFields, Mono, StatusMark, useDelayed } from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useSearchParams } from '@headless/dashboard-sdk/router'
import { useEffect, useState } from 'react'
import { DockedInspector } from '../layout/inspector-dock.tsx'
import { useOpenObject } from '../shell-context.tsx'
import { attentionTotal, useNeedsYou, useRelated, type AttentionItem, type ModuleSection } from '../shell-data.ts'

interface Cause {
  key: string
  items: AttentionItem[]
}

/** A module's items grouped by cause, in the order the module listed them. */
function groupByCause(items: readonly AttentionItem[]): Cause[] {
  const causes = new Map<string, Cause>()

  for (const item of items) {
    let cause = causes.get(item.groupKey)

    if (!cause) {
      cause = { key: item.groupKey, items: [] }
      causes.set(item.groupKey, cause)
    }

    cause.items.push(item)
  }

  return [...causes.values()]
}

function selectionKey(module: string, item: string) {
  return `${module}:${item}`
}

function InvestigateButton({ item, variant }: { item: AttentionItem; variant?: 'primary' | 'secondary' }) {
  const openObject = useOpenObject()
  const [error, setError] = useState<string | undefined>()

  if (!item.target) {
    return null
  }

  const target = item.target

  return (
    <>
      <Button
        variant={variant}
        onPress={() => {
          setError(undefined)
          openObject(target).catch((caught: unknown) =>
            setError(caught instanceof Error ? caught.message : 'Opening the object failed. Try again.'),
          )
        }}
      >
        Investigate
      </Button>
      {error ? (
        <span role="alert" className="hd-error">
          {error}
        </span>
      ) : null}
    </>
  )
}

/**
 * The item's recommended command as its primary button, such as `Requeue 23`: an Operation over the item's group,
 * which confirms the scope the server counts before anything runs. A user who may not run it sees the button disabled
 * with the reason. A plain function rather than a component, so the compiler adds no memo cache of its own: the shell
 * sits at the edge of its size budget.
 */
function recommendedButton(operations: ReturnType<typeof useOperations>, format: ReturnType<typeof useFormat>, module: string, item: AttentionItem) {
  const recommended = item.recommended

  return recommended ? (
    <Button
      variant="primary"
      disabledReason={recommended.command.disabledReason}
      onPress={() => operations.start({ module, ...recommended, noun: `${recommended.command.objectKind}s in this group` })}
    >
      {`${recommended.command.label} ${format.count({ value: item.count, capped: item.capped })}`}
    </Button>
  ) : null
}

function ItemInspector({ section, item, onClose }: { section: ModuleSection<AttentionItem>; item: AttentionItem; onClose: () => void }) {
  const related = useRelated(item.target)
  const format = useFormat()

  return (
    <DockedInspector label={`Needs you: ${item.title}`} onClose={onClose}>
      <header className="hd-object-header">
        <h2 className="hd-title">{item.title}</h2>
        <StatusMark tone="needs" label={item.severity === 'critical' ? 'Needs you' : 'Warning'} />
      </header>
      <InspectorFields>
        <InspectorField label="Module">{section.displayName}</InspectorField>
        <InspectorField label="Count">{format.count({ value: item.count, capped: item.capped })}</InspectorField>
        {item.since ? (
          <InspectorField label="Since">
            <span title={format.exactTime(item.since)}>{format.relativeTime(item.since, { style: 'short' })}</span>
          </InspectorField>
        ) : null}
        {item.target ? (
          <InspectorField label="Object">
            <Mono>{item.target.id}</Mono>
          </InspectorField>
        ) : null}
      </InspectorFields>
      <div className="hd-actions">
        <InvestigateButton item={item} variant="primary" />
      </div>
      {item.target ? (
        <section aria-label="Related objects" className="hd-related">
          {related.isError ? (
            <p role="alert" className="hd-error">
              Related objects failed to load. {related.error.message}
            </p>
          ) : related.data ? (
            <>
              {related.data.groups.map((group) => (
                <div key={group.label} className="hd-related-group">
                  <h3 className="hd-label">{group.label}</h3>
                  <ul>
                    {group.items.map((object) => (
                      <li key={`${object.target.module}:${object.target.id}`}>
                        <span>{object.title}</span>
                        {object.traceUrl ? (
                          <a href={object.traceUrl} target="_blank" rel="noreferrer noopener">
                            Open trace
                          </a>
                        ) : null}
                      </li>
                    ))}
                  </ul>
                  {group.hasMore ? <p className="hd-hint">More in the object's own view.</p> : null}
                </div>
              ))}
              {related.data.stale.map((module) => (
                <p key={module.id} className="hd-stale-note">
                  <StatusMark tone="waiting" label="Stale" /> {module.displayName} did not answer in time.
                </p>
              ))}
            </>
          ) : null}
        </section>
      ) : null}
    </DockedInspector>
  )
}

function Summary({ sections }: { sections: readonly ModuleSection<AttentionItem>[] }) {
  const count = sections.reduce((sum, section) => sum + section.items.length, 0)
  const modules = sections.filter((section) => section.items.length > 0).length
  const format = useFormat()

  return (
    <DockedInspector label="Needs you summary">
      <h2 className="hd-title">Needs you</h2>
      <p className="hd-prose">
        {count === 0
          ? 'Nothing needs you now.'
          : `${count} ${count === 1 ? 'cause' : 'causes'} across ${modules} ${modules === 1 ? 'module' : 'modules'}, ${format.count(attentionTotal(sections))} items in all. Select one to see it here.`}
      </p>
    </DockedInspector>
  )
}

/**
 * Needs you: every module's attention items, grouped by module and then by cause. A module that timed out or failed
 * shows `Stale` in its place; the rest still list. New items arrive through `attention.changed`.
 */
export function NeedsYou() {
  const needsYou = useNeedsYou()
  const [params, setParams] = useSearchParams()
  const showSkeleton = useDelayed(needsYou.isPending)
  const selected = params.get('item')
  const format = useFormat()
  const operations = useOperations()

  function select(key: string | null) {
    setParams(
      (current) => {
        const next = new URLSearchParams(current)

        if (key) {
          next.set('item', key)
        } else {
          next.delete('item')
        }

        return next
      },
      { replace: true },
    )
  }

  useEffect(() => {
    if (!selected) {
      return undefined
    }

    // Esc clears the selection, unless a popover or dialog above the view takes it first.
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape' && !event.defaultPrevented && !document.querySelector('[role="dialog"]')) {
        setParams(
          (current) => {
            const next = new URLSearchParams(current)
            next.delete('item')
            return next
          },
          { replace: true },
        )
      }
    }

    document.addEventListener('keydown', onKeyDown)

    return () => document.removeEventListener('keydown', onKeyDown)
  }, [selected, setParams])

  if (needsYou.isPending) {
    return showSkeleton ? <div className="hd-skeleton" aria-label="Loading Needs you" /> : null
  }

  if (needsYou.isError) {
    return (
      <div role="alert" className="hd-banner">
        <span>Needs you failed to load. {needsYou.error.message}</span>
        <Button onPress={() => void needsYou.refetch()}>Retry</Button>
      </div>
    )
  }

  const sections = needsYou.data
  const current = sections
    .flatMap((section) => section.items.map((item) => ({ section, item })))
    .find(({ section, item }) => selectionKey(section.id, item.id) === selected)

  return (
    <div className="hd-view">
      {sections.every((section) => section.items.length === 0 && !section.stale) ? (
        <p className="hd-empty">Nothing needs you. A terminal failure or anything else only an operator can move appears here.</p>
      ) : null}
      {sections.map((section) => (
        <section key={section.id} aria-labelledby={`needs-${section.id}`} className="hd-section">
          <div className="hd-section-head">
            <h2 id={`needs-${section.id}`} className="hd-title">
              {section.displayName}
            </h2>
            {section.stale ? <StatusMark tone="waiting" label="Stale" /> : null}
          </div>
          {section.stale ? (
            <p className="hd-stale-note">{section.displayName} did not answer in time. Its items may be missing; the rest are current.</p>
          ) : section.items.length === 0 ? (
            <p className="hd-hint">Nothing needs you here.</p>
          ) : (
            groupByCause(section.items).map((cause) => (
              <ul key={cause.key} className="hd-cause" aria-label={cause.items[0]?.title ?? cause.key}>
                {cause.items.map((item) => {
                  const key = selectionKey(section.id, item.id)

                  return (
                    <li key={item.id} className="hd-attention-item" data-selected={key === selected ? '' : undefined}>
                      <button
                        type="button"
                        className="hd-attention-select"
                        aria-pressed={key === selected}
                        onClick={() => select(key === selected ? null : key)}
                      >
                        <span className="hd-attention-title">{item.title}</span>
                        <span className="hd-hint">
                          {`${format.count({ value: item.count, capped: item.capped })} items`}
                          {item.since ? ` · since ${format.relativeTime(item.since, { style: 'short' })}` : ''}
                          {item.recentCount ? ' · growing' : ''}
                        </span>
                      </button>
                      <span className="hd-actions">
                        {recommendedButton(operations, format, section.id, item)}
                        <InvestigateButton item={item} />
                      </span>
                    </li>
                  )
                })}
              </ul>
            ))
          )}
        </section>
      ))}
      {current ? <ItemInspector section={current.section} item={current.item} onClose={() => select(null)} /> : <Summary sections={sections} />}
    </div>
  )
}
