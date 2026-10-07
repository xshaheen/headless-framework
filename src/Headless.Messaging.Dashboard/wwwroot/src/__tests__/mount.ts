import { createApp, nextTick, type Component, type Plugin } from 'vue'
import { vi } from 'vitest'
import { createPinia } from 'pinia'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'

export interface Mounted {
  unmount: () => void
}

/**
 * Mounts a component the way main.ts wires the app (Pinia + Vuetify, plus any extra plugin such as a router) into a
 * host attached to the document, so teleported overlays such as v-dialog land where queries can reach them.
 */
export const mountWithApp = (component: Component, plugins: Plugin[] = []): Mounted => {
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

  const app = createApp(component)
  app.use(createPinia())
  app.use(createVuetify({ components, directives }))
  for (const plugin of plugins) app.use(plugin)
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
