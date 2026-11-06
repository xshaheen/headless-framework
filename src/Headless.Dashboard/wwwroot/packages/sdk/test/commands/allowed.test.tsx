import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { describe, expect, it } from 'vitest'
import { useAllowedActions } from '../../src/commands/allowed.ts'
import type { CommandDefinition } from '../../src/commands/api.ts'
import { CommandAction } from '../../src/commands/command-action.tsx'
import { useSelectionCommands } from '../../src/components/keymap.tsx'
import { DashboardProvider, ModuleScope } from '../../src/context.tsx'
import { DashboardApiError, type ApiRequest } from '../../src/data/api.ts'
import { createDashboardClient } from '../../src/data/client.tsx'
import { createFakeStream } from '../../src/testing/fake-stream.ts'

const requeue: CommandDefinition = {
  id: 'diag.requeue',
  label: 'Requeue',
  objectKind: 'run',
  permission: 'operate',
  confirmation: 'none',
  typedConfirmationAbove: 100,
  requiresReason: false,
  supportsFilter: true,
  arguments: { type: 'object', required: [], properties: {} },
  canRun: true,
}

function renderInModule(children: ReactNode, handler: (request: ApiRequest) => unknown) {
  const requests: ApiRequest[] = []

  render(
    <DashboardProvider
      client={createDashboardClient()}
      environment={{
        basePath: '/headless',
        apiBase: '/headless/api',
        session: { authenticated: true },
        permissions: { diag: { canView: true, canOperate: true } },
        stream: createFakeStream(),
        api: {
          async request<T>(request: ApiRequest) {
            requests.push(request)

            return (await handler(request)) as T
          },
        },
      }}
    >
      <ModuleScope id="diag">{children}</ModuleScope>
    </DashboardProvider>,
  )

  return requests
}

/** The allowed-now answer for `r1`: requeue refused with `reason`, or allowed when there is none. */
function allowedAnswer(reason?: string) {
  return { objects: [{ kind: 'run', id: 'r1', commands: [{ id: requeue.id, allowed: !reason, disabledReason: reason }] }] }
}

function Refusal() {
  const refusal = useAllowedActions('run', [{ id: 'r1', version: '1' }])

  return <p>{refusal(requeue.id) ?? 'Allowed'}</p>
}

function Recovery() {
  const refusal = useAllowedActions('run', [{ id: 'r1', version: '1' }])

  return <CommandAction module="diag" command={requeue} ids={['r1']} noun="run" recovery disabledReason={refusal(requeue.id)} />
}

/** The palette's view of the selection: its commands, read when the button is pressed, as the palette reads them. */
function Palette() {
  const commands = useSelectionCommands()

  return (
    <button type="button" onClick={() => commands().forEach((command) => command.run())}>
      Palette
    </button>
  )
}

describe('useAllowedActions', () => {
  it('says the check is running while the server has not answered', () => {
    renderInModule(<Refusal />, () => new Promise(() => undefined))

    expect(screen.getByText('Checking whether this can run…')).toBeTruthy()
  })

  it('says the check failed when the server could not answer', async () => {
    renderInModule(<Refusal />, () => {
      throw new DashboardApiError(500, 'Server error')
    })

    expect(await screen.findByText('Could not check whether this can run. Reload to try again.')).toBeTruthy()
  })

  it('reports the server’s reason, or nothing once the server allows the command', async () => {
    renderInModule(<Refusal />, (request) => (request.path === 'diag/commands/allowed' ? allowedAnswer('Running - cancel instead') : undefined))

    expect(await screen.findByText('Running - cancel instead')).toBeTruthy()
  })

  it('answers per row, and gives a shared reason only when every row is refused', async () => {
    function PerRow({ ids }: { ids: string[] }) {
      const allowed = useAllowedActions(
        'run',
        ids.map((id) => ({ id })),
      )
      const refused = [...allowed.refused(requeue.id)].map(([id, reason]) => `${id}: ${reason}`)

      return <p>{`${refused.join(', ')} | ${allowed(requeue.id) ?? 'some can run'}`}</p>
    }

    const reasons: Record<string, string> = { r2: 'Running', r3: 'Held' }
    renderInModule(
      <>
        <PerRow ids={['r1', 'r2', 'r3']} />
        <PerRow ids={['r2', 'r3']} />
        <PerRow ids={['r2']} />
      </>,
      (request) => ({
        objects: (request.query!.id as string[]).map((id) => ({
          kind: 'run',
          id,
          commands: [{ id: requeue.id, allowed: !reasons[id], disabledReason: reasons[id] }],
        })),
      }),
    )

    expect(await screen.findByText('r2: Running, r3: Held | some can run')).toBeTruthy()
    expect(screen.getByText('r2: Running, r3: Held | None of these can run now')).toBeTruthy()
    expect(screen.getByText('r2: Running | Running')).toBeTruthy()
  })
})

describe('R on the selection', () => {
  it('does nothing when the server refuses the recovery command, and announces the reason', async () => {
    const user = userEvent.setup()
    const requests = renderInModule(<Recovery />, () => allowedAnswer('Running - cancel instead'))

    await screen.findByText('Running - cancel instead')
    expect(screen.getByRole('status').textContent).toBe('')

    await user.keyboard('r')

    expect(screen.getByRole('status').textContent).toBe('Running - cancel instead')
    expect(requests.map((request) => request.path)).toEqual(['diag/commands/allowed'])
  })

  it('runs the recovery command once the server allows it', async () => {
    const user = userEvent.setup()
    const requests = renderInModule(<Recovery />, (request) =>
      request.path === 'diag/commands/allowed' ? allowedAnswer() : { rows: [{ id: 'r1', outcome: 'done' }] },
    )

    await waitFor(() => expect(screen.getByRole('button', { name: 'Requeue' }).getAttribute('aria-disabled')).toBeNull())
    await user.keyboard('r')

    await waitFor(() => expect(requests.map((request) => request.path)).toEqual(['diag/commands/allowed', 'diag/commands/diag.requeue']))
  })

  it('starts one run when R is pressed again, or the palette runs it, before the first run answers', async () => {
    const user = userEvent.setup()
    const requests = renderInModule(
      <>
        <Recovery />
        <Palette />
      </>,
      (request) => (request.path === 'diag/commands/allowed' ? allowedAnswer() : new Promise(() => undefined)),
    )

    await waitFor(() => expect(screen.getByRole('button', { name: 'Requeue' }).getAttribute('aria-disabled')).toBeNull())
    await user.keyboard('r')
    await user.keyboard('r')
    await user.click(screen.getByRole('button', { name: 'Palette' }))

    expect(requests.map((request) => request.path)).toEqual(['diag/commands/allowed', 'diag/commands/diag.requeue'])
  })
})
