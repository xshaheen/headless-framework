/**
 * A list count the server stopped counting at a cap, so no count runs past it: `{ value: 10000, capped: true }`
 * reads `10,000+`.
 */
export interface CappedCount {
  value: number
  capped: boolean
}

/** Caps an exact count at `cap`. */
export function capCount(value: number, cap: number): CappedCount {
  return value > cap ? { value: cap, capped: true } : { value, capped: false }
}

/**
 * Formats a count for display in `locale`: `312`, or `10,000+` when the server capped it. A view reads `count` from
 * `useFormat()`, which passes the dashboard's locale.
 */
export function formatCount(count: CappedCount, locale: string) {
  const text = new Intl.NumberFormat(locale).format(count.value)

  return count.capped ? `${text}+` : text
}
