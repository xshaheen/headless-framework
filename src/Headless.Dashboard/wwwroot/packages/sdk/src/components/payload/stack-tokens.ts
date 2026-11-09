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

// `at Namespace.Type.Method(Args) in C:\src\File.cs:line 42`. The location is anchored at the end, because a Windows
// path holds a colon of its own.
const framePattern = /^(\s*)(at )([^(]+)(\(.*?\))(?:( in )(.+)(:line )(\d+))?(\s*)$/

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
  const frame = framePattern.exec(line)

  if (frame) {
    const [, lead, at, name, args, inWord, file, lineWord, lineNumber, trail] = frame
    // The method is the name after the last dot outside generic arguments, so `Run[System.String]` stays whole.
    const bracket = name!.indexOf('[')
    const dot = name!.lastIndexOf('.', bracket < 0 ? name!.length : bracket)

    return tokens([
      ['plain', lead!],
      ['keyword', at!],
      ['type', dot < 0 ? '' : name!.slice(0, dot + 1)],
      ['method', dot < 0 ? name! : name!.slice(dot + 1)],
      ['plain', args!],
      ['keyword', inWord ?? ''],
      ['file', file ?? ''],
      ['keyword', lineWord ?? ''],
      ['line', lineNumber ?? ''],
      ['plain', trail!],
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
