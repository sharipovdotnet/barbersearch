import { createContext, useContext } from 'react'

import { DEFAULT_LOCALE, type Locale } from './locale'
import { t } from './translate'

export type I18nContextValue = {
  locale: Locale
  setLocale: (locale: Locale) => void
  t: typeof t
}

/**
 * Значение по умолчанию позволяет рендерить компонент без провайдера (например, в
 * модульном тесте): интерфейс остаётся русским, а переключатель ничего не меняет.
 * В приложении провайдер всегда стоит выше `App`.
 */
export const I18nContext = createContext<I18nContextValue>({
  locale: DEFAULT_LOCALE,
  setLocale: () => {},
  t,
})

export function useI18n(): I18nContextValue {
  return useContext(I18nContext)
}
