// The React Compiler's memo caches cost the SDK more bytes than this small block saves, and the shell's assets sit
// near their size budget.
'use no memo'

import { useState } from 'react'
import { Button } from '../button.tsx'

export interface ExceptionBlockProps {
  /** The exception's type, such as `System.TimeoutException`; left out when the server does not serve it. */
  type?: string
  message: string
  /** The stack frames, one per entry, without the leading `at`. */
  frames?: readonly string[]
}

/** The frames shown before `Full stack`: enough to see where it threw without pushing the fields out of view. */
const shownFrames = 3

/** A failure's exception: the type in the danger tone, the message, and its frames, with Full stack and Copy. */
export function ExceptionBlock({ type, message, frames = [] }: ExceptionBlockProps) {
  const [full, setFull] = useState(false)
  const lines = frames.map((frame) => `   at ${frame}`)

  return (
    <div className="hd-exception">
      <pre className="hd-mono">
        {type ? <b>{type}: </b> : null}
        {message}
        {(full ? lines : lines.slice(0, shownFrames)).map((line, index) => (
          <span key={index}>{`\n${line}`}</span>
        ))}
      </pre>
      <div>
        {lines.length > shownFrames ? (
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
