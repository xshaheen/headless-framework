// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

export interface SegmentedControlProps<T extends string> {
  /** Names the choice, such as `Row density` or `History range`. */
  label: string
  options: readonly { value: T; label: string }[]
  value: T
  onChange: (value: T) => void
}

/**
 * One choice among a few, such as `24 h | 7 days`, as a group of pressed-state buttons: the chosen one reads by its
 * weight and fill and by `aria-pressed`, never by colour alone. Plain buttons, because each is one Tab stop the
 * operator presses, and a radio group's arrow-key model adds code without adding meaning here. The track is one
 * control height, so it lines up with the buttons, chips, and compact fields beside it wherever it sits.
 */
export function SegmentedControl<T extends string>({ label, options, value, onChange }: SegmentedControlProps<T>) {
  return (
    // Its own width in any container: in a column of fields it would otherwise stretch into a bar unlike the same
    // control in a card's heading.
    <div role="group" aria-label={label} className="inline-flex h-(--control-height) w-fit items-center rounded-md border border-border bg-deep p-0.5">
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          className="h-(--seg-height) cursor-pointer rounded-sm px-2.5 type-small whitespace-nowrap text-faint transition-fast hover:text-foreground aria-pressed:bg-muted aria-pressed:font-medium aria-pressed:text-foreground"
          aria-pressed={option.value === value}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  )
}
