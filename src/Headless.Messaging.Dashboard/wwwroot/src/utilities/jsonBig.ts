import JSONBig from 'json-bigint'

// storeAsString keeps int64 message ids / large numbers exact (as strings) instead of
// letting JS coerce them to a lossy IEEE-754 double once they exceed Number.MAX_SAFE_INTEGER.
const parser = JSONBig({ storeAsString: true })

export interface ParsedJson {
  parsed: unknown
  isJson: boolean
}

/**
 * Parse a raw string as JSON without losing large-integer precision.
 * Returns { isJson: false } instead of throwing when the input is not valid JSON.
 */
export function parseJsonSafe(raw: string): ParsedJson {
  try {
    return { parsed: parser.parse(raw), isJson: true }
  } catch {
    return { parsed: null, isJson: false }
  }
}
