import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { deferred, fakeService, flush, mountWithApp, type Mounted } from '@/__tests__/mount'

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
})

const occurrences = [
  occurrence('failed-1', 'Failed'),
  occurrence('done-1', 'Done'),
  occurrence('idle-1', 'Idle'),
  occurrence('running-1', 'InProgress'),
]

let requeueGate = deferred()

const listService = fakeService({ items: occurrences, totalCount: occurrences.length, pageNumber: 1, pageSize: 20 })
const requeueService = fakeService<object>({}, () => requeueGate.promise)

vi.mock('@/http/services/cronJobOccurrenceService', () => ({
  cronJobOccurrenceService: {
    getByCronJobIdPaginated: () => listService,
    deleteCronJobOccurrence: () => fakeService({}),
  },
}))

vi.mock('@/http/services/cronJobService', () => ({
  cronJobService: { requeueOccurrence: () => requeueService },
}))

// The real hub module opens a SignalR connection on import; the dialog only needs the method-name constants.
vi.mock('@/hub/jobNotificationHub', () => ({
  methodName: new Proxy({}, { get: (_, key) => String(key) }),
}))

// The dialogs are lazily imported; __esModule lets defineAsyncComponent unwrap the stub's default export.
vi.mock('@/components/common/ConfirmDialog.vue', () => ({ __esModule: true, default: { render: () => null } }))

// Imported after the mocks are registered and the fakes above exist, because the mock factories close over them.
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

const requeueButton = (id: string) =>
  [...document.querySelectorAll<HTMLTableRowElement>('tbody tr')]
    .find((tr) => tr.textContent?.includes(`Fn${id}`))
    ?.querySelector<HTMLButtonElement>('.requeue-btn') ?? null

describe('CronOccurrenceDialog requeue action', () => {
  let mounted: Mounted

  beforeEach(async () => {
    requeueGate = deferred()
    mounted = mountWithApp(CronOccurrenceDialog, {
      dialogProps: { id: 'cron-1', retries: 0, retryIntervals: [] },
      jobNotificationHub: hub,
      isOpen: true,
    })
    await flush()
    listService.requestAsync.mockClear()
    requeueService.requestAsync.mockClear()
  })

  afterEach(() => mounted.unmount())

  it('offers requeue only on Failed occurrences', () => {
    expect(document.querySelectorAll('tbody tr').length).toBeGreaterThanOrEqual(occurrences.length)
    expect(requeueButton('failed-1')).not.toBeNull()
    expect(requeueButton('done-1')).toBeNull()
    expect(requeueButton('idle-1')).toBeNull()
    expect(requeueButton('running-1')).toBeNull()
    expect(document.querySelectorAll('.requeue-btn')).toHaveLength(1)
  })

  it('requeues the clicked occurrence, disables the control while pending, then reloads the list', async () => {
    requeueButton('failed-1')!.click()
    await flush()

    expect(requeueService.requestAsync).toHaveBeenCalledExactlyOnceWith('failed-1')
    expect(requeueButton('failed-1')!.disabled).toBe(true)
    expect(listService.requestAsync).not.toHaveBeenCalled()

    requeueButton('failed-1')!.click()
    await flush()
    expect(requeueService.requestAsync).toHaveBeenCalledOnce()

    requeueGate.resolve()
    await flush()

    expect(listService.requestAsync).toHaveBeenCalledExactlyOnceWith('cron-1', 1, 20)
    expect(requeueButton('failed-1')!.disabled).toBe(false)
  })

  it('still reloads the list when the server refuses the requeue', async () => {
    const unhandled = vi.fn()
    process.on('unhandledRejection', unhandled)
    try {
      requeueButton('failed-1')!.click()
      await flush()

      requeueGate.reject(new Error('NotFailed'))
      await flush()

      expect(listService.requestAsync).toHaveBeenCalledOnce()
      expect(unhandled).not.toHaveBeenCalled()
    } finally {
      process.off('unhandledRejection', unhandled)
    }
  })
})
