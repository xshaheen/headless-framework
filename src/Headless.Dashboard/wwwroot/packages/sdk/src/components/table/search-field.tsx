'use no memo'

import { useEffect, useRef, useState } from 'react'
import { Input, Label, SearchField as AriaSearchField, Button as AriaButton } from 'react-aria-components'
import { CloseIcon } from '../../icons/index.ts'
import { Button } from '../button.tsx'
import { inputClass } from '../form.tsx'

export interface SearchFieldProps {
  /** Names the field, such as `Search runs`. */
  label: string
  /**
   * The text the list should search for, trimmed; an empty string clears the search. A list passes it to its keyset
   * hook as `search`, which drops the old request and reads the first page again.
   */
  onSearch: (text: string) => void
  /**
   * `debounce`, the default, searches once typing pauses for `delay` ms. `submit` searches only on `Enter` or the
   * `Search` button, for a search the server pays for on every request, such as one through message content.
   */
  mode?: 'debounce' | 'submit'
  /** The pause, in ms, a debounced search waits for. */
  delay?: number
  /** The text the field starts with, such as a search restored from the view's state. */
  defaultValue?: string
  placeholder?: string
}

const defaultDelay = 300

/**
 * A list's text search. While typing, a debounced field waits for a pause and a submit field sends nothing; `Esc`
 * or the clear button clears the search at once in either mode. The same text is never sent twice in a row.
 */
export function SearchField({ label, onSearch, mode = 'debounce', delay = defaultDelay, defaultValue = '', placeholder }: SearchFieldProps) {
  const [text, setText] = useState(defaultValue)
  const sent = useRef(defaultValue.trim())
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  function cancel() {
    clearTimeout(timer.current)
    timer.current = undefined
  }

  function send(value: string) {
    cancel()
    const next = value.trim()

    if (next !== sent.current) {
      sent.current = next
      onSearch(next)
    }
  }

  function change(value: string) {
    setText(value)

    if (mode === 'debounce') {
      cancel()
      timer.current = setTimeout(() => send(value), delay)
    }
  }

  useEffect(() => cancel, [])

  // The `Search` button sits outside React Aria's field: inside it, the field's button slot would make it a clear button.
  return (
    <div className="flex min-w-0 items-center gap-1.5">
      <AriaSearchField className="relative flex min-w-0 flex-1 items-center" value={text} onChange={change} onSubmit={send} onClear={() => send('')}>
        <Label className="sr-only">{label}</Label>
        <Input className={`${inputClass} pe-7 [&::-webkit-search-cancel-button]:hidden`} placeholder={placeholder} />
        {text !== '' ? (
          <AriaButton className="absolute end-1 grid size-6 cursor-pointer place-content-center rounded-sm text-muted-foreground outline-none hover:text-foreground data-focus-visible:ring-2 data-focus-visible:ring-ring">
            <CloseIcon className="text-current" />
          </AriaButton>
        ) : null}
      </AriaSearchField>
      {mode === 'submit' ? (
        <Button variant="secondary" onPress={() => send(text)}>
          Search
        </Button>
      ) : null}
    </div>
  )
}
