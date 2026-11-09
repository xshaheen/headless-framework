/**
 * What a piece of a .NET stack trace line is. `plain` is anything the tokenizer does not recognise, which renders as
 * it is, so an unusual line loses its colour, never its text.
 */
export type StackTokenKind =
  | 'plain'
  | 'keyword'
  | 'type'
  | 'method'
  | 'file'
  | 'line'
  | 'heading'
  | 'message'
  | 'separator'

export interface StackToken {
  kind: StackTokenKind
  text: string
}

/** The CSS class each kind renders with; written out whole so Tailwind finds every class in the source. */
export const stackTokenClass: Record<StackTokenKind, string | undefined> = {
  plain: undefined,
  keyword: 'text-faint',
  type: 'text-syntax-key',
  method: 'text-syntax-method',
  file: 'text-muted-foreground',
  line: 'text-syntax-number',
  heading: 'text-destructive',
  message: undefined,
  separator: 'text-faint',
}

const lineTerminator = /[\n\r\u2028\u2029]/

interface FrameParts {
  lead: string
  name: string
  args: string
  file: string
  lineNumber: string
  trail: string
}

/**
 * Splits `at Namespace.Type.Method(Args) in C:\src\File.cs:line 42` by index, in one pass, where a regex with two
 * open-ended groups would backtrack over a long line. The arguments end at the first `)` after which the line either
 * ends or reads ` in <file>:line <n>`; the location is the last `:line `, because a Windows path holds a colon of its
 * own. Returns `null` for any other line.
 */
function splitFrame(line: string): FrameParts | null {
  const start = line.length - line.trimStart().length

  if (!line.startsWith('at ', start)) {
    return null
  }

  const open = line.indexOf('(', start + 3)

  if (open <= start + 3) {
    return null
  }

  const trailStart = line.trimEnd().length
  // The arguments and the file are single-line text, so neither may run past a line terminator.
  const terminator = line.slice(open).search(lineTerminator)
  const textEnd = terminator < 0 ? line.length : open + terminator
  const marker = line.lastIndexOf(':line ')
  const digits = marker < 0 ? '' : /^\d+/.exec(line.slice(marker + 6))?.[0] ?? ''
  const located = digits !== '' && marker + 6 + digits.length >= trailStart && marker <= textEnd

  for (let close = line.indexOf(')', open + 1); close >= 0 && close < textEnd; close = line.indexOf(')', close + 1)) {
    if (located && line.startsWith(' in ', close + 1) && marker >= close + 6) {
      const numberEnd = marker + 6 + digits.length

      return {
        lead: line.slice(0, start),
        name: line.slice(start + 3, open),
        args: line.slice(open, close + 1),
        file: line.slice(close + 5, marker),
        lineNumber: digits,
        trail: line.slice(numberEnd),
      }
    }

    if (close + 1 === trailStart) {
      return { lead: line.slice(0, start), name: line.slice(start + 3, open), args: line.slice(open, close + 1), file: '', lineNumber: '', trail: line.slice(close + 1) }
    }
  }

  return null
}

// An inner exception's `---> Type: message`, or a bare `Type: message`. The type must look like one, a dotted name or
// one ending in Exception or Error, so a message line such as `Parameter name: id` stays plain.
const headingPattern =
  /^(\s*)(---> )?((?:[A-Za-z_][\w`]*(?:\.[A-Za-z_][\w`+]*)+|[A-Za-z_][\w`]*(?:Exception|Error))): (.*)$/

// `--- End of inner exception stack trace ---`, `--- End of stack trace from previous location ---`, and their kin.
const separatorPattern = /^(\s*)(--- End of .* ---)(\s*)$/

/**
 * One stack trace line as tokens whose texts join back to exactly `line`: frames split into type, method, file and
 * line number; inner exception headings and end-of-trace separators keep their own kind.
 */
export function tokenizeStackLine(line: string): StackToken[] {
  const frame = splitFrame(line)

  if (frame) {
    const { lead, name, args, file, lineNumber, trail } = frame
    const located = lineNumber !== ''
    // The method is the name after the last dot outside generic arguments, so `Run[System.String]` stays whole.
    const bracket = name.indexOf('[')
    const dot = name.lastIndexOf('.', bracket < 0 ? name.length : bracket)

    return tokens([
      ['plain', lead],
      ['keyword', 'at '],
      ['type', dot < 0 ? '' : name.slice(0, dot + 1)],
      ['method', dot < 0 ? name : name.slice(dot + 1)],
      ['plain', args],
      ['keyword', located ? ' in ' : ''],
      ['file', file],
      ['keyword', located ? ':line ' : ''],
      ['line', lineNumber],
      ['plain', trail],
    ])
  }

  const separator = separatorPattern.exec(line)

  if (separator) {
    const [, lead, text, trail] = separator

    return tokens([
      ['plain', lead!],
      ['separator', text!],
      ['plain', trail!],
    ])
  }

  const heading = headingPattern.exec(line)

  if (heading) {
    const [, lead, arrow, type, message] = heading

    return tokens([
      ['plain', lead!],
      ['separator', arrow ?? ''],
      ['heading', `${type!}: `],
      ['message', message!],
    ])
  }

  return [{ kind: 'plain', text: line }]
}

function tokens(parts: [StackTokenKind, string][]): StackToken[] {
  return parts.filter(([, text]) => text !== '').map(([kind, text]) => ({ kind, text }))
}
