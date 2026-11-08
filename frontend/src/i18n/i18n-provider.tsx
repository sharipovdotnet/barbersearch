import {
  useCallback,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'

import { I18nContext } from './context'
import {
  readStoredLocale,
  storeLocale,
  type Locale,
} from './locale'
import { setCurrentLocale, t } from './translate'

/**
 * Владелец выбранного языка.
 *
 * `setCurrentLocale` вызывается синхронно и в инициализаторе, и в сеттере: модульный
 * `t()` должен отдавать новый язык уже на первом рендере, не дожидаясь эффекта.
 * `App` подписан на контекст, поэтому смена языка перерисовывает всё дерево.
 */
export function I18nProvider({
  children,
  initialLocale,
}: {
  children: ReactNode
  initialLocale?: Locale
}) {
  const [locale, setLocaleState] = useState<Locale>(() => {
    const initial = initialLocale ?? readStoredLocale()
    setCurrentLocale(initial)

    return initial
  })

  const setLocale = useCallback((next: Locale) => {
    setCurrentLocale(next)
    storeLocale(next)
    setLocaleState(next)
  }, [])

  useEffect(() => {
    setCurrentLocale(locale)
    document.documentElement.lang = locale
    document.documentElement.dataset.locale = locale
    document
      .querySelector('meta[name="description"]')
      ?.setAttribute(
        'content',
        t('BarberSearch — поиск барберов и онлайн-запись в салоны.')
      )
  }, [locale])

  const value = useMemo(
    () => ({ locale, setLocale, t }),
    [locale, setLocale]
  )

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>
}
