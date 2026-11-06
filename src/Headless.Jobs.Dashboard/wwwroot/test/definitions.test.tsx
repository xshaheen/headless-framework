import { screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { functions, now, open, user } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(() => {
  vi.useRealTimers()
  localStorage.clear()
})

const unclaimed = { ...functions[0]!, name: 'mail.send', claimedBy: [], cronExpression: '0 6 * * *', requestType: undefined }

describe('jobs functions list', () => {
  it('lists each function’s kind, request type, limit, and nodes, and tags one no node runs', async () => {
    open({ functions: [{ ...functions[0]!, requestType: 'Billing.ChargeArgs', maxConcurrency: 8, running: 2, claimedBy: ['node-a', 'node-c'] }, unclaimed] }, { path: 'functions' })
    await screen.findByRole('row', { name: /billing\.charge/ })

    const charge = screen.getByRole('row', { name: /billing\.charge/ })
    expect(charge.textContent).toContain('time')
    expect(charge.textContent).toContain('Billing.ChargeArgs')
    expect(charge.textContent).toContain('2 / 8')
    expect(charge.textContent).toContain('node-anode-c')
    const mail = screen.getByRole('row', { name: /mail\.send/ })
    expect(mail.textContent).toContain('cron 0 6 * * *')
    expect(mail.textContent).toContain('0 / no limit')
    expect(within(mail).getByText('no node runs it').closest('.hd-tag')?.className).toContain('hd-tone-fail')
    expect(screen.getByText('1 not run by any live node; their runs wait until a node claims them.')).toBeTruthy()
  })

  it('opens a function in the docked panel with its contract and where it runs', async () => {
    const actor = user()
    const view = open({ functions: [unclaimed] }, { path: 'functions' })

    await actor.click(await screen.findByRole('row', { name: /mail\.send/ }))

    const panel = await screen.findByRole('region', { name: 'Function mail.send' })
    expect(view.location().search).toBe('?open=mail.send')
    expect(within(panel).getByText('Contract version', { selector: 'dt' }).nextElementSibling?.textContent).toBe('1')
    expect(within(within(panel).getByRole('region', { name: 'Where it runs' })).getByText(/No live node's run filter claims it/)).toBeTruthy()
  })

  it('shows the function panel as its own page, with nothing to close or detach', async () => {
    open({ functions: [unclaimed] }, { path: 'functions/mail.send' })

    const panel = await screen.findByRole('region', { name: 'Function mail.send' })
    expect(within(panel).queryByRole('button', { name: 'Close details' })).toBeNull()
    expect(within(panel).queryByRole('link', { name: 'Open in its own page' })).toBeNull()
  })
})

describe('jobs servers', () => {
  const nodes = [
    { identity: 'node-a', state: 'Alive', role: 'api', claimedFunctions: ['billing.charge', 'billing.refund'] },
    { identity: 'node-b', state: 'Suspected', claimedFunctions: null },
  ]

  it('lists each server’s role, state, and claimed functions, and no scheduler or workers column', async () => {
    open({ nodes }, { path: 'nodes' })

    const row = await screen.findByRole('row', { name: /node-a/ })
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['Server', 'Role', 'State', 'Claimed functions'])
    expect(row.textContent).toContain('api')
    expect(row.textContent).toContain('Alive')
    expect(row.textContent).toContain('billing.chargebilling.refund')
    expect(screen.getByRole('row', { name: /node-b/ }).textContent).toContain('No run filter published')
    expect(screen.queryByRole('columnheader', { name: /Scheduler|Workers|Registered/ })).toBeNull()
  })

  it('opens a server in the docked panel with its claimed functions linked, and offers no scheduler action', async () => {
    const actor = user()
    open({ nodes }, { path: 'nodes' })

    await actor.click(await screen.findByRole('row', { name: /node-a/ }))

    const panel = await screen.findByRole('region', { name: 'Server node-a' })
    expect(within(panel).getByRole('link', { name: 'billing.charge' }).getAttribute('href')).toBe('/jobs/functions/billing.charge')
    expect(within(panel).queryByRole('button', { name: /scheduler/i })).toBeNull()
  })
})
