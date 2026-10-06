import { afterEach, describe, expect, it } from 'vitest'
import { flush, mountWithApp, type Mounted } from '@/__tests__/mount'
import JobProgressCell from '@/components/JobProgressCell.vue'

const cell = () => document.querySelector<HTMLElement>('[data-testid="job-progress"]')

describe('JobProgressCell', () => {
  let mounted: Mounted | undefined

  afterEach(() => {
    mounted?.unmount()
    mounted = undefined
  })

  const render = async (props: Record<string, unknown>) => {
    mounted = mountWithApp(JobProgressCell, props)
    await flush()
  }

  it('shows a live bar with the percent and message for a running row', async () => {
    await render({
      status: 'InProgress',
      percent: 42.46,
      message: 'Imported 4,246 of 10,000 rows',
      updatedAt: '2026-01-01T00:00:00Z',
    })

    expect(cell()).not.toBeNull()
    expect(cell()!.classList).not.toContain('job-progress--muted')
    expect(cell()!.textContent).toContain('42.5%')
    expect(cell()!.textContent).toContain('Imported 4,246 of 10,000 rows')

    const bar = cell()!.querySelector('[role="progressbar"]')
    expect(bar).not.toBeNull()
    expect(bar!.getAttribute('aria-label')).toBe('Job progress 42.5%')
    expect(bar!.getAttribute('aria-valuenow')).toBe('42.46')
  })

  it('shows the last value muted for a finished row so a failure shows how far it got', async () => {
    await render({ status: 'Failed', percent: 42, message: 'Batch 3', updatedAt: '2026-01-01T00:00:00Z' })

    expect(cell()).not.toBeNull()
    expect(cell()!.classList).toContain('job-progress--muted')
    expect(cell()!.textContent).toContain('42%')
    expect(cell()!.textContent).toContain('Batch 3')
  })

  it('renders nothing when the row never reported progress', async () => {
    await render({ status: 'InProgress', percent: null, message: null, updatedAt: null })

    expect(cell()).toBeNull()
    expect(document.querySelector('[role="progressbar"]')).toBeNull()
  })

  it('renders nothing for a row that has not started', async () => {
    await render({ status: 'Queued', percent: 10, message: null, updatedAt: null })

    expect(cell()).toBeNull()
  })
})
