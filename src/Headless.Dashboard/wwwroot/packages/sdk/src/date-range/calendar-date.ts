import { fromAbsolute, parseDate, toCalendarDate, today, type CalendarDate } from '@internationalized/date'
import { useEffect, useReducer } from 'react'

/** An ISO day (`2026-11-11`) as a calendar date, or `null` when the text is not a real day. */
export function parseCalendarDay(text: string): CalendarDate | null {
  try {
    return parseDate(text)
  } catch {
    return null
  }
}

/** The ISO day an instant falls on in `timeZone`. */
export function calendarDayOf(instant: number, timeZone: string): string {
  return toCalendarDate(fromAbsolute(instant, timeZone)).toString()
}

/**
 * Milliseconds from `now` until the date changes in `timeZone`. Not always the rest of 24 hours: a day that starts or
 * ends daylight saving time is an hour short or long, and where the clocks move at midnight (Cairo, Beirut) the new
 * day starts at 01:00.
 */
export function msUntilNextDay(now: number, timeZone: string): number {
  return toCalendarDate(fromAbsolute(now, timeZone)).add({ days: 1 }).toDate(timeZone).getTime() - now
}

/**
 * Today in `timeZone`, kept current: a calendar left open overnight moves its today mark at that zone's midnight
 * without waiting for anything else to render it again.
 */
export function useToday(timeZone: string): CalendarDate {
  const [rollovers, rollOver] = useReducer((count: number) => count + 1, 0)

  useEffect(() => {
    // Armed again after every firing, so a timer the browser ran a moment early still leads to the real midnight
    // instead of leaving yesterday marked.
    const timer = setTimeout(rollOver, msUntilNextDay(Date.now(), timeZone) + 50)

    return () => clearTimeout(timer)
  }, [rollovers, timeZone])

  return today(timeZone)
}
