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
  VAlertTitle,
  VApp,
  VAppBar,
  VBadge,
  VBtn,
  VBtnGroup,
  VCard,
  VCardActions,
  VCardItem,
  VCardSubtitle,
  VCardText,
  VCardTitle,
  VCheckbox,
  VCheckboxBtn,
  VChip,
  VCol,
  VCombobox,
  VContainer,
  VDataTable,
  VDateInput,
  VDialog,
  VDivider,
  VExpansionPanel,
  VExpansionPanels,
  VExpansionPanelText,
  VExpansionPanelTitle,
  VFooter,
  VForm,
  VIcon,
  VList,
  VListItem,
  VListItemTitle,
  VListSubheader,
  VMain,
  VMenu,
  VPagination,
  VProgressCircular,
  VProgressLinear,
  VRangeSlider,
  VRow,
  VSelect,
  VSheet,
  VSpacer,
  VTab,
  VTabs,
  VTextField,
  VTextarea,
  VTooltip,
  VWindow,
  VWindowItem,
} from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { aliases, mdi } from 'vuetify/iconsets/mdi'
import '@mdi/font/css/materialdesignicons.css'

const components = {
  VAlert,
  VAlertTitle,
  VApp,
  VAppBar,
  VBadge,
  VBtn,
  VBtnGroup,
  VCard,
  VCardActions,
  VCardItem,
  VCardSubtitle,
  VCardText,
  VCardTitle,
  VCheckbox,
  VCheckboxBtn,
  VChip,
  VCol,
  VCombobox,
  VContainer,
  VDataTable,
  VDateInput,
  VDialog,
  VDivider,
  VExpansionPanel,
  VExpansionPanels,
  VExpansionPanelText,
  VExpansionPanelTitle,
  VFooter,
  VForm,
  VIcon,
  VList,
  VListItem,
  VListItemTitle,
  VListSubheader,
  VMain,
  VMenu,
  VPagination,
  VProgressCircular,
  VProgressLinear,
  VRangeSlider,
  VRow,
  VSelect,
  VSheet,
  VSpacer,
  VTab,
  VTabs,
  VTextField,
  VTextarea,
  VTooltip,
  VWindow,
  VWindowItem,
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
