// The React Compiler's memo caches cost the SDK more bytes than this small block saves, and the shell's assets sit
// near their size budget.
'use no memo'

import { useState } from 'react'
import { Button } from '../button.tsx'

export interface ExceptionBlockProps {
  /** The exception's type, such as `System.TimeoutException`; left out when the server does not serve it. */
  type?: string
  message: string
  /**
   * Everything under the message, as stored, one line each. A line that starts `at` is a frame; any other line, such
   * as an inner exception's `Type: message` or `--- End of inner exception stack trace ---`, reads as it is.
   */
  stack?: string
}

/** The lines shown before `Full stack`: enough to see where it threw without pushing the fields out of view. */
const shownLines = 3

/** A failure's exception: the type in the danger tone, the message, and its stack, with Full stack and Copy. */
export function ExceptionBlock({ type, message, stack }: ExceptionBlockProps) {
  const [full, setFull] = useState(false)
  const lines = stackLines(stack)

  return (
    <div className="hd-exception">
      <pre className="hd-mono">
        {type ? <b>{type}: </b> : null}
        {message}
        {(full ? lines : lines.slice(0, shownLines)).map((line, index) => (
          <span key={index}>{`\n${line}`}</span>
        ))}
      </pre>
      <div>
        {lines.length > shownLines ? (
          <Button variant="ghost" aria-expanded={full} onPress={() => setFull(!full)}>
            Full stack
          </Button>
        ) : null}
        <Button
          variant="ghost"
          aria-label="Copy exception"
          onPress={() => void navigator.clipboard?.writeText([type ? `${type}: ${message}` : message, ...lines].join('\n'))}
        >
          Copy
        </Button>
      </div>
    </div>
  )
}

/** The stack's lines, frames indented under their exception, so an inner exception reads as a heading, not a frame. */
function stackLines(stack: string | undefined) {
  return (stack ?? '')
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => (line.startsWith('at ') ? `   ${line}` : line))
}
