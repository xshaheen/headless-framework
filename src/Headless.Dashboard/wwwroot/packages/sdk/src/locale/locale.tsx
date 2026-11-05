import { createContext, useContext, useMemo, useState, type ReactNode } from 'react'
import { I18nProvider } from 'react-aria-components'
import { SelectField } from '../components/form.tsx'
import { createFormatters, isTimeZone, type Formatters } from './formatters.ts'

/** The dashboard ships in English; a translation sets another locale here without touching a view. */
export const defaultLocale = 'en'

/** The display time zone until the operator picks one in the account menu. */
export const defaultTimeZone = 'UTC'

const storageKey = 'headless-dashboard:time-zone'

/** The display time zone this browser chose; UTC when it chose none, the stored zone is unknown, or storage fails. */
export function readTimeZone(): string {
  try {
    const value = localStorage.getItem(storageKey)

    return value && isTimeZone(value) ? value : defaultTimeZone
  } catch {
    return defaultTimeZone
  }
}

interface LocaleContextValue {
  locale: string
  timeZone: string
  setTimeZone: (timeZone: string) => void
  format: Formatters
}

const LocaleContext = createContext<LocaleContextValue | null>(null)

export interface LocaleProviderProps {
  locale?: string
  children: ReactNode
}

/**
 * Holds the locale and the display time zone every formatter reads, and hands the locale to React Aria. The zone
 * is remembered per browser.
 */
export function LocaleProvider({ locale = defaultLocale, children }: LocaleProviderProps) {
  const [timeZone, setZone] = useState(readTimeZone)
  const value = useMemo<LocaleContextValue>(
    () => ({
      locale,
      timeZone,
      format: createFormatters(locale, timeZone),
      setTimeZone: (next) => {
        if (!isTimeZone(next)) {
          return
        }

        setZone(next)

        try {
          localStorage.setItem(storageKey, next)
        } catch {
          // The zone still applies to this page; it is only not remembered.
        }
      },
    }),
    [locale, timeZone],
  )

  return (
    <LocaleContext.Provider value={value}>
      <I18nProvider locale={locale}>{children}</I18nProvider>
    </LocaleContext.Provider>
  )
}

/** The locale, the display time zone, and the setter the account menu calls. */
export function useLocale() {
  const value = useContext(LocaleContext)

  if (!value) {
    throw new Error('useLocale must be called inside <LocaleProvider>; DashboardProvider and renderModule provide it.')
  }

  return value
}

/** The formatters bound to the current locale and display time zone. */
export function useFormat(): Formatters {
  return useLocale().format
}

let zones: readonly string[] | undefined

function timeZoneOptions() {
  // The runtime's list omits UTC, the default, so it leads.
  zones ??= [defaultTimeZone, ...Intl.supportedValuesOf('timeZone').filter((zone) => zone !== defaultTimeZone)]

  return zones.map((zone) => ({ value: zone, label: zone.replaceAll('_', ' ') }))
}

/** The account menu's display time zone choice: every time on the page follows it, and the browser remembers it. */
export function TimeZoneField() {
  const { timeZone, setTimeZone } = useLocale()

  return <SelectField label="Time zone" isRequired value={timeZone} options={timeZoneOptions()} onChange={setTimeZone} />
}
