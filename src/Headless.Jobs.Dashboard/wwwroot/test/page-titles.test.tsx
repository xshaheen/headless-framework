import { useViewTitle } from '@headless/dashboard-sdk/components'
import { screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cronJob, now, open, run } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(() => {
  vi.useRealTimers()
})

// renderModule has no shell to write the document title, so the name each page gives the shell is read where the
// shell reads it.
function pageTitle() {
  return useViewTitle.getState()
}

describe('jobs page titles', () => {
  it('names a run page by its function once the run loads', async () => {
    const failed = run('Failed', { function: 'billing.charge' })
    open({ runs: [failed] }, { path: `runs/${failed.id}` })

    await waitFor(() => expect(pageTitle()).toEqual(expect.objectContaining({ title: 'billing.charge run' })))
  })

  it('names a run page that no longer exists, so the shell does not wait for a name', async () => {
    open({ runs: [] }, { path: 'runs/run_gone' })

    await screen.findByText('This run no longer exists.')
    expect(pageTitle()).toEqual(expect.objectContaining({ isNamed: true, title: 'Run not found' }))
  })

  it('names a recurring job page by its function', async () => {
    const job = cronJob({ function: 'reports.nightly' })
    open({ cronJobs: [job] }, { path: `cron-jobs/${job.id}` })

    await waitFor(() =>
      expect(pageTitle()).toEqual(expect.objectContaining({ title: 'reports.nightly recurring job' })),
    )
  })

  it('names a function page by the function its route names', async () => {
    open({}, { path: 'functions/mail.send' })

    await waitFor(() => expect(pageTitle()).toEqual(expect.objectContaining({ title: 'mail.send function' })))
  })
})
