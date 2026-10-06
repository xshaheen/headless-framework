import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fakeService, flush, mountWithApp, type Mounted } from '@/__tests__/mount'

const occurrence = (id: string, status: string) => ({
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
  progressPercent: null,
  progressMessage: null,
  progressUpdatedAt: null,
})

const listService = fakeService({
  items: [occurrence('failed-1', 'Failed')],
  totalCount: 1,
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

vi.mock('@/hub/jobNotificationHub', () => ({
  methodName: new Proxy({}, { get: (_, key) => String(key) }),
}))

vi.mock('@/components/common/ConfirmDialog.vue', () => ({ __esModule: true, default: { render: () => null } }))

const CronOccurrenceDialog = (await import('@/components/cronJobComponents/CronOccurrenceDialog.vue')).default

const noop = () => {}
const hub = {
  onReceiveUpdateCronJobOccurrence: noop,
  onReceiveAddCronJobOccurrence: noop,
  onReceiveJobProgress: noop,
  stopReceiver: noop,
  joinGroup: noop,
  leaveGroup: noop,
}

describe('CronOccurrenceDialog accessible names', () => {
  let mounted: Mounted

  beforeEach(async () => {
    mounted = mountWithApp(CronOccurrenceDialog, {
      dialogProps: { id: 'cron-1', retries: 0, retryIntervals: [] },
      jobNotificationHub: hub,
      isOpen: true,
    })
    await flush()
  })

  afterEach(() => mounted.unmount())

  it('names the icon-only occurrence actions', () => {
    expect(document.querySelector('tbody .requeue-btn')?.getAttribute('aria-label')).toBe('Requeue occurrence')
    expect(document.querySelector('tbody .delete-btn')?.getAttribute('aria-label')).toBe('Delete occurrence')
  })
})
