/**
 * Escape the HTML-significant characters in server-supplied text.
 *
 * Message content is attacker-reachable, so every string that reaches a `v-html`
 * binding must pass through here first — only the markup the dashboard itself adds
 * (syntax-highlighting spans) may remain live.
 */
export function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}
