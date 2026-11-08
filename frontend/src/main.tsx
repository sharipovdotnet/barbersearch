import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { SessionProvider } from '@/components/session-provider'
import { I18nProvider, readStoredLocale, setCurrentLocale, translate } from '@/i18n'

/**
 * Язык ставится до первого рендера: иначе у пользователя с сохранённым английским
 * на мгновение мелькают русские `<title>` и meta description из `index.html`.
 */
const initialLocale = readStoredLocale()
setCurrentLocale(initialLocale)
document.documentElement.lang = initialLocale
document.title = translate(
  initialLocale,
  'BarberSearch — поиск барберов и запись'
)
document
  .querySelector('meta[name="description"]')
  ?.setAttribute(
    'content',
    translate(
      initialLocale,
      'BarberSearch — поиск барберов и онлайн-запись в салоны.'
    )
  )

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nProvider>
      <SessionProvider>
        <App />
      </SessionProvider>
    </I18nProvider>
  </StrictMode>,
)
