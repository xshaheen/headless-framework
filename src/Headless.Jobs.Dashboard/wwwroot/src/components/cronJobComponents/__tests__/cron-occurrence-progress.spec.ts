import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fakeService, flush, mountWithApp, type Mounted } from '@/__tests__/mount'
import type { JobProgressNotification } from '@/utilities/job-progress'

const occurrence = (id: string, status: string, percent: number | null = null) => ({
  id,
  function: `Fn${id}`,
  status,
  retryIntervals: [],
  lockHolder: '-',
  lockedAt: '',
  executionTime: '2026-01-01T00:00:00Z',
  executionTimeFormatted: '',
  executedAt: '',
  elapsedTime: 0,
  retryCount: 0,
  progressPercent: percent,
  progressMessage: percent == null ? null : 'Step 2',
  progressUpdatedAt: percent == null ? null : '2026-01-01T00:00:00Z',
})

const listService = fakeService({
  items: [occurrence('running-1', 'InProgress'), occurrence('failed-1', 'Failed', 70)],
  totalCount: 2,
  pageNumber: 1,
  pageSize: 20,
})

vi.mock('@/http/services/cronJobOccurrenceService', () => ({
  cronJobOccurrenceService: {
    getByCronJobIdPaginated: () => listService,
    deleteCronJobOccurrence: () => fakeService({}),
  },
}))

vi.mock('@/http/services/cronJobService', () => ({
  cronJobService: { requeueOccurrence: () => fakeService({}) },
}))

// The real hub module opens a SignalR connection on import; the dialog only needs the method-name constants.
vi.mock('@/hub/jobNotificationHub', () => ({
  methodName: new Proxy({}, { get: (_, key) => String(key) }),
}))

vi.mock('@/components/common/ConfirmDialog.vue', () => ({ __esModule: true, default: { render: () => null } }))

// Imported after the mocks are registered and the fakes above exist, because the mock factories close over them.
const CronOccurrenceDialog = (await import('@/components/cronJobComponents/CronOccurrenceDialog.vue')).default

const noop = () => {}
let progressHandler: ((notification: JobProgressNotification) => void) | undefined
const hub = {
  onReceiveUpdateCronJobOccurrence: noop,
  onReceiveAddCronJobOccurrence: noop,
  onReceiveJobProgress: (callback: (notification: JobProgressNotification) => void) => {
    progressHandler = callback
  },
  stopReceiver: noop,
  joinGroup: noop,
  leaveGroup: noop,
}

const progressIn = (id: string) =>
  [...document.querySelectorAll<HTMLTableRowElement>('tbody tr')]
    .find((tr) => tr.textContent?.includes(`Fn${id}`))
    ?.querySelector<HTMLElement>('[data-testid="job-progress"]') ?? null

describe('CronOccurrenceDialog progress', () => {
  let mounted: Mounted

  beforeEach(async () => {
    progressHandler = undefined
    mounted = mountWithApp(CronOccurrenceDialog, {
      dialogProps: { id: 'cron-1', retries: 0, retryIntervals: [] },
      jobNotificationHub: hub,
      isOpen: true,
    })
    await flush()
    listService.requestAsync.mockClear()
  })

  afterEach(() => mounted.unmount())

  it('shows stored progress muted on a finished occurrence', () => {
    expect(progressIn('failed-1')?.classList).toContain('job-progress--muted')
    expect(progressIn('failed-1')?.textContent).toContain('70%')
    expect(progressIn('running-1')).toBeNull()
  })

  it('patches the running occurrence in place and ignores one that is not on the page', async () => {
    progressHandler!({ id: 'running-1', type: 0, percent: 25, message: 'Step 1', updatedAt: '2026-01-01T00:00:02Z' })
    progressHandler!({ id: 'elsewhere', type: 0, percent: 90, message: null, updatedAt: '2026-01-01T00:00:02Z' })
    await flush()

    expect(progressIn('running-1')?.textContent).toContain('25%')
    expect(progressIn('running-1')?.textContent).toContain('Step 1')
    expect(listService.requestAsync).not.toHaveBeenCalled()
  })
})
