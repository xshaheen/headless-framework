// The React Compiler's memo caches cost the SDK more bytes than these rare renders save, and the shell's assets sit
// near their size budget.
'use no memo'

import { useState } from 'react'
import { remember } from '../breakpoints.ts'
import { SegmentedControl } from '../display/segmented-control.tsx'

/** How tall a list's rows are: 34 px, or 28 px on a long list read at a glance. */
export type Density = 'comfortable' | 'compact'

/** The row heights `tokens.css` sets as `--row-height` and `--row-height-compact`; the virtualizer lays rows out by them. */
export const rowHeights: Readonly<Record<Density, number>> = { comfortable: 34, compact: 28 }

const storagePrefix = 'headless-dashboard:density:'

/** One view's row density, remembered per browser under the view's id; comfortable when storage fails. */
export function useDensity(viewId: string | undefined) {
  const key = storagePrefix + viewId
  const [density, setState] = useState<Density>(() => (viewId && remember(key) === 'compact' ? 'compact' : 'comfortable'))

  function setDensity(next: Density) {
    setState(next)

    if (viewId) {
      remember(key, next)
    }
  }

  return [density, setDensity] as const
}

const densities = [
  { value: 'comfortable', label: 'Comfortable' },
  { value: 'compact', label: 'Compact' },
] as const

/** The list's density choice: comfortable or compact rows. */
export function DensityToggle({ value, onChange }: { value: Density; onChange: (next: Density) => void }) {
  return <SegmentedControl label="Row density" options={densities} value={value} onChange={onChange} />
}
