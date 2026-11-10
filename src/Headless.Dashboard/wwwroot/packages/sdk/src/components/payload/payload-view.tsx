// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { SugarHigh, tokenize } from 'sugar-high/core'
import * as jsonSyntax from 'sugar-high/lang/json'
import { useMemo } from 'react'
import { useFormat } from '../../locale/locale.tsx'
import { Button } from '../button.tsx'
import { Text } from '../display/text.tsx'

/** The most of a payload the view renders, in UTF-8 bytes; the rest is one `Copy all` away. */
export const payloadDisplayLimit = 64 * 1024

/**
 * The longest start of `bytes` that fits in `limit` bytes and ends on a whole character: a UTF-8 continuation byte
 * (`10xxxxxx`) at the cut means the character began before it, so the cut moves back to that character's first byte.
 */
function cutUtf8(bytes: Uint8Array, limit: number) {
  let end = limit

  while (end > 0 && (bytes[end]! & 0b1100_0000) === 0b1000_0000) {
    end--
  }

  return new TextDecoder().decode(bytes.subarray(0, end))
}

/**
 * Indents JSON text without parsing its values, so a large integer such as `9007199254740993` keeps every digit that
 * `JSON.parse` would round away. Returns `null` when `text` is not valid JSON.
 */
export function formatJson(text: string): string | null {
  try {
    JSON.parse(text)
  } catch {
    return null
  }

  let out = ''
  let depth = 0
  let inString = false
  let escaped = false

  const newline = () => `\n${'  '.repeat(depth)}`

  for (let at = 0; at < text.length; at++) {
    const char = text[at]!

    if (inString) {
      out += char

      if (escaped) {
        escaped = false
      } else if (char === '\\') {
        escaped = true
      } else if (char === '"') {
        inString = false
      }

      continue
    }

    if (/\s/.test(char)) {
      continue
    }

    switch (char) {
      case '"':
        inString = true
        out += char
        break
      case '{':
      case '[': {
        const close = char === '{' ? '}' : ']'
        let next = at + 1

        while (next < text.length && /\s/.test(text[next]!)) {
          next++
        }

        if (text[next] === close) {
          out += char + close
          at = next
        } else {
          depth++
          out += char + newline()
        }
        break
      }
      case '}':
      case ']':
        depth--
        out += newline() + char
        break
      case ',':
        out += char + newline()
        break
      case ':':
        out += ': '
        break
      default:
        out += char
    }
  }

  return out
}

/** The class each JSON token renders with, by sugar-high's token type; written out whole so Tailwind finds each one. */
const jsonTokenClass: Partial<Record<string, string>> = {
  property: 'text-syntax-key',
  string: 'text-syntax-string',
  // sugar-high's JSON preset reads a number as a `class` token and `true`, `false`, `null` as keywords.
  class: 'text-syntax-number',
  keyword: 'text-syntax-literal',
  sign: 'text-faint',
}

/**
 * Formatted JSON as coloured spans, from sugar-high's tokens and never its HTML, whose `style` attributes the
 * dashboard's CSP refuses. React writes every token as text. A tokenization that does not join back to exactly
 * `text` renders `text` plain, so colouring can never change what the operator reads.
 */
function jsonRuns(text: string): readonly (readonly [string | undefined, string])[] | null {
  const tokens = tokenize(text, jsonSyntax).map(([type, value]) => [SugarHigh.TokenTypes[type], value] as const)

  if (tokens.map(([, value]) => value).join('') !== text) {
    return null
  }

  // Adjacent tokens of one colour render as one span, so a long payload costs a span per coloured run, not per token.
  const runs: [string | undefined, string][] = []

  tokens.forEach(([type, value], index) => {
    const className =
      type === 'sign' && value === '-' && tokens[index + 1]?.[0] === 'class'
        ? jsonTokenClass.class
        : jsonTokenClass[type ?? '']
    const last = runs.at(-1)

    if (last && last[0] === className) {
      last[1] += value
    } else {
      runs.push([className, value])
    }
  })

  return runs
}

function JsonTokens({ text }: { text: string }) {
  const runs = useMemo(() => jsonRuns(text), [text])

  if (runs === null) {
    return text
  }

  return runs.map(([className, value], index) =>
    className ? (
      <span key={index} className={className}>
        {value}
      </span>
    ) : (
      value
    ),
  )
}

export interface PayloadViewProps {
  /** The payload as the server's formatter returned it: already redacted. */
  content: string
  contentType?: string
  /** A stored contract version the host no longer reads, shown instead of a failed parse. */
  unsupportedVersion?: string | number
}

/**
 * A payload, shown only on request: formatted JSON with exact large integers and syntax colours, mono, with copy.
 * Never rendered as HTML: React writes it as text. Copy copies `content` as the server sent it, not the formatted
 * text.
 */
export function PayloadView({ content, contentType, unsupportedVersion }: PayloadViewProps) {
  const format = useFormat()
  const isJsonType = contentType === undefined || contentType.includes('json')
  const { bytes, truncated, formatted, shown } = useMemo(() => {
    const encoded = new TextEncoder().encode(content)
    const truncated = encoded.length > payloadDisplayLimit
    const formatted = !truncated && isJsonType ? formatJson(content) : null

    return {
      bytes: encoded.length,
      truncated,
      formatted,
      shown: truncated ? cutUtf8(encoded, payloadDisplayLimit) : (formatted ?? content),
    }
  }, [content, isJsonType])

  if (unsupportedVersion !== undefined) {
    return <p className="text-muted-foreground">{`Unsupported contract version ${unsupportedVersion}`}</p>
  }

  return (
    <figure className="rounded-md bg-deep">
      <figcaption className="flex items-center justify-between gap-2 ps-3 pe-1 pt-1">
        <Text tone="hint">{truncated ? `${format.size(bytes)}, first 64 KB shown` : format.size(bytes)}</Text>
        <Button variant="ghost" onPress={() => void navigator.clipboard?.writeText(content)}>
          {truncated ? 'Copy all' : 'Copy'}
        </Button>
      </figcaption>
      <pre className="hd-mono max-h-(--payload-max) overflow-auto px-3 pt-2 pb-3 whitespace-pre" dir="ltr">
        {formatted === null ? shown : <JsonTokens text={formatted} />}
      </pre>
    </figure>
  )
}
