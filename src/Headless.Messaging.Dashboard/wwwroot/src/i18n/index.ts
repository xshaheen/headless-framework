import { createI18n } from 'vue-i18n'
import en from './locales/en'
import ar from './locales/ar'

export function createI18nInstance() {
  return createI18n({
    legacy: false,
    locale: 'en',
    fallbackLocale: 'en',
    messages: {
      en,
      ar,
    },
  })
}
