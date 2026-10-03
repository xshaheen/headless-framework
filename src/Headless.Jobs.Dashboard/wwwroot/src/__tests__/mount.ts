import { createApp, nextTick, ref, type Component } from 'vue'
import { vi } from 'vitest'
import { createPinia } from 'pinia'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'

export interface Mounted {
  unmount: () => void
}

/**
 * Mounts a component the way main.ts wires the app (Pinia + Vuetify) into a host attached to the document, so
 * teleported overlays such as v-dialog land where queries can reach them.
 */
export const mountWithApp = (component: Component, props?: Record<string, unknown>): Mounted => {
  // happy-dom has no visualViewport, which Vuetify's overlay positioning (dialogs, tooltips) reads unguarded.
  if (!('visualViewport' in globalThis)) {
    vi.stubGlobal('visualViewport', {
      width: window.innerWidth,
      height: window.innerHeight,
      offsetLeft: 0,
      offsetTop: 0,
      scale: 1,
      addEventListener: () => {},
      removeEventListener: () => {},
    })
  }

  const host = document.createElement('div')
  document.body.appendChild(host)

  const app = createApp(component, props)
  app.use(createPinia())
  app.use(createVuetify({ components, directives }))
  app.mount(host)

  return {
    unmount: () => {
      app.unmount()
      host.remove()
      document.body.innerHTML = ''
    },
  }
}

/** Lets pending promise continuations and the resulting re-render settle. */
export const flush = async () => {
  for (let i = 0; i < 5; i++) {
    await new Promise((resolve) => setTimeout(resolve, 0))
    await nextTick()
  }
}

export interface Deferred {
  promise: Promise<void>
  resolve: () => void
  reject: (reason: unknown) => void
}

export const deferred = (): Deferred => {
  let resolve!: () => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<void>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

/**
 * Stands in for a useBaseHttpService-backed service: like the real one, the loader is true for exactly as long as the
 * request is in flight, and response holds what the component renders from.
 */
export const fakeService = <T>(initial: T, impl: (...args: never[]) => Promise<unknown> = async () => initial) => {
  const loader = ref(false)
  const response = ref(initial)
  const requestAsync = vi.fn(async (...args: never[]) => {
    loader.value = true
    try {
      return await impl(...args)
    } finally {
      loader.value = false
    }
  })
  return { loader, response, requestAsync }
}
