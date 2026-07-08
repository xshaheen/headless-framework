import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'

// Vuetify
import 'vuetify/styles'
import { createVuetify } from 'vuetify'
import {
  VAlert,
  VApp,
  VAppBar,
  VBadge,
  VBtn,
  VCard,
  VCardActions,
  VCardText,
  VCardTitle,
  VCheckbox,
  VChip,
  VDialog,
  VDivider,
  VExpandTransition,
  VFooter,
  VForm,
  VIcon,
  VMain,
  VPagination,
  VProgressCircular,
  VSelect,
  VSpacer,
  VTab,
  VTable,
  VTabs,
  VTextField,
  VToolbar,
  VToolbarTitle,
  VTooltip,
} from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { aliases, mdi } from 'vuetify/iconsets/mdi'
import '@mdi/font/css/materialdesignicons.css'

const components = {
  VAlert,
  VApp,
  VAppBar,
  VBadge,
  VBtn,
  VCard,
  VCardActions,
  VCardText,
  VCardTitle,
  VCheckbox,
  VChip,
  VDialog,
  VDivider,
  VExpandTransition,
  VFooter,
  VForm,
  VIcon,
  VMain,
  VPagination,
  VProgressCircular,
  VSelect,
  VSpacer,
  VTab,
  VTable,
  VTabs,
  VTextField,
  VToolbar,
  VToolbarTitle,
  VTooltip,
}

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
const app = createApp(App)

app.use(pinia)
app.use(router)
app.use(vuetify)

app.mount('#app')
