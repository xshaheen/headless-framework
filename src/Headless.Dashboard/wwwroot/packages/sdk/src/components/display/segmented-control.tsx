// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

export interface SegmentedControlProps<T extends string> {
  /** Names the choice, such as `Row density` or `History range`. */
  label: string
  options: readonly { value: T; label: string }[]
  value: T
  onChange: (value: T) => void
  /** `form` draws the taller segments a form uses, such as `Single job | Chain`. */
  size?: 'form'
}

/**
 * One choice among a few, such as `24 h | 7 days`, as a group of pressed-state buttons: the chosen one reads by its
 * weight and fill and by `aria-pressed`, never by colour alone. Plain buttons, because each is one Tab stop the
 * operator presses, and a radio group's arrow-key model adds code without adding meaning here.
 */
export function SegmentedControl<T extends string>({ label, options, value, onChange, size }: SegmentedControlProps<T>) {
  return (
    <div role="group" aria-label={label} className="inline-flex rounded-md border border-border bg-deep p-0.5">
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          className={`cursor-pointer rounded-sm text-faint transition-fast hover:text-foreground aria-pressed:bg-muted aria-pressed:font-medium aria-pressed:text-foreground ${size ? 'h-(--seg-form-height) px-2.5 type-small' : 'h-(--chip-height) px-2 type-label-regular'}`}
          aria-pressed={option.value === value}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  )
}
