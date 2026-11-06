import { parseAst } from 'vite'

/** A build error in a module's definition, naming the field at fault. */
export class ModuleDefinitionError extends Error {
  override name = 'ModuleDefinitionError'
}

/**
 * The products a module may belong to, in the order the shell shows their tabs. The dashboard fixes the order, not
 * the host's registration, so a product's tab sits in the same place on every host.
 */
export const dashboardProducts = ['jobs', 'messaging', 'cluster', 'platform', 'dev-inbox', 'diagnostics'] as const

export type DashboardProduct = (typeof dashboardProducts)[number]

/** The colour roles a sidebar entry's state dot may paint. */
export const stateRoles = ['sched', 'enq', 'proc', 'ok', 'fail', 'off'] as const

export type StateRole = (typeof stateRoles)[number]

export interface DefinitionSidebarEntry {
  id: string
  label: string
  route: string
  icon?: string
  state?: StateRole
  countKey?: string
  requires?: Permission
}

export interface DefinitionSidebarSection {
  label?: string
  entries: DefinitionSidebarEntry[]
}

export interface DefinitionRoute {
  path: string
  /** The specifier the route's `lazy` imports, as written in the source. */
  import: string
}

export interface DefinitionCommand {
  id: string
  label: string
  requires?: Permission
}

/** Everything static a `defineModule` call declares, read from its source without running it. */
export interface ModuleDefinitionSource {
  id: string
  product: DashboardProduct
  sidebar: DefinitionSidebarSection[]
  routes: DefinitionRoute[]
  /** Each object kind's literal fields; function fields such as `route` stay in code. */
  objectKinds: Record<string, Record<string, JsonValue>>
  commands: DefinitionCommand[]
  events: string[]
}

export type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue }

type Permission = 'view' | 'operate'

interface Node {
  type: string
  [key: string]: unknown
}

const definitionFields = new Set(['id', 'product', 'sidebar', 'routes', 'objectKinds', 'commands', 'events'])

/**
 * Reads the object literal passed to `export default defineModule(...)` in a module's entry source.
 *
 * @throws {ModuleDefinitionError} The source has no such call, or a static field is computed rather than written as
 * a literal.
 */
export function readModuleDefinition(source: string, fileName: string): ModuleDefinitionSource {
  const lang = /\.[cm]?tsx$/.test(fileName) || /\.jsx$/.test(fileName) ? 'tsx' : 'ts'
  const program = parseAst(source, { lang }, fileName) as unknown as { body: Node[] }
  const exported = program.body.find((statement) => statement.type === 'ExportDefaultDeclaration')
  const call = exported ? unwrap(exported.declaration as Node) : undefined

  if (
    call?.type !== 'CallExpression' ||
    (call.callee as Node).type !== 'Identifier' ||
    (call.callee as Node).name !== 'defineModule'
  ) {
    throw new ModuleDefinitionError(`${fileName} must export default a defineModule({ ... }) call.`)
  }

  const args = call.arguments as Node[]
  const definition = args.length === 1 ? unwrap(args[0]!) : undefined

  if (definition?.type !== 'ObjectExpression') {
    throw new ModuleDefinitionError(`${fileName}: defineModule takes one object literal.`)
  }

  const fields = properties(definition, 'defineModule({ ... })', fileName)

  for (const name of fields.keys()) {
    if (!definitionFields.has(name)) {
      throw new ModuleDefinitionError(`${fileName}: defineModule has no field '${name}'.`)
    }
  }

  const idNode = fields.get('id')

  if (!idNode) {
    throw new ModuleDefinitionError(`${fileName}: defineModule needs an id.`)
  }

  const productNode = fields.get('product')

  if (!productNode) {
    throw new ModuleDefinitionError(`${fileName}: defineModule needs a product, one of ${quoted(dashboardProducts)}.`)
  }

  return {
    id: stringField(idNode, 'id', fileName),
    product: oneOf(productNode, 'product', dashboardProducts, fileName),
    sidebar: arrayField(fields.get('sidebar'), 'sidebar', fileName, (section, at) => {
      const props = objectProperties(section, at, fileName, ['label', 'entries'])
      const label = props.get('label')
      const entries = props.get('entries')

      if (!entries) {
        throw new ModuleDefinitionError(`${fileName}: ${at}.entries is required.`)
      }

      return {
        ...(label ? { label: stringField(label, `${at}.label`, fileName) } : {}),
        entries: arrayField(entries, `${at}.entries`, fileName, (entry, entryAt) =>
          sidebarEntry(entry, entryAt, fileName),
        ),
      }
    }),
    routes: arrayField(fields.get('routes'), 'routes', fileName, (entry, at) => {
      const props = objectProperties(entry, at, fileName, ['path', 'lazy'])
      const lazy = props.get('lazy')

      if (!lazy) {
        throw new ModuleDefinitionError(`${fileName}: ${at}.lazy is required.`)
      }

      return { path: requiredString(props, 'path', at, fileName), import: lazyImport(lazy, `${at}.lazy`, fileName) }
    }),
    objectKinds: objectKindsField(fields.get('objectKinds'), fileName),
    commands: arrayField(fields.get('commands'), 'commands', fileName, (entry, at) => {
      const props = objectProperties(entry, at, fileName, ['id', 'label', 'requires', 'run'])

      if (!props.get('run')) {
        throw new ModuleDefinitionError(`${fileName}: ${at}.run is required.`)
      }

      return {
        id: requiredString(props, 'id', at, fileName),
        label: requiredString(props, 'label', at, fileName),
        ...optionalPermission(props, at, fileName),
      }
    }),
    events: eventNames(fields.get('events'), fileName),
  }
}

