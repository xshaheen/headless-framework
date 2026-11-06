/**
 * The media conditions for the named breakpoints in `tokens.css` (`--bp-wide`, `--bp-medium`, `--bp-phone`). A media
 * condition cannot read a custom property, so script and CSS both write the widths as literals; each condition matches
 * the widths below its breakpoint, one pixel under it, as the CSS rules do.
 */
export const below = {
  wide: '(max-width: 1279px)',
  medium: '(max-width: 1023px)',
  phone: '(max-width: 767px)',
} as const
