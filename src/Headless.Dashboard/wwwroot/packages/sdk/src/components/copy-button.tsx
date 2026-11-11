// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { useRef, useState } from 'react'
import { CopyIcon } from '../icons/index.ts'
import { Button } from './button.tsx'
import { Announcer } from './live.tsx'

export interface CopyButtonProps {
  /** The text the clipboard receives: the stored value, not what the page draws of it. */
  text: string
  /** A visible label, such as `Copy` or `Copy all`. Without one the button is the copy icon alone. */
  label?: string
  /** The accessible name, which an icon-only button needs: `Copy id 01a1…`. A labelled button defaults to its label. */
  'aria-label'?: string
  /** A shorter name for an icon-only button's tooltip when `aria-label` carries the value, such as `Copy id`. */
  tooltip?: string
}

type Outcome = 'copied' | 'failed' | undefined

/**
 * Copies text to the clipboard and says so: a polite live region announces `Copied`, and a labelled button reads
 * `Copied` until the next press. A browser that refuses the clipboard API (an insecure origin, a denied permission, an
 * embedding frame) falls back to the document's copy command; when that fails too, the region says so, so a press is
 * never silent.
 */
export function CopyButton({ text, label, 'aria-label': ariaLabel, tooltip }: CopyButtonProps) {
  const [outcome, setOutcome] = useState<Outcome>()
  const [presses, setPresses] = useState(0)
  const button = useRef<HTMLButtonElement>(null)

  async function copy() {
    let isCopied = false

    try {
      await navigator.clipboard.writeText(text)
      isCopied = true
    } catch {
      isCopied = copyThroughSelection(text, button.current)
    }

    setOutcome(isCopied ? 'copied' : 'failed')
    setPresses((count) => count + 1)
  }

  const message = outcome === 'copied' ? 'Copied' : outcome === 'failed' ? 'Copy failed. Select the text to copy it.' : ''

  return (
    <>
      <Button
        ref={button}
        variant="ghost"
        icon={label === undefined ? CopyIcon : undefined}
        aria-label={ariaLabel ?? label}
        tooltip={label === undefined ? (tooltip ?? ariaLabel) : undefined}
        onPress={() => void copy()}
      >
        {label === undefined ? undefined : outcome === 'copied' ? 'Copied' : label}
      </Button>
      <Announcer message={message} id={presses} />
    </>
  )
}

/** The pre-clipboard-API path: select the text in an off-screen field and run the copy command, then hand focus back. */
function copyThroughSelection(text: string, returnFocus: HTMLElement | null) {
  const field = document.createElement('textarea')

  field.value = text
  field.setAttribute('readonly', '')
  field.className = 'fixed -start-[9999px] top-0'
  document.body.append(field)
  field.focus()
  field.select()

  try {
    // The command is deprecated but is the only copy a browser allows once it refuses the clipboard API.
    return document.execCommand('copy')
  } catch {
    return false
  } finally {
    field.remove()
    returnFocus?.focus()
  }
}
