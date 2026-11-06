import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { ActionConfirm } from '../../src/commands/action-confirm.tsx'
import type { CommandDefinition } from '../../src/commands/api.ts'
import { CommandAction } from '../../src/commands/command-action.tsx'
import { OperationHost, useOperations } from '../../src/commands/operations.tsx'
import { Button } from '../../src/components/button.tsx'
import { ToastRegion } from '../../src/components/live.tsx'
import { DashboardProvider } from '../../src/context.tsx'
import { DashboardApiError, type ApiRequest } from '../../src/data/api.ts'
import { createDashboardClient } from '../../src/data/client.tsx'
import { createFakeStream } from '../../src/testing/fake-stream.ts'

function command(overrides: Partial<CommandDefinition>): CommandDefinition {
  return {
    id: 'requeue',
    label: 'Requeue',
    objectKind: 'run',
    permission: 'operate',
    confirmation: 'confirm',
    typedConfirmationAbove: 100,
    requiresReason: false,
    supportsFilter: true,
    arguments: { type: 'object', required: [], properties: {} },
    canRun: true,
    ...overrides,
  }
}

function renderWithDashboard(children: ReactNode, handler: (request: ApiRequest) => unknown) {
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
      <OperationHost chunkDelayMs={0}>{children}</OperationHost>
      <ToastRegion />
    </DashboardProvider>,
  )

  return requests
}

