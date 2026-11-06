import { SelectField } from '@headless/dashboard-sdk/components'
import { useState } from 'react'

export type Theme = 'dark' | 'light'

const storageKey = 'headless-dashboard:theme'

function isTheme(value: unknown): value is Theme {
  return value === 'dark' || value === 'light'
}

/**
 * The theme this browser chose. Dark is the default whatever the system setting, so a value this build does not
 * know, such as an older `system`, an empty store, or storage that throws, all read as dark.
 */
export function readTheme(): Theme {
  try {
    return localStorage.getItem(storageKey) === 'light' ? 'light' : 'dark'
  } catch {
    return 'dark'
  }
}

/** Applies a theme to the document: the dark tokens sit on `:root`, and only light needs the attribute. */
export function applyTheme(theme: Theme, root: HTMLElement = document.documentElement) {
  if (theme === 'light') {
    root.setAttribute('data-theme', 'light')
  } else {
    root.removeAttribute('data-theme')
  }
}

const options = [
  { value: 'dark', label: 'Dark' },
  { value: 'light', label: 'Light' },
]

/** Dark or light; remembered per browser. */
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
