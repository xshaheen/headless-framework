import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { createFormatters } from '../../src/locale/formatters.ts'
import { gregorianLocale, LocaleProvider, TimeZoneField, useFormat, useLocale } from '../../src/locale/locale.tsx'

// 15:40 UTC on 5 Nov 2026 is 17:40 in Cairo (UTC+2) and 10:40 in New York (UTC−5).
const failedAt = '2026-11-05T15:40:00Z'
const createdAt = '2026-11-05T09:05:30Z'

function Page() {
  const format = useFormat()
  const { timeZone } = useLocale()

  return (
    <>
      <TimeZoneField />
      <span data-testid="chip">{timeZone}</span>
      <ul>
        <li>{`Failed ${format.exactTime(failedAt)}`}</li>
        <li>{`Created ${format.exactTime(new Date(createdAt))}`}</li>
        <li>{`Next fire ${format.zonedTime(failedAt, 'America/New_York')}`}</li>
      </ul>
    </>
  )
}

function renderPage() {
  return render(
    <LocaleProvider>
      <Page />
    </LocaleProvider>,
  )
}

function lines() {
  return within(screen.getByRole('list'))
    .getAllByRole('listitem')
    .map((item) => item.textContent)
}

beforeEach(() => localStorage.clear())
afterEach(() => localStorage.clear())

describe('display time zone', () => {
  it('shows UTC until the operator picks a zone', () => {
    renderPage()

    expect(screen.getByTestId('chip').textContent).toBe('UTC')
    expect(lines().slice(0, 2)).toEqual(['Failed Nov 5, 2026, 15:40:00 UTC', 'Created Nov 5, 2026, 09:05:30 UTC'])
  })

  it('moves every displayed time to Africa/Cairo when chosen, and keeps it after a reload', async () => {
    const user = userEvent.setup()
    const page = renderPage()

    await user.selectOptions(screen.getByLabelText('Time zone'), 'Africa/Cairo')

    const cairo = [
      'Failed Nov 5, 2026, 17:40:00 GMT+02:00 Africa/Cairo',
      'Created Nov 5, 2026, 11:05:30 GMT+02:00 Africa/Cairo',
    ]
    expect(screen.getByTestId('chip').textContent).toBe('Africa/Cairo')
    expect(lines().slice(0, 2)).toEqual(cairo)

    page.unmount()
    renderPage()

    expect(screen.getByTestId('chip').textContent).toBe('Africa/Cairo')
    expect((screen.getByLabelText('Time zone') as HTMLSelectElement).value).toBe('Africa/Cairo')
    expect(lines().slice(0, 2)).toEqual(cairo)
  })

  it('renders a scheduler-zone time in the zone passed, whatever the display zone', async () => {
    const user = userEvent.setup()
    renderPage()
    const nextFire = 'Next fire Nov 5, 2026, 10:40:00 GMT-05:00 America/New_York'

    expect(lines()[2]).toBe(nextFire)

    await user.selectOptions(screen.getByLabelText('Time zone'), 'Africa/Cairo')

    expect(lines()[2]).toBe(nextFire)
  })

  it('falls back to UTC when the stored zone is not one the runtime knows', () => {
    localStorage.setItem('headless-dashboard:time-zone', 'Mars/Olympus_Mons')

    renderPage()

    expect(screen.getByTestId('chip').textContent).toBe('UTC')
  })
})

describe('Gregorian calendar', () => {
  it('pins a locale whose own calendar is another one to Gregorian, keeping its language', () => {
    expect(gregorianLocale('ar-SA-u-ca-islamic')).toBe('ar-SA-u-ca-gregory')
    expect(gregorianLocale('fa-IR')).toBe('fa-IR-u-ca-gregory')
    expect(gregorianLocale('ar-EG')).toBe('ar-EG')
    expect(gregorianLocale('en')).toBe('en')
    expect(gregorianLocale('not a locale')).toBe('not a locale')
  })

  it('formats an Arabic page’s times in the Gregorian year the server stored', () => {
    function Year() {
      const { locale } = useLocale()

      return <span data-testid="year">{new Intl.DateTimeFormat(locale, { year: 'numeric', numberingSystem: 'latn' }).format(Date.parse(failedAt))}</span>
    }

    render(
      <LocaleProvider locale="ar-SA-u-ca-islamic">
        <Year />
      </LocaleProvider>,
    )

    expect(screen.getByTestId('year').textContent).toBe('2026')
  })
})

describe('duration formatter', () => {
  it('reads milliseconds, seconds, then minutes and seconds, then hours and minutes, in Intl units', () => {
    const format = createFormatters('en', 'UTC')

    expect(format.duration(340)).toBe('340 ms')
    expect(format.duration(1_240)).toBe('1.2 sec')
    expect(format.duration(184_000)).toBe('3 min 4 sec')
    expect(format.duration(7_500_000)).toBe('2 hr 5 min')
  })

  it.each([
    [999.6, '1 sec'],
    [59_960, '1 min'],
    [239_600, '4 min'],
    [3_599_600, '1 hr'],
  ])('rounds %d ms before it picks the unit, so it reads %s and never 60 of the smaller one', (ms, text) => {
    expect(createFormatters('en', 'UTC').duration(ms)).toBe(text)
  })

  it('names the units in the locale', () => {
    const format = createFormatters('ar', 'UTC')

    expect(format.duration(184_000)).toBe(
      new Intl.ListFormat('ar', { style: 'narrow', type: 'unit' }).format([
        new Intl.NumberFormat('ar', { style: 'unit', unit: 'minute', unitDisplay: 'short' }).format(3),
        new Intl.NumberFormat('ar', { style: 'unit', unit: 'second', unitDisplay: 'short' }).format(4),
      ]),
    )
    expect(format.duration(184_000)).not.toContain('min')
  })
})

describe('size formatter', () => {
  it('steps up through kB, MB, and GB, 1,024 to the step, rounding before it picks the unit', () => {
    const format = createFormatters('en', 'UTC')

    expect(format.size(812)).toBe('812 bytes')
    expect(format.size(70 * 1024)).toBe('70 kB')
    expect(format.size(1.5 * 1024 ** 2)).toBe('1.5 MB')
    expect(format.size(2.1 * 1024 ** 3)).toBe('2.1 GB')
    expect(format.size(1024 ** 2 - 20)).toBe('1 MB')
  })
})
