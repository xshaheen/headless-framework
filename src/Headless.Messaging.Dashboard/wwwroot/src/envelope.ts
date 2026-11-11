/**
 * The stored envelope's value, as the text the store holds, or `undefined` when `content` is not an envelope: a JSON
 * object with exactly a headers and a value member, in any letter case, as the configured serializer writes them.
 *
 * The value is cut from the stored text rather than parsed and written again, so its large integers and its spacing
 * stay exactly as stored and Copy copies the stored value.
 */
export function envelopeBody(content: string): string | undefined {
  const members = topLevelMembers(content)

  if (!members || members.length !== 2) {
    return undefined
  }

  const names = members.map((member) => member.name.toLowerCase())
  const value = members.find((member) => member.name.toLowerCase() === 'value')

  return names.includes('headers') && value ? value.text : undefined
}

interface Member {
  name: string
  text: string
}

/** The members of a JSON object, each with its value's raw text; `undefined` when `json` is not one valid object. */
function topLevelMembers(json: string): Member[] | undefined {
  try {
    const parsed: unknown = JSON.parse(json)

    if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
      return undefined
    }
  } catch {
    return undefined
  }

  // The text is valid JSON from here on, so the scan only has to find where each top-level value starts and ends.
  const members: Member[] = []
  let at = json.indexOf('{') + 1

  while (at < json.length) {
    at = skipSpace(json, at)

    if (json[at] === '}') {
      break
    }

    const nameEnd = stringEnd(json, at)
    const name = JSON.parse(json.slice(at, nameEnd)) as string
    at = skipSpace(json, nameEnd) + 1
    const start = skipSpace(json, at)
    const end = valueEnd(json, start)
    members.push({ name, text: json.slice(start, end) })
    at = skipSpace(json, end)

    if (json[at] === ',') {
      at += 1
    }
  }

  return members
}

function skipSpace(json: string, at: number) {
  while (at < json.length && /\s/.test(json[at]!)) {
    at += 1
  }

  return at
}

/** The index after the string that starts at `at`. */
function stringEnd(json: string, at: number) {
  let index = at + 1

  while (json[index] !== '"') {
    index += json[index] === '\\' ? 2 : 1
  }

  return index + 1
}

/** The index after the value that starts at `at`: a string, an object or array (to its matching close), or a literal. */
function valueEnd(json: string, at: number) {
  const first = json[at]

  if (first === '"') {
    return stringEnd(json, at)
  }

  if (first === '{' || first === '[') {
    let depth = 0
    let index = at

    while (index < json.length) {
      const char = json[index]

      if (char === '"') {
        index = stringEnd(json, index)
        continue
      }

      if (char === '{' || char === '[') {
        depth += 1
      } else if (char === '}' || char === ']') {
        depth -= 1

        if (depth === 0) {
          return index + 1
        }
      }

      index += 1
    }

    return index
  }

  let index = at

  while (index < json.length && !/[\s,}\]]/.test(json[index]!)) {
    index += 1
  }

  return index
}
