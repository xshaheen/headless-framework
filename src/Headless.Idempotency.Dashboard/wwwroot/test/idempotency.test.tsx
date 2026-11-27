import { act, screen, within } from '@testing-library/react'
import { DashboardApiError, type ApiRequest } from '@headless/dashboard-sdk/data'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { RecordView } from '../src/api.ts'
import { cacheStore, found, hash, held, key, notFound, now, open, records, user } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
})

async function lookUp(actor: ReturnType<typeof user>, text = key, tenant = '') {
  if (tenant) {
    await actor.type(await screen.findByRole('textbox', { name: /^Tenant/ }), tenant)
  }

  await actor.type(await screen.findByRole('textbox', { name: /^Idempotency key/ }), text)
  await actor.click(screen.getByRole('button', { name: 'Look up' }))
}

function lookups(requests: ApiRequest[]) {
  return requests.filter((request) => request.path === 'idempotency/lookup')
}

async function result() {
  return screen.findByRole('region', { name: 'Lookup result' })
}

describe('idempotency', () => {
  it('states the store provider, location, retention, lease bounds, and purge', async () => {
    open()

    const stats = await screen.findByRole('region', { name: 'Idempotency store' })
    expect(within(stats).getByText('PostgreSql')).toBeTruthy()
    expect(within(stats).getByText('"headless"."idempotency_records"')).toBeTruthy()
    expect(within(stats).getByText('1 d')).toBeTruthy()
    expect(within(stats).getByText('2 min')).toBeTruthy()
    expect(within(stats).getByText('1 s to 1 d')).toBeTruthy()
    expect(within(stats).getByText('every 1 h')).toBeTruthy()
  })

  it('reads an absent purge as off or expiring on its own on the cache provider', async () => {
    open({ store: cacheStore })

    const stats = await screen.findByRole('region', { name: 'Idempotency store' })
    expect(within(stats).getByText('Off')).toBeTruthy()
    expect(within(stats).getByText(/purge is off, or the records expire on their own/)).toBeTruthy()
  })

  it('shows an in-process store when the provider serves no location', async () => {
    open({ store: { ...cacheStore, location: undefined, provider: 'Headless.Idempotency.InMemory' } })

    const stats = await screen.findByRole('region', { name: 'Idempotency store' })
    expect(within(stats).getByText('In process')).toBeTruthy()
  })

  it('sends one POST with the key and tenant in the body, never in a path or query', async () => {
    const actor = user()
    const { requests } = open({ lookups: [found(records.completed, 'acme')] })

    await lookUp(actor, key, 'acme')
    await result()

    const sent = lookups(requests)
    expect(sent).toHaveLength(1)
    expect(sent[0]!.method).toBe('POST')
    expect(sent[0]!.body).toEqual({ key, tenantId: 'acme' })

    for (const request of requests) {
      expect(request.path).not.toContain(key)
      expect(JSON.stringify(request.query ?? {})).not.toContain(key)
    }
  })

  it('sends no tenant when Tenant is blank and names the host scope when nothing is found', async () => {
    const actor = user()
    const { requests } = open({ lookups: [notFound()] })

    expect(await screen.findByText(/A blank Tenant reads the host scope \(no tenant\)/)).toBeTruthy()
    expect(screen.getByText(/never searches every tenant/)).toBeTruthy()
    await lookUp(actor)

    const region = await result()
    expect(lookups(requests)[0]!.body).toEqual({ key })
    expect(within(region).getByText('No record for this key in the host scope (no tenant)')).toBeTruthy()
    expect(within(region).queryByText('Generation')).toBeNull()
  })

  // The search row aligns its fields to the bottom, so a field with its own helper line sits higher than the rest:
  // the form-level hint carries the guidance instead, as the board draws it.
  it('keeps helper text off the search fields so the row lines up', async () => {
    open()

    const form = await screen.findByRole('search', { name: 'Look up a key' })
    for (const field of within(form).getAllByRole('textbox')) {
      expect(field.getAttribute('aria-describedby')).toBeNull()
    }
  })

  it('names the tenant it searched when a tenant lookup finds nothing', async () => {
    const actor = user()
    open({ lookups: [notFound('acme')] })

    await lookUp(actor, key, 'acme')

    expect(within(await result()).getByText('No record for this key in tenant acme')).toBeTruthy()
  })

  it('shows a completed key with its generation, takeovers, fingerprint, retention, result contract and size', async () => {
    const actor = user()
    open({ lookups: [found(records.completed)] })

    await lookUp(actor)

    const region = await result()
    expect(within(region).getByRole('heading', { name: 'Completed' })).toBeTruthy()
    expect(within(region).getByText(/replay the stored result/)).toBeTruthy()
    expect(within(region).getByText('48211')).toBeTruthy()
    expect(within(region).getByText('Takeovers')).toBeTruthy()
    expect(within(region).getByText(`v1 · ${hash}`)).toBeTruthy()
    expect(within(region).getByText('Retention until')).toBeTruthy()
    expect(within(region).getByText(/orders\.create\/v1 · 812 byte/)).toBeTruthy()
    expect(within(region).getByText(/As of/)).toBeTruthy()
  })

  it.each<[string, RecordView, string, RegExp]>([
    ['in flight', records.inFlight, 'In flight', /gets InFlight and retries later/],
    ['stalled', records.stalled, 'Stalled', /next admission takes the key over/],
    ['released', records.released, 'Released', /runs the operation again/],
    ['never admitted', records.notAdmitted, 'Not admitted yet', /no attempt was admitted/],
  ])('shows a %s key with its state and what the next admission does', async (_, record, label, meaning) => {
    const actor = user()
    open({ lookups: [found(record)] })

    await lookUp(actor)

    const region = await result()
    expect(within(region).getByRole('heading', { name: label })).toBeTruthy()
    expect(within(region).getByText(meaning)).toBeTruthy()
  })

  it('shows the lease expiry of an in-flight key and its recovery point without its state bytes', async () => {
    const actor = user()
    open({ lookups: [found(records.inFlight)] })

    await lookUp(actor)

    const region = await result()
    expect(within(region).getByText('Lease expires at')).toBeTruthy()
    expect(within(region).getByText(/payment-authorized · payments\.recovery\/v1 · 96 byte/)).toBeTruthy()
    expect(within(region).queryByText('Stored result')).toBeNull()
  })

  it('shows the lease expiry and takeover count of a stalled key', async () => {
    const actor = user()
    open({ lookups: [found(records.stalled)] })

    await lookUp(actor)

    const region = await result()
    expect(within(region).getByText('Lease expired at')).toBeTruthy()
    expect(within(region).getByText('Takeovers')).toBeTruthy()
  })

  it('notes that an in-flight key past retention is kept until its attempt ends', async () => {
    const actor = user()
    open({ lookups: [found(records.pastRetention)] })

    await lookUp(actor)

    const region = await result()
    expect(within(region).getByRole('heading', { name: 'In flight' })).toBeTruthy()
    expect(within(region).getByText(/Kept past retention/)).toBeTruthy()
  })

  it('draws no takeover row for a record whose provider does not count takeovers', async () => {
    const actor = user()
    open({ store: cacheStore, lookups: [found({ ...records.completed, takeoverCount: undefined })] })

    await lookUp(actor)

    expect(within(await result()).queryByText('Takeovers')).toBeNull()
  })

  it('looks the same key and tenant up again', async () => {
    const actor = user()
    const { requests } = open({ lookups: [found(records.inFlight, 'acme'), found(records.completed, 'acme')] })

    await lookUp(actor, key, 'acme')
    await actor.click(within(await result()).getByRole('button', { name: 'Look up again' }))

    expect(await within(await result()).findByRole('heading', { name: 'Completed' })).toBeTruthy()
    const sent = lookups(requests)
    expect(sent).toHaveLength(2)
    expect(sent[1]!.body).toEqual({ key, tenantId: 'acme' })
  })

  it('shows a store failure as an error with Retry and never a previous answer', async () => {
    const actor = user()
    const { requests } = open({
      lookups: [
        found(records.completed),
        new DashboardApiError(503, 'The idempotency store is unavailable. Try again.'),
        found(records.completed),
      ],
    })

    await lookUp(actor)
    await within(await result()).findByRole('heading', { name: 'Completed' })
    await actor.click(screen.getByRole('button', { name: 'Look up again' }))

    const region = await result()
    expect(await within(region).findByText('The idempotency store is unavailable. Try again.')).toBeTruthy()
    expect(within(region).queryByRole('heading', { name: 'Completed' })).toBeNull()
    expect(within(region).queryByText(/No record/)).toBeNull()

    await actor.click(within(region).getByRole('button', { name: 'Retry' }))
    expect(await within(await result()).findByRole('heading', { name: 'Completed' })).toBeTruthy()
    expect(lookups(requests)).toHaveLength(3)
  })

  it('shows a refused field without repeating the key', async () => {
    const actor = user()
    open({
      lookups: [
        new DashboardApiError(400, 'key must not start or end with white space.', {
          key: ['key must not start or end with white space.'],
        }),
      ],
    })

    await lookUp(actor, `${key} `)

    const region = await result()
    expect(within(region).getByText('key must not start or end with white space.')).toBeTruthy()
    expect(region.textContent).not.toContain(key)
  })

  it('asks for a key before it sends a lookup', async () => {
    const actor = user()
    const { requests } = open()

    await actor.click(await screen.findByRole('button', { name: 'Look up' }))

    expect(await screen.findByText('Type the idempotency key to look up.')).toBeTruthy()
    expect(lookups(requests)).toHaveLength(0)
  })

  it('clears a shown result when the key or the tenant changes', async () => {
    const actor = user()
    open({ lookups: [found(records.completed)] })

    await lookUp(actor)
    await result()
    await actor.type(screen.getByRole('textbox', { name: /^Idempotency key/ }), 'x')
    expect(screen.queryByRole('region', { name: 'Lookup result' })).toBeNull()

    await actor.click(screen.getByRole('button', { name: 'Look up' }))
    await result()
    await actor.type(screen.getByRole('textbox', { name: /^Tenant/ }), 'a')
    expect(screen.queryByRole('region', { name: 'Lookup result' })).toBeNull()
  })

  it('never shows the answer of a lookup that a later edit superseded', async () => {
    const actor = user()
    const slow = held()
    open({ lookups: [slow.answer, found(records.inFlight)] })

    await lookUp(actor)
    await actor.type(screen.getByRole('textbox', { name: /^Idempotency key/ }), '-2')
    await actor.click(screen.getByRole('button', { name: 'Look up' }))
    await within(await result()).findByRole('heading', { name: 'In flight' })

    await act(async () => slow.release(found(records.completed)))

    expect(within(await result()).queryByRole('heading', { name: 'Completed' })).toBeNull()
    expect(within(await result()).getByRole('heading', { name: 'In flight' })).toBeTruthy()
  })

  it('returns focus to the result after Look up again', async () => {
    const actor = user()
    open({ lookups: [found(records.inFlight), found(records.completed)] })

    await lookUp(actor)
    await actor.click(within(await result()).getByRole('button', { name: 'Look up again' }))

    await within(await result()).findByRole('heading', { name: 'Completed' })
    expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Lookup result' }))
  })

  it('returns focus to the result after Retry', async () => {
    const actor = user()
    open({
      lookups: [
        new DashboardApiError(503, 'The idempotency store is unavailable. Try again.'),
        found(records.completed),
      ],
    })

    await lookUp(actor)
    await actor.click(within(await result()).getByRole('button', { name: 'Retry' }))

    await within(await result()).findByRole('heading', { name: 'Completed' })
    expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Lookup result' }))
  })

  it('sends no second lookup while Look up again is pending, then shows its answer', async () => {
    const actor = user()
    const slow = held()
    const { requests } = open({ lookups: [found(records.inFlight), slow.answer, found(records.completed)] })

    await lookUp(actor)
    await actor.click(within(await result()).getByRole('button', { name: 'Look up again' }))
    await actor.click(screen.getByRole('button', { name: 'Look up' }))
    expect(lookups(requests)).toHaveLength(2)

    await act(async () => slow.release(found(records.stalled)))

    expect(await within(await result()).findByRole('heading', { name: 'Stalled' })).toBeTruthy()
    expect(lookups(requests)).toHaveLength(2)
  })

  it('shows no figure the server does not serve, before or after a lookup', async () => {
    const actor = user()
    open({ lookups: [found(records.completed)] })

    await screen.findByRole('region', { name: 'Idempotency store' })
    const unserved =
      /Lookups today|admitted at|completed at|HTTP status|HTTP \d{3}|replays served|replay count|Show response body/i
    expect(screen.queryByText(unserved)).toBeNull()

    await lookUp(actor)
    await result()
    expect(screen.queryByText(unserved)).toBeNull()
    expect(screen.queryByRole('button', { name: /release|expire|delete|response body/i })).toBeNull()
  })
})
