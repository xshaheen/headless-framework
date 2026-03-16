import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'

// Import Vuetify
import 'vuetify/styles'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { aliases, mdi } from 'vuetify/iconsets/mdi'
import '@mdi/font/css/materialdesignicons.css'

// Import Vuetify lab components
import { VDateInput } from 'vuetify/labs/VDateInput'
import { VPullToRefresh } from 'vuetify/labs/VPullToRefresh'

// Import VueTheMask for input masking
import VueTheMask from 'vue-the-mask'

// Import styles
import './assets/main.css'

// Create Vuetify instance
const vuetify = createVuetify({
  components: {
    ...components,
    VDateInput,
    VPullToRefresh
  },
  directives,
  defaults: {
    VBtn: {
      density: 'compact',
      rounded: 'lg',
    },
    VBtnGroup: {
      density: 'compact',
    },
    VChip: {
      density: 'comfortable',
      size: 'small',
    },
    VTextField: {
      density: 'compact',
      variant: 'outlined',
      hideDetails: 'auto',
    },
    VTextarea: {
      density: 'compact',
      variant: 'outlined',
      hideDetails: 'auto',
      rows: 3,
    },
    VSelect: {
      density: 'compact',
      variant: 'outlined',
      hideDetails: 'auto',
    },
    VAutocomplete: {
      density: 'compact',
      variant: 'outlined',
      hideDetails: 'auto',
    },
    VCombobox: {
      density: 'compact',
      variant: 'outlined',
      hideDetails: 'auto',
    },
    VDateInput: {
      density: 'compact',
      variant: 'outlined',
      hideDetails: 'auto',
    },
    VDataTable: {
      density: 'comfortable',
    },
    VCard: {
      rounded: 'lg',
    },
  },
  icons: {
    defaultSet: 'mdi',
    aliases,
    sets: {
      mdi,
    },
  },
  theme: {
    defaultTheme: 'dark'
  }
})

// Create Pinia store
const pinia = createPinia()

// Create Vue app
const app = createApp(App)

// Use plugins
app.use(pinia)
app.use(router)
app.use(vuetify)
app.use(VueTheMask as any)

// Mount the app
app.mount('#app')
// Make connection store available globally for debugging
import { useConnectionStore } from './stores/connectionStore'

// Expose connection store methods globally for debugging
const connectionStore = useConnectionStore()
;(window as any).connectionStore = connectionStore
