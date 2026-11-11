import { useMemo } from 'react'
import type { ListRange } from '../data/keyset.ts'
import { useLocale } from '../locale/locale.tsx'
import { useSearchParams } from '../router/index.ts'
import {
  timeRangeFromSearch,
  timeRangePresets,
  timeRangeToSearch,
  toListRange,
  type TimeRange,
  type TimeRangePreset,
  type TimeRangeSearchOptions,
} from './time-range.ts'

/**
 * Keeps a view's time range in the URL, so a filtered list can be linked, reloaded, and gone back to: `?range=7d` for
 * a preset, `?from=2026-11-01&to=2026-11-11` for custom days. A value nobody recognises reads as `defaultRange`, and
 * the default writes no params. Each change is a history entry, so Back returns to the range before it.
 *
 * Pass `presets` and `defaultRange` from module scope rather than as inline literals: the returned range keeps its
 * identity while they and the three params stay the same.
 */
export function useTimeRangeSearch({
  presets = timeRangePresets,
  defaultRange,
}: TimeRangeSearchOptions = {}): [TimeRange | undefined, (range: TimeRange | undefined) => void] {
  const [params, setParams] = useSearchParams()
  const range = params.get('range')
  const from = params.get('from')
  const to = params.get('to')
  const value = useMemo(
    () => timeRangeFromSearch({ range, from, to }, { presets, defaultRange }),
    [range, from, to, presets, defaultRange],
  )

  function change(next: TimeRange | undefined) {
    setParams((current) => {
      const merged = new URLSearchParams(current)

      for (const [key, text] of Object.entries(timeRangeToSearch(next, defaultRange))) {
        if (text === undefined) {
          merged.delete(key)
        } else {
          merged.set(key, text)
        }
      }

      return merged
    })
  }

  return [value, change]
}

/**
 * A range as the instants a keyset list reads, in the display time zone. A preset resolves when the range changes, not
 * on every render: a list's range is part of its cache key, so a window that moved on each render would read the first
 * page again forever. A rolling window therefore ends at the moment it was picked until the operator picks again or
 * the view mounts again.
 */
export function useListRange(
  range: TimeRange | undefined,
  presets: readonly TimeRangePreset[] = timeRangePresets,
): ListRange | undefined {
  const { timeZone } = useLocale()
  const preset = range?.kind === 'preset' ? range.preset : undefined
  const start = range?.kind === 'custom' ? range.start : undefined
  const end = range?.kind === 'custom' ? range.end : undefined

  return useMemo(() => {
    if (preset !== undefined) {
      return toListRange({ kind: 'preset', preset }, { timeZone, presets })
    }

    return start !== undefined && end !== undefined
      ? toListRange({ kind: 'custom', start, end }, { timeZone, presets })
      : undefined
  }, [preset, start, end, timeZone, presets])
}
