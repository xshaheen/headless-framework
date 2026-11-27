/** The server answered a shape a module does not read, so the view shows the read as failed. */
export function shapeError(what: string): Error {
  return new Error(`The server answered an unexpected ${what}.`)
}

/** Whether `value` is a plain JSON object (non-null and not an array). */
export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function readRecord(value: unknown, what: string): Record<string, unknown> {
  if (!isRecord(value)) {
    throw shapeError(what)
  }

  return value
}

export function readString(value: unknown, what: string): string {
  if (typeof value !== 'string') {
    throw shapeError(what)
  }

  return value
}

/** A string field the server writes as `null` when absent; `undefined` reads as `null` too. */
export function readOptionalString(value: unknown, what: string): string | null {
  return value == null ? null : readString(value, what)
}

export function readNumber(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw shapeError(what)
  }

  return value
}

export function readBoolean(value: unknown, what: string): boolean {
  if (typeof value !== 'boolean') {
    throw shapeError(what)
  }

  return value
}

export function readList<T>(value: unknown, what: string, read: (item: unknown) => T): T[] {
  if (!Array.isArray(value)) {
    throw shapeError(what)
  }

  return value.map(read)
}

export function readOneOf<const T extends readonly string[]>(values: T, value: unknown, what: string): T[number] {
  const found = values.find((candidate) => candidate === value)

  if (found === undefined) {
    throw shapeError(what)
  }

  return found
}