/** Strips the TypeScript and grouping wrappers that do not change a value: `as const`, `satisfies`, parentheses. */
function unwrap(node: Node): Node {
  let current = node

  while (
    current.type === 'TSAsExpression' ||
    current.type === 'TSSatisfiesExpression' ||
    current.type === 'TSNonNullExpression' ||
    current.type === 'ParenthesizedExpression'
  ) {
    current = current.expression as Node
  }

  return current
}

function properties(node: Node, at: string, fileName: string): Map<string, Node> {
  const result = new Map<string, Node>()

  for (const property of node.properties as Node[]) {
    if (property.type !== 'Property' || property.computed || property.kind !== 'init') {
      throw new ModuleDefinitionError(
        `${fileName}: ${at} must list its fields as literals; a spread, computed key, or accessor cannot be read at build time.`,
      )
    }

    const key = property.key as Node
    const name = key.type === 'Identifier' ? (key.name as string) : key.type === 'Literal' ? String(key.value) : null

    if (name === null) {
      throw new ModuleDefinitionError(`${fileName}: ${at} has a key that is not a literal.`)
    }

    if (result.has(name)) {
      throw new ModuleDefinitionError(`${fileName}: ${at} declares '${name}' twice.`)
    }

    // A shorthand { label } reads a variable, so its value is not a literal; the value node says so below.
    result.set(name, unwrap(property.value as Node))
  }

  return result
}

function objectProperties(node: Node, at: string, fileName: string, allowed: string[]) {
  if (node.type !== 'ObjectExpression') {
    throw new ModuleDefinitionError(`${fileName}: ${at} must be an object literal.`)
  }

  const props = properties(node, at, fileName)

  for (const name of props.keys()) {
    if (!allowed.includes(name)) {
      throw new ModuleDefinitionError(`${fileName}: ${at} has no field '${name}'.`)
    }
  }

  return props
}

function arrayField<T>(
  node: Node | undefined,
  at: string,
  fileName: string,
  read: (element: Node, at: string) => T,
): T[] {
  if (!node) {
    return []
  }

  if (node.type !== 'ArrayExpression') {
    throw new ModuleDefinitionError(`${fileName}: ${at} must be an array literal.`)
  }

  return (node.elements as (Node | null)[]).map((element, index) => {
    if (!element || element.type === 'SpreadElement') {
      throw new ModuleDefinitionError(`${fileName}: ${at}[${index}] must be written out, not spread or left empty.`)
    }

    return read(unwrap(element), `${at}[${index}]`)
  })
}

function requiredString(props: Map<string, Node>, name: string, at: string, fileName: string) {
  const node = props.get(name)

  if (!node) {
    throw new ModuleDefinitionError(`${fileName}: ${at}.${name} is required.`)
  }

  return stringField(node, `${at}.${name}`, fileName)
}

function optionalPermission(props: Map<string, Node>, at: string, fileName: string): { requires?: Permission } {
  const node = props.get('requires')

  if (!node) {
    return {}
  }

  const value = stringField(node, `${at}.requires`, fileName)

  if (value !== 'view' && value !== 'operate') {
    throw new ModuleDefinitionError(`${fileName}: ${at}.requires must be 'view' or 'operate', not '${value}'.`)
  }

  return { requires: value }
}

function quoted(values: readonly string[]) {
  return values.map((value) => `'${value}'`).join(', ')
}

function oneOf<const T extends readonly string[]>(node: Node, at: string, values: T, fileName: string): T[number] {
  const value = stringField(node, at, fileName)

  if (!isOneOf(values, value)) {
    throw new ModuleDefinitionError(`${fileName}: ${at} must be one of ${quoted(values)}, not '${value}'.`)
  }

  return value
}

function isOneOf<const T extends readonly string[]>(values: T, value: string): value is T[number] {
  return values.includes(value)
}

