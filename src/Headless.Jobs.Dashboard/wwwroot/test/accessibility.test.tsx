import { screen } from '@testing-library/react'
import { axeViolations } from '@headless/dashboard-sdk/testing/axe'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cronJob, node, now, open, run, user } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(() => {
  vi.useRealTimers()
  localStorage.clear()
})

const failed = run('Failed', { function: 'billing.refund' })
const fake = { runs: [failed, run('Succeeded')], cronJobs: [cronJob()], nodes: [node()] }

describe('jobs accessibility', () => {
  it.each([
    '',
    'runs',
    `runs/${failed.id}`,
    'cron-jobs',
    'cron-jobs/c0000000-0000-0000-0000-000000000001',
    'functions',
    'functions/billing.charge',
    'nodes',
  ])('the %s view breaks no WCAG A or AA rule axe checks', async (path) => {
    open(fake, { path })

    await screen.findByRole('heading', { level: 1 })
    expect(await axeViolations()).toEqual([])
  })

  it('the runs list with a run open in the panel breaks no rule', async () => {
    const actor = user()
    open(fake, { path: 'runs' })

    await actor.click(await screen.findByRole('row', { name: /billing\.refund/ }))
    await screen.findByRole('region', { name: `Run ${failed.id}` })

    expect(await axeViolations()).toEqual([])
  })
})
