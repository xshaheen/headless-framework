import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { applyTheme, readTheme, ThemeControl } from '../src/layout/theme.tsx'

const storageKey = 'headless-dashboard:theme'

function preferColorScheme(scheme: 'light' | 'dark') {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query.includes(`prefers-color-scheme: ${scheme}`),
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }))
}

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('theme', () => {
  it.each(['light', 'dark'] as const)('boots dark with no stored choice when the system prefers %s', (scheme) => {
    preferColorScheme(scheme)

    applyTheme(readTheme())

    expect(document.documentElement.hasAttribute('data-theme')).toBe(false)
  })

  it('boots dark from a stored value it does not know, such as the retired system choice', () => {
    preferColorScheme('light')
    localStorage.setItem(storageKey, 'system')

    applyTheme(readTheme())

    expect(document.documentElement.hasAttribute('data-theme')).toBe(false)
  })

  it('boots light from a stored light choice, and switches back to dark from the theme control', async () => {
    const user = userEvent.setup()
    localStorage.setItem(storageKey, 'light')
    applyTheme(readTheme())
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')

    render(<ThemeControl />)
    const select = screen.getByRole('combobox', { name: 'Theme' })
    expect(within(select).getAllByRole('option').map((option) => option.textContent)).toEqual(['Dark', 'Light'])
    await user.selectOptions(select, 'Dark')

    expect(document.documentElement.hasAttribute('data-theme')).toBe(false)
    expect(localStorage.getItem(storageKey)).toBe('dark')
  })
})
