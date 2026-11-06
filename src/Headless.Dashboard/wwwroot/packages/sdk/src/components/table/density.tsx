import { useState } from 'react'
import { Button } from '../button.tsx'

/** How tall a list's rows are: 34 px, or 28 px on a long list read at a glance. */
export type Density = 'comfortable' | 'compact'

/** The row heights `tokens.css` sets as `--row-height` and `--row-height-compact`; the virtualizer lays rows out by them. */
export const rowHeights: Readonly<Record<Density, number>> = { comfortable: 34, compact: 28 }

const storagePrefix = 'headless-dashboard:density:'

function readDensity(viewId: string | undefined): Density {
  if (!viewId) {
    return 'comfortable'
  }

  try {
    return localStorage.getItem(storagePrefix + viewId) === 'compact' ? 'compact' : 'comfortable'
  } catch {
    return 'comfortable'
  }
}

/** One view's row density, remembered per browser under the view's id; comfortable when storage fails. */
export function useDensity(viewId: string | undefined) {
  const [density, setState] = useState(() => readDensity(viewId))

  function setDensity(next: Density) {
    setState(next)

    if (!viewId) {
      return
    }

    try {
      localStorage.setItem(storagePrefix + viewId, next)
    } catch {
      // The density still applies to this page; it is only not remembered.
    }
  }

  return [density, setDensity] as const
}

/** The list's density choice: comfortable or compact rows, as two pressed-state buttons. */
export function DensityToggle({ value, onChange }: { value: Density; onChange: (next: Density) => void }) {
  return (
    <div role="group" aria-label="Row density" className="hd-density">
      {(['comfortable', 'compact'] as const).map((density) => (
        <Button key={density} variant="ghost" aria-pressed={value === density} onPress={() => onChange(density)}>
          {density === 'comfortable' ? 'Comfortable' : 'Compact'}
        </Button>
      ))}
    </div>
  )
}
