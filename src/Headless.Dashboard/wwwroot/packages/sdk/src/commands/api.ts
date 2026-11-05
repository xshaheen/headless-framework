import type { DashboardApi } from '../data/api.ts'

/** How a command is confirmed: not at all, in a dialog, or by typing the row count. */
export type Confirmation = 'none' | 'confirm' | 'typed'

export interface ArgumentProperty {
  type: 'string' | 'integer' | 'number' | 'boolean'
  /** The form label. */
  title: string
  enum?: readonly (string | number | boolean)[]
  minimum?: number
  maximum?: number
  maxLength?: number
}

/** A command's arguments: the JSON-Schema subset the server validates and the shell renders as a form. */
export interface ArgumentSchema {
  type: 'object'
  required: readonly string[]
  properties: Readonly<Record<string, ArgumentProperty>>
}

/** A server command, as `GET {module}/commands` describes it for the signed-in user. */
export interface CommandDefinition {
  id: string
  label: string
  objectKind: string
  permission: 'view' | 'operate'
  capability?: string
  /** The declared confirmation; past `typedConfirmationAbove` rows a confirmed command is typed instead. */
  confirmation: Confirmation
  typedConfirmationAbove: number
  requiresReason: boolean
  /** The command that undoes this one: it runs without confirmation, and the toast offers Undo. */
  inverseCommandId?: string
  /** Whether the command can run over a filter as an Operation. */
  supportsFilter: boolean
  arguments: ArgumentSchema
  canRun: boolean
  /** Why the user may not run it, such as `Needs operate permission`. */
  disabledReason?: string
}

export type RowOutcome = 'done' | 'skipped' | 'failed'

export interface RowResult {
  id: string
  outcome: RowOutcome
  reason?: string
}

export interface ObjectSummary {
  target: { module: string; kind: string; id: string }
  title: string
  status?: string
  traceId?: string
}

export interface CommandAvailability {
  id: string
  allowed: boolean
  disabledReason?: string
}

export interface ObjectAvailability {
  kind: string
  id: string
  commands: CommandAvailability[]
}

/** An Operation's frozen scope: its exact count, a sample, the confirmation the count needs, and its signed token. */
export interface OperationScope {
  operationId: string
  count: number
  confirmation: Confirmation
  sample: ObjectSummary[]
  token: string
  expiresAt: string
  chunkSize: number
}

export interface OperationChunk {
  operationId: string
  rows: RowResult[]
  /** The cursor the next chunk sends; absent once the scope is done. */
  nextCursor?: string | null
  done: boolean
}

export interface CommandCall {
  ids: readonly string[]
  arguments?: Readonly<Record<string, unknown>>
  reason?: string
  confirmed?: boolean
  typedCount?: number
}

export interface OperationConfirmCall {
  filter: unknown
  arguments?: Readonly<Record<string, unknown>>
  reason?: string
}

export interface OperationChunkCall {
  token: string
  cursor?: string
  confirmed: boolean
  typedCount?: number
}

/** The shell's command endpoints of one module, under `{apiBase}/{module}/commands`. */
export interface CommandsApi {
  module: string
  list(): Promise<CommandDefinition[]>
  allowed(kind: string, ids: readonly string[]): Promise<ObjectAvailability[]>
  run(commandId: string, call: CommandCall): Promise<RowResult[]>
  confirm(commandId: string, call: OperationConfirmCall): Promise<OperationScope>
  chunk(commandId: string, call: OperationChunkCall): Promise<OperationChunk>
}

/** The command endpoints of `module`. */
export function createCommandsApi(api: DashboardApi, module: string): CommandsApi {
  const base = `${encodeURIComponent(module)}/commands`
  const at = (commandId: string, suffix = '') => `${base}/${encodeURIComponent(commandId)}${suffix}`

  return {
    module,
    async list() {
      return (await api.request<{ commands: CommandDefinition[] }>({ method: 'GET', path: base })).commands
    },
    async allowed(kind, ids) {
      const answer = await api.request<{ objects: ObjectAvailability[] }>({
        method: 'GET',
        path: `${base}/allowed`,
        query: { kind, id: ids },
      })

      return answer.objects
    },
    async run(commandId, call) {
      return (await api.request<{ rows: RowResult[] }>({ method: 'POST', path: at(commandId), body: call })).rows
    },
    confirm: (commandId, call) => api.request<OperationScope>({ method: 'POST', path: at(commandId, '/confirm'), body: call }),
    chunk: (commandId, call) => api.request<OperationChunk>({ method: 'POST', path: at(commandId, '/chunks'), body: call }),
  }
}

/** The confirmation a call over `count` rows needs: a confirmed command past the threshold is typed. */
export function effectiveConfirmation(command: Pick<CommandDefinition, 'confirmation' | 'typedConfirmationAbove'>, count: number): Confirmation {
  return command.confirmation !== 'none' && count > command.typedConfirmationAbove ? 'typed' : command.confirmation
}
