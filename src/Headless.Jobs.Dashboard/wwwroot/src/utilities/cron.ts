import cronstrue from 'cronstrue'

/**
 * Jobs cron expressions are required to be 6-segment (seconds included).
 */
export function hasSecondsSegment(expression: string | null | undefined): boolean {
  if (!expression) return false
  return expression.trim().split(/\s+/).length === 6
}

/**
 * A cron expression is valid when it has the 6-segment shape AND cronstrue can parse it.
 */
export function isValidCronExpression(expression: string | null | undefined): boolean {
  if (!hasSecondsSegment(expression)) return false

  try {
    cronstrue.toString(expression as string)
    return true
  } catch {
    return false
  }
}

/**
 * Human-readable description of a cron expression, or the fallback when it cannot be parsed.
 */
export function describeCron(expression: string | null | undefined, fallback = ''): string {
  if (!expression) return fallback

  try {
    return cronstrue.toString(expression)
  } catch {
    return fallback
  }
}
