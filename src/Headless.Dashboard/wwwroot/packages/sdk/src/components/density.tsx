// The React Compiler's memo caches cost the SDK more bytes than these rare renders save, and the shell's assets sit
// near their size budget.
'use no memo'

import { create } from 'zustand'
import { remember } from './breakpoints.ts'
import { SelectField } from './form.tsx'

/** How tall every list's rows are: 34 px, or 28 px for long lists read at a glance. */
export type Density = 'comfortable' | 'compact'

/** The row heights `tokens.css` sets as `--row-height` and `--row-height-compact`; the virtualizer lays rows out by them. */
export const rowHeights: Readonly<Record<Density, number>> = { comfortable: 34, compact: 28 }

const storageKey = 'headless-dashboard:density'

function isDensity(value: unknown): value is Density {
  return value === 'comfortable' || value === 'compact'
}

interface DensityState {
  density: Density
  setDensity: (next: Density) => void
}

// One choice for every list, as the theme is one choice for every page: the account menu sets it and each open table
// follows at once, so it lives in a store both read. Comfortable when storage is empty or fails.
const useDensityStore = create<DensityState>((set) => ({
  density: remember(storageKey) === 'compact' ? 'compact' : 'comfortable',
  setDensity: (next) => {
    remember(storageKey, next)
    set({ density: next })
  },
}))

/** The row density this browser chose, and the call that changes it for every list. */
export function useDensity() {
  const density = useDensityStore((state) => state.density)
  const setDensity = useDensityStore((state) => state.setDensity)

  return [density, setDensity] as const
}

const options = [
  { value: 'comfortable', label: 'Comfortable' },
  { value: 'compact', label: 'Compact' },
]

/**
 * Comfortable or compact rows in every list; remembered per browser. It sits in the account menu beside the theme, so
 * no list spends a row of its own on a choice made once.
 */
export function DensityControl() {
  const [density, setDensity] = useDensity()

  return (
    <SelectField
      label="Row density"
      isRequired
      value={density}
      options={options}
      onChange={(value) => {
        if (isDensity(value)) {
          setDensity(value)
        }
      }}
    />
  )
}