function sidebarEntry(entry: Node, at: string, fileName: string): DefinitionSidebarEntry {
  const props = objectProperties(entry, at, fileName, ['id', 'label', 'route', 'icon', 'state', 'countKey', 'requires'])
  const icon = props.get('icon')
  const state = props.get('state')
  const countKey = props.get('countKey')

  // The entry's leading mark is one slot: an icon for a view, a dot for one state's list.
  if (!icon === !state) {
    throw new ModuleDefinitionError(`${fileName}: ${at} needs exactly one of icon or state.`)
  }

  return {
    id: requiredString(props, 'id', at, fileName),
    label: requiredString(props, 'label', at, fileName),
    route: requiredString(props, 'route', at, fileName),
    ...(icon ? { icon: stringField(icon, `${at}.icon`, fileName) } : {}),
    ...(state ? { state: oneOf(state, `${at}.state`, stateRoles, fileName) } : {}),
    ...(countKey ? { countKey: stringField(countKey, `${at}.countKey`, fileName) } : {}),
    ...optionalPermission(props, at, fileName),
  }
}

function stringField(node: Node, at: string, fileName: string): string {
  const value = literal(node, at, fileName)

  if (typeof value !== 'string') {
    throw new ModuleDefinitionError(`${fileName}: ${at} must be a string literal.`)
  }

  return value
}

/** Evaluates a JSON-shaped literal, refusing anything whose value is only known at run time. */
function literal(node: Node, at: string, fileName: string): JsonValue {
  const value = unwrap(node)

  switch (value.type) {
    case 'Literal': {
      const raw = value.value

      if (raw === null || typeof raw === 'string' || typeof raw === 'number' || typeof raw === 'boolean') {
        return raw
      }

      break
    }
    case 'TemplateLiteral': {
      if ((value.expressions as Node[]).length === 0) {
        return ((value.quasis as Node[])[0]!.value as { cooked: string }).cooked
      }

      break
    }
    case 'ArrayExpression':
      return arrayField(value, at, fileName, (element, elementAt) => literal(element, elementAt, fileName))
    case 'ObjectExpression': {
      const result: Record<string, JsonValue> = {}

      for (const [name, property] of properties(value, at, fileName)) {
        result[name] = literal(property, `${at}.${name}`, fileName)
      }

      return result
    }
    case 'UnaryExpression': {
      const argument = unwrap(value.argument as Node)

      if (value.operator === '-' && argument.type === 'Literal' && typeof argument.value === 'number') {
        return -argument.value
      }

      break
    }
  }

  throw new ModuleDefinitionError(
    `${fileName}: ${at} must be a literal; the build reads it without running the module, so it cannot be computed.`,
  )
}

function isFunction(node: Node) {
  return node.type === 'ArrowFunctionExpression' || node.type === 'FunctionExpression'
}

/** Reads the specifier of `() => import('./view')`, the only shape whose chunk the build can name. */
function lazyImport(node: Node, at: string, fileName: string): string {
  if (isFunction(node)) {
    let body = unwrap(node.body as Node)

    if (body.type === 'BlockStatement') {
      const statements = body.body as Node[]
      const only = statements.length === 1 ? statements[0] : undefined

      body = only?.type === 'ReturnStatement' && only.argument ? unwrap(only.argument as Node) : body
    }

    if (body.type === 'ImportExpression') {
      const source = unwrap(body.source as Node)
      const specifier = source.type === 'Literal' || source.type === 'TemplateLiteral' ? safeLiteral(source) : null

      if (typeof specifier === 'string') {
        return specifier
      }
    }
  }

  throw new ModuleDefinitionError(
    `${fileName}: ${at} must be written as () => import('./path'), so the build can name the route's chunk.`,
  )
}

function safeLiteral(node: Node): JsonValue | undefined {
  try {
    return literal(node, '', '')
  } catch {
    return undefined
  }
}

function objectKindsField(node: Node | undefined, fileName: string) {
  const result: Record<string, Record<string, JsonValue>> = {}

  if (!node) {
    return result
  }

  if (node.type !== 'ObjectExpression') {
    throw new ModuleDefinitionError(`${fileName}: objectKinds must be an object literal.`)
  }

  for (const [kind, value] of properties(node, 'objectKinds', fileName)) {
    const at = `objectKinds.${kind}`

    if (value.type !== 'ObjectExpression') {
      throw new ModuleDefinitionError(`${fileName}: ${at} must be an object literal.`)
    }

    const fields: Record<string, JsonValue> = {}

    for (const [name, field] of properties(value, at, fileName)) {
      // Functions such as route(id) run in the browser once the module loads; only literal fields are static.
      if (!isFunction(field)) {
        fields[name] = literal(field, `${at}.${name}`, fileName)
      }
    }

    result[kind] = fields
  }

  return result
}

function eventNames(node: Node | undefined, fileName: string): string[] {
  if (!node) {
    return []
  }

  if (node.type !== 'ObjectExpression') {
    throw new ModuleDefinitionError(`${fileName}: events must be an object literal.`)
  }

  // Only the names are static; each handler is code that runs once the module loads.
  return [...properties(node, 'events', fileName).keys()]
}