describe('commands', () => {
  it('runs a command with an inverse without a confirm dialog, and Undo calls the inverse with a stated reason', async () => {
    const user = userEvent.setup()
    const fail = command({ id: 'fail', label: 'Fail', confirmation: 'none', inverseCommandId: 'requeue' })
    const requeue = command({ id: 'requeue', label: 'Requeue', requiresReason: true })
    const requests = renderWithDashboard(
      <CommandAction module="diag" command={fail} commands={[fail, requeue]} ids={['r1', 'r2']} noun="runs" />,
      (request) => ({ rows: (request.body as { ids: string[] }).ids.map((id) => ({ id, outcome: 'done' })) }),
    )

    await user.click(screen.getByRole('button', { name: 'Fail 2 runs' }))

    expect(screen.queryByRole('dialog')).toBeNull()
    const toast = await screen.findByText('Fail: 2 done')
    await user.click(within(toast.closest('[role="status"]') as HTMLElement).getByRole('button', { name: 'Undo' }))

    expect(await screen.findByText('Requeue: 2 done')).toBeTruthy()
    expect(requests.map((request) => request.path)).toEqual(['diag/commands/fail', 'diag/commands/requeue'])
    expect(requests[1]!.body).toMatchObject({ ids: ['r1', 'r2'], reason: 'Undo of Fail', confirmed: true })
  })

  it('reports a failed Undo in a toast, which outlives the button, and beside the action while the rows stay selected', async () => {
    const user = userEvent.setup()
    const fail = command({ id: 'fail', label: 'Fail', confirmation: 'none', inverseCommandId: 'requeue' })
    const requeue = command({ id: 'requeue', label: 'Requeue' })
    renderWithDashboard(
      <CommandAction module="diag" command={fail} commands={[fail, requeue]} ids={['r1', 'r2']} noun="runs" />,
      (request) => {
        if (request.path === 'diag/commands/requeue') {
          throw new DashboardApiError(409, 'Run r1 is running again.')
        }

        return { rows: (request.body as { ids: string[] }).ids.map((id) => ({ id, outcome: 'done' })) }
      },
    )

    await user.click(screen.getByRole('button', { name: 'Fail 2 runs' }))
    const toast = await screen.findByText('Fail: 2 done')
    await user.click(within(toast.closest('[role="status"]') as HTMLElement).getByRole('button', { name: 'Undo' }))

    const failures = await screen.findAllByText('Run r1 is running again.')
    expect(failures.some((failure) => failure.closest('[role="status"]'))).toBe(true)
    expect((await screen.findByRole('alert')).textContent).toContain('Run r1 is running again.')
  })

  it('lists each row the server did not change beside the action, with its reason', async () => {
    const user = userEvent.setup()
    const fail = command({ id: 'fail', label: 'Fail', confirmation: 'none' })
    renderWithDashboard(<CommandAction module="diag" command={fail} ids={['r1', 'r2', 'r3']} noun="runs" />, () => ({
      rows: [
        { id: 'r1', outcome: 'done' },
        { id: 'r2', outcome: 'skipped', reason: 'Already failed' },
        { id: 'r3', outcome: 'failed', reason: 'Locked by node-b' },
      ],
    }))

    await user.click(screen.getByRole('button', { name: 'Fail 3 runs' }))

    const list = await screen.findByRole('list', { name: 'Rows not changed' })
    const items = within(list).getAllByRole('listitem').map((item) => item.textContent)
    expect(items).toEqual(['r2RefusedAlready failed', 'r3FailedLocked by node-b'])
    expect(screen.getByRole('alert').contains(list)).toBe(true)
  })

  it('keeps Confirm disabled until the typed count matches', async () => {
    const user = userEvent.setup()
    const onConfirm = vi.fn()
    render(
      <ActionConfirm
        isOpen
        onCancel={vi.fn()}
        onConfirm={onConfirm}
        title="Delete 312 runs"
        confirmLabel="Delete 312 runs"
        cancelLabel="Keep runs"
        confirmation="typed"
        count={312}
      />,
    )
    const confirm = screen.getByRole('button', { name: 'Delete 312 runs' })
    const typed = screen.getByRole('textbox', { name: /Type 312 to confirm/ })

    await user.type(typed, '31')
    expect(confirm.hasAttribute('disabled')).toBe(true)

    await user.type(typed, '2')
    expect(confirm.hasAttribute('disabled')).toBe(false)

    await user.click(confirm)
    expect(onConfirm).toHaveBeenCalledWith({ arguments: {}, reason: undefined, typedCount: 312 })
  })

  it('confirms a selection past 100 rows by typing the count', async () => {
    const user = userEvent.setup()
    const ids = Array.from({ length: 150 }, (_, at) => `r${at}`)
    const requests = renderWithDashboard(
      <CommandAction module="diag" command={command({})} ids={ids} noun="runs" />,
      () => ({ rows: [] }),
    )

    await user.click(screen.getByRole('button', { name: 'Requeue 150 runs' }))
    const dialog = screen.getByRole('dialog')
    await user.type(within(dialog).getByRole('textbox', { name: /Type 150 to confirm/ }), '150')
    await user.click(within(dialog).getByRole('button', { name: 'Requeue 150 runs' }))

    expect(requests[0]!.body).toMatchObject({ confirmed: true, typedCount: 150 })
  })

  it('keeps failed rows listed after a run, and Run again confirms the remaining rows anew', async () => {
    const user = userEvent.setup()
    let confirms = 0

    function Start() {
      const { start } = useOperations()

      return <Button onPress={() => start({ module: 'diag', command: command({}), filter: {}, noun: 'failed runs' })}>Start</Button>
    }

    renderWithDashboard(<Start />, (request) => {
      if (request.path.endsWith('/confirm')) {
        confirms++

        return { operationId: `op${confirms}`, count: confirms === 1 ? 2 : 1, confirmation: 'confirm', sample: [], token: 't', expiresAt: '', chunkSize: 500 }
      }

      return { operationId: 'op1', rows: [{ id: 'a', outcome: 'done' }, { id: 'b', outcome: 'failed', reason: 'locked by node-b' }], done: true }
    })

    await user.click(screen.getByRole('button', { name: 'Start' }))
    await user.type(screen.getByRole('textbox', { name: /Reason/ }), 'retry')
    await user.click(screen.getByRole('button', { name: 'Count rows' }))
    await user.click(await screen.findByRole('button', { name: 'Requeue 2 failed runs' }))

    const panel = await screen.findByRole('group', { name: 'Requeue failed runs' })
    expect((await within(panel).findByRole('list', { name: 'Rows not changed' })).textContent).toContain('locked by node-b')

    await user.click(within(panel).getByRole('button', { name: 'Run again' }))
    await user.type(screen.getByRole('textbox', { name: /Reason/ }), 'retry')
    await user.click(screen.getByRole('button', { name: 'Count rows' }))

    expect(await screen.findByRole('button', { name: 'Requeue 1 failed runs' })).toBeTruthy()
    expect(confirms).toBe(2)
  })

  it('shows a refused confirm in place, not only in a toast', async () => {
    const user = userEvent.setup()
    const requests = renderWithDashboard(<CommandAction module="diag" command={command({})} ids={['r1']} noun="runs" />, () => {
      throw Object.assign(new Error('x'), { name: 'other' })
    })
    await user.click(screen.getByRole('button', { name: 'Requeue' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Requeue runs' }))

    expect((await screen.findByRole('alert')).textContent).toBe('Requeue failed. Try again.')
    expect(requests).toHaveLength(1)
  })
})
