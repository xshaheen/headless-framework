// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { FormEvent, ReactNode } from 'react'
import type { IconComponent } from '../../icons/index.ts'
import { Button } from '../button.tsx'
import { cardHeading, cardSurface, headingRow } from './card.tsx'

export interface LookupFormProps {
  /** The card's heading, which also names its region and its search form: `Check a key`. */
  title: string
  /** What the card is about, from `@headless/dashboard-sdk/icons`, before the heading. */
  icon?: IconComponent
  /** One sentence under the heading on what the lookup answers. */
  description?: ReactNode
  /** The form's fields, such as a `TextInputField` per value the lookup needs, laid out in one wrapping row. */
  children: ReactNode
  /** The submit button's label; `Look up` by default. */
  submitLabel?: string
  /** A lookup is in flight: the button shows its pending state, and a second submit does nothing. */
  isPending?: boolean
  /** Called on submit, by the button or Enter in a field, with the page's own navigation already prevented. */
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
  /** A note under the fields, such as what a key looks like. */
  hint?: ReactNode
  /**
   * What the lookup found, under a hairline. The module owns it: the answer, or a `Banner` or `EmptyState` when the
   * lookup found nothing or failed.
   */
  result?: ReactNode
}

/**
 * A card that looks one thing up, such as the state of an idempotency key or a rate-limit partition: the heading,
 * a search form of fields and its submit button, an optional hint, and the result below.
 */
export function LookupForm({
  title,
  icon: Icon,
  description,
  children,
  submitLabel = 'Look up',
  isPending = false,
  onSubmit,
  hint,
  result,
}: LookupFormProps) {
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    // Enter in a field submits the form without pressing the button, so the button's pending state alone would let
    // a second lookup start.
    if (!isPending) {
      onSubmit(event)
    }
  }

  return (
    <section className={cardSurface} aria-label={title}>
      <header className={headingRow}>
        <h2 className={cardHeading}>
          {Icon ? <Icon /> : null}
          {title}
        </h2>
      </header>
      <div className="flex flex-col gap-2.5 p-3.5">
        {description != null ? <p className="hd-hint">{description}</p> : null}
        <form role="search" aria-label={title} className="flex flex-wrap items-end gap-2.5" onSubmit={submit}>
          {children}
          <Button type="submit" variant="primary" isPending={isPending}>
            {submitLabel}
          </Button>
        </form>
        {hint != null ? <p className="hd-hint">{hint}</p> : null}
      </div>
      {result != null ? <div className="border-t border-border p-3.5">{result}</div> : null}
    </section>
  )
}
