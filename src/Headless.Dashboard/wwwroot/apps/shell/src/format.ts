import type { Formatters } from '@headless/dashboard-sdk/locale'

/** A figure's value with its unit: `98.2 %`, `1.2 k/min`, `312`. */
export function figureText(format: Formatters, value: number, unit?: string | null) {
  const text = format.number(value, { maximumFractionDigits: 1 })

  return unit ? `${text} ${unit}` : text
}

/** A figure's comparison: `−1.1 vs previous hour`, with whether the change is good news. */
export function comparisonText(
  format: Formatters,
  value: number,
  comparison: { label: string; value: number | null; higherIsBetter?: boolean },
  unit?: string | null,
) {
  if (comparison.value === null) {
    return { text: comparison.label }
  }

  const delta = value - comparison.value
  const sign = delta > 0 ? '+' : delta < 0 ? '−' : '±'
  const text = `${sign}${format.number(Math.abs(delta), { maximumFractionDigits: 1 })}${unit ? ` ${unit}` : ''} vs ${comparison.label}`
  const better: boolean | undefined = delta === 0 ? undefined : (delta > 0) === (comparison.higherIsBetter ?? true)

  return { text, better }
}
