// The React Compiler's memo caches cost the SDK more bytes than this small block saves, and the shell's assets sit
// near their size budget.
'use no memo'

import { useState } from 'react'
import { Button } from '../button.tsx'
import { stackTokenClass, tokenizeStackLine } from '../payload/stack-tokens.ts'

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

/**
 * A failure's exception: the type in the danger tone, the message, and its stack with frames and inner exceptions
 * coloured, with Full stack and Copy.
 */
export function ExceptionBlock({ type, message, stack }: ExceptionBlockProps) {
  const [full, setFull] = useState(false)
  const lines = stackLines(stack)

  return (
    <div className="hd-exception overflow-hidden rounded-lg border border-border bg-deep">
      <pre className="hd-mono m-0 px-3 py-2.5 type-code wrap-anywhere whitespace-pre-wrap">
        {type ? (
          <>
            <b className="text-destructive">{type}: </b>
            {message}
          </>
        ) : (
          <Heading message={message} />
        )}
        {(full ? lines : lines.slice(0, shownLines)).map((line, index) => (
          <span key={index}>
            {'\n'}
            <StackLine line={line} />
          </span>
        ))}
      </pre>
      <div className="flex gap-0.5 border-t border-border px-1.5 py-0.5">
        {lines.length > shownLines ? (
          <Button variant="ghost" aria-expanded={full} onPress={() => setFull(!full)}>
            Full stack
          </Button>
        ) : null}
        <Button
          variant="ghost"
          aria-label="Copy exception"
          onPress={() => void navigator.clipboard?.writeText(copyText(type, message, stack))}
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

/**
 * A message served without a type, such as a store's text that starts `TimeoutException: …`: its leading type reads
 * in the danger tone, as a served type does. Any other message reads as it is.
 */
function Heading({ message }: { message: string }) {
  const [first, ...rest] = tokenizeStackLine(message)

  return first?.kind === 'heading' && rest.every((token) => token.kind === 'message') ? (
    <>
      <b className="text-destructive">{first.text}</b>
      {rest.map((token) => token.text).join('')}
    </>
  ) : (
    message
  )
}

/** One stack line as coloured tokens; a line the tokenizer does not recognise reads faint, as a frame used to. */
function StackLine({ line }: { line: string }) {
  const tokens = tokenizeStackLine(line)

  if (tokens.length === 1 && tokens[0]!.kind === 'plain') {
    return <span className="text-faint">{line}</span>
  }

  return tokens.map((token, index) => {
    const className = stackTokenClass[token.kind]

    return className ? (
      <span key={index} className={className}>
        {token.text}
      </span>
    ) : (
      token.text
    )
  })
}

/**
 * The exception as it was stored, for the clipboard: the stack exactly as given, not the trimmed, re-indented lines
 * the block shows.
 */
function copyText(type: string | undefined, message: string, stack: string | undefined) {
  const head = type ? `${type}: ${message}` : message

  return stack ? `${head}\n${stack}` : head
}
