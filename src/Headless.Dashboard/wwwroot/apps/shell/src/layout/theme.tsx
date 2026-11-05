import { SelectField } from '@headless/dashboard-sdk/components'
import { useState } from 'react'

export type Theme = 'system' | 'light' | 'dark'

const storageKey = 'headless-dashboard:theme'

function isTheme(value: unknown): value is Theme {
  return value === 'system' || value === 'light' || value === 'dark'
}

/** The theme this browser chose; `system` when it chose none or storage is unavailable. */
export function readTheme(): Theme {
  try {
    const value = localStorage.getItem(storageKey)

    return isTheme(value) ? value : 'system'
  } catch {
    return 'system'
  }
}

/** Applies a theme to the document: `system` leaves the choice to `prefers-color-scheme`. */
export function applyTheme(theme: Theme, root: HTMLElement = document.documentElement) {
  if (theme === 'system') {
    root.removeAttribute('data-theme')
  } else {
    root.setAttribute('data-theme', theme)
  }
}

const options = [
  { value: 'system', label: 'System' },
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
]

/** Light, dark, or the system setting; remembered per browser. */
export function ThemeControl() {
  const [theme, setTheme] = useState(readTheme)

  return (
    <SelectField
      label="Theme"
      isRequired
      value={theme}
      options={options}
      onChange={(value) => {
        if (!isTheme(value)) {
          return
        }

        setTheme(value)
        applyTheme(value)

        try {
          localStorage.setItem(storageKey, value)
        } catch {
          // The choice still applies to this page; it is only not remembered.
        }
      }}
    />
  )
}
