/**
 * Pretty-print a raw JSON string for display.
 *
 * The result is plain text and MUST be rendered through text interpolation (`{{ }}`),
 * never `v-html`: job request payloads and function example data are server-supplied
 * and may contain markup that would otherwise execute in the operator's session.
 * Use CSS (`white-space: pre-wrap`) to preserve the indentation, not `<br>`/`&nbsp;`.
 *
 * Returns `undefined` for empty or unparseable input so callers pick their own fallback.
 */
export function formatJsonForDisplay(json: string | null | undefined): string | undefined {
  if (!json) {
    return undefined
  }

  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    return undefined
  }
}
