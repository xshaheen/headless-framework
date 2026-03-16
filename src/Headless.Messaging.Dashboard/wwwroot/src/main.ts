import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'
import { createI18nInstance } from './i18n'

// Vuetify
import 'vuetify/styles'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { aliases, mdi } from 'vuetify/iconsets/mdi'
import '@mdi/font/css/materialdesignicons.css'

const vuetify = createVuetify({
  components,
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
    defaultTheme: 'dark',
  },
})

const pinia = createPinia()
const i18n = createI18nInstance()
const app = createApp(App)

app.use(pinia)
app.use(router)
app.use(vuetify)
app.use(i18n)

app.mount('#app')
