import { useFormat } from '../../locale/locale.tsx'
import { Button } from '../button.tsx'

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

export interface PayloadViewProps {
  /** The payload as the server's formatter returned it: already redacted. */
  content: string
  contentType?: string
  /** A stored contract version the host no longer reads, shown instead of a failed parse. */
  unsupportedVersion?: string | number
}

/**
 * A payload, shown only on request: formatted JSON with exact large integers, mono, with copy. Never rendered as
 * HTML: React writes it as text.
 */
export function PayloadView({ content, contentType, unsupportedVersion }: PayloadViewProps) {
  const format = useFormat()

  if (unsupportedVersion !== undefined) {
    return <p className="hd-payload-note">{`Unsupported contract version ${unsupportedVersion}`}</p>
  }

  const encoded = new TextEncoder().encode(content)
  const bytes = encoded.length
  const truncated = bytes > payloadDisplayLimit
  const isJson = !truncated && (contentType === undefined || contentType.includes('json'))
  const shown = truncated ? cutUtf8(encoded, payloadDisplayLimit) : isJson ? (formatJson(content) ?? content) : content

  return (
    <figure className="hd-payload">
      <figcaption className="hd-payload-bar">
        <span className="hd-hint">{truncated ? `${format.size(bytes)}, first 64 KB shown` : format.size(bytes)}</span>
        <Button variant="ghost" onPress={() => void navigator.clipboard?.writeText(content)}>
          {truncated ? 'Copy all' : 'Copy'}
        </Button>
      </figcaption>
      <pre className="hd-mono" dir="ltr">
        {shown}
      </pre>
    </figure>
  )
}
