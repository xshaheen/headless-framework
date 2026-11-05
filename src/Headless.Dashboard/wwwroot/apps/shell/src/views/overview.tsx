import { Button, StatusMark } from '@headless/dashboard-sdk/components'
import { formatCount } from '@headless/dashboard-sdk/data'
import { Link } from '@headless/dashboard-sdk/router'
import { comparisonText, exactTime, figureText, relativeTime } from '../format.ts'
import { attentionTotal, useNeedsYou, useOverview, type Figure, type ModuleSection } from '../shell-data.ts'
import { useDelayed } from './use-delayed.ts'

const topItems = 5

function FigureCell({ module, figure }: { module: string; figure: Figure }) {
  const comparison = figure.comparison ? comparisonText(figure.value, figure.comparison, figure.unit) : undefined

  return (
    <div className="hd-figure">
      <span className="hd-figure-module">{module}</span>
      <span className="hd-figure-value">{figureText(figure.value, figure.unit)}</span>
      <span className="hd-figure-label">
        {figure.label}
        {comparison ? (
          <>
            {', '}
            <span className={comparison.better === false ? 'hd-delta-worse' : undefined}>{comparison.text}</span>
          </>
        ) : null}
      </span>
    </div>
  )
}

function StaleNote({ sections }: { sections: readonly ModuleSection<unknown>[] }) {
  const stale = sections.filter((section) => section.stale)

  return stale.length > 0 ? (
    <p className="hd-stale-note">
      <StatusMark tone="waiting" label="Stale" />
      {` ${stale.map((section) => section.displayName).join(', ')} did not answer in time. What shows for ${stale.length === 1 ? 'it' : 'them'} may be missing.`}
    </p>
  ) : null
}

/** Overview: what needs the operator first, then every module's figures with their comparisons. */
export function Overview() {
  const needsYou = useNeedsYou()
  const overview = useOverview()
  const showSkeleton = useDelayed(needsYou.isPending || overview.isPending)
  const sections = needsYou.data ?? []
  const items = sections.flatMap((section) => section.items.map((item) => ({ section, item })))
  const total = attentionTotal(sections)

  return (
    <div className="hd-view">
      <section aria-labelledby="overview-needs-you" className="hd-section">
        <div className="hd-section-head">
          <h2 id="overview-needs-you" className="hd-title">
            Needs you
          </h2>
          {items.length > 0 ? (
            <span className="hd-hint">{`${items.length} ${items.length === 1 ? 'cause' : 'causes'} · ${formatCount(total)} items`}</span>
          ) : null}
          <span className="hd-docbar-spacer" />
          <Link to="/needs-you">Open Needs you</Link>
        </div>
        {needsYou.isError ? (
          <div role="alert" className="hd-banner">
            <span>Needs you failed to load. {needsYou.error.message}</span>
            <Button onPress={() => void needsYou.refetch()}>Retry</Button>
          </div>
        ) : needsYou.isPending ? (
          showSkeleton ? <div className="hd-skeleton" aria-label="Loading Needs you" /> : null
        ) : items.length === 0 ? (
          <p className="hd-prose hd-muted">Nothing needs you. Items appear here when only an operator can move them.</p>
        ) : (
          <ul className="hd-attention-lines">
            {items.slice(0, topItems).map(({ section, item }) => (
              <li key={`${section.id}:${item.id}`} className="hd-attention-line">
                <span className="hd-attention-what">
                  <span className="hd-attention-title">{item.title}</span>
                  <span className="hd-hint">{section.displayName}</span>
                </span>
                <span className="hd-attention-count">{formatCount({ value: item.count, capped: item.capped })}</span>
                {item.since ? (
                  <span className="hd-hint hd-attention-when" title={exactTime(item.since)}>
                    since {relativeTime(item.since)}
                  </span>
                ) : (
                  <span />
                )}
              </li>
            ))}
          </ul>
        )}
        {needsYou.data ? <StaleNote sections={needsYou.data} /> : null}
      </section>
      <section aria-labelledby="overview-figures" className="hd-section">
        <h2 id="overview-figures" className="hd-title">
          Figures
        </h2>
        {overview.isError ? (
          <div role="alert" className="hd-banner">
            <span>The figures failed to load. {overview.error.message}</span>
            <Button onPress={() => void overview.refetch()}>Retry</Button>
          </div>
        ) : overview.isPending ? (
          showSkeleton ? <div className="hd-skeleton" aria-label="Loading figures" /> : null
        ) : overview.data.every((section) => section.items.length === 0 && !section.stale) ? (
          <p className="hd-prose hd-muted">No module reports figures yet. A module adds up to three when it registers an overview source.</p>
        ) : (
          <>
            <div className="hd-figures">
              {overview.data.flatMap((section) =>
                section.items.map((figure) => <FigureCell key={`${section.id}:${figure.id}`} module={section.displayName} figure={figure} />),
              )}
            </div>
            <StaleNote sections={overview.data} />
          </>
        )}
      </section>
    </div>
  )
}
