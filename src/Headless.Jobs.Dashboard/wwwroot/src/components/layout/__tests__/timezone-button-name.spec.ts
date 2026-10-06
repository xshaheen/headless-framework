import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flush, mountWithApp, type Mounted } from '@/__tests__/mount'
import { useTimeZoneStore } from '@/stores/timeZoneStore'

vi.mock('@/stores/connectionStore', () => {
  const noop = async () => {}
  return {
    useConnectionStore: () => ({
      isReady: false,
      isConnecting: false,
      isWebSocketConnected: false,
      initializeConnectionWithRetry: noop,
      setupVisibilityHandling: () => {},
      initializeConnection: noop,
    }),
  }
})

vi.mock('@/stores/dashboardStore', () => ({ useDashboardStore: () => ({}) }))
vi.mock('@/components/common/AuthHeader.vue', () => ({ __esModule: true, default: { render: () => null } }))
vi.mock('@/components/common/ConfirmDialog.vue', () => ({ __esModule: true, default: { render: () => null } }))

const DashboardLayout = (await import('@/components/layout/DashboardLayout.vue')).default

describe('DashboardLayout time-zone button', () => {
  let mounted: Mounted

  beforeEach(async () => {
    localStorage.clear()
    mounted = mountWithApp(DashboardLayout)
    await flush()
  })

  afterEach(() => {
    mounted.unmount()
    localStorage.clear()
  })

  const button = () => document.querySelector<HTMLElement>('.timezone-button')!

  it('names the button for the effective zone at every width', async () => {
    expect(button().getAttribute('aria-label')).toBe('Display time zone: UTC')

    useTimeZoneStore().setSelectedTimeZone('Africa/Cairo')
    await flush()

    expect(button().getAttribute('aria-label')).toBe('Display time zone: Africa/Cairo')
  })

  it('keeps the open menu out of the button name', async () => {
    button().click()
    await flush()

    expect(document.querySelector('.timezone-card')).not.toBeNull()
    expect(button().getAttribute('aria-expanded')).toBe('true')
    // aria-owns would re-parent the card, and its text, into the button.
    expect(button().hasAttribute('aria-owns')).toBe(false)
    expect(button().getAttribute('aria-controls')).toBeTruthy()
  })
})
