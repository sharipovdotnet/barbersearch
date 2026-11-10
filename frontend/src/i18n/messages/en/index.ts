import adminAnalytics from './admin-analytics.json'
import adminCatalogue from './admin-catalogue.json'
import adminLibs from './admin-libs.json'
import adminQueues from './admin-queues.json'
import account from './account.json'
import admin from './admin.json'
import auth from './auth.json'
import barber from './barber.json'
import barberCabinet from './barber-cabinet.json'
import barberImport from './barber-import.json'
import barberPages from './barber-pages.json'
import barberProfile from './barber-profile.json'
import barberPublic from './barber-public.json'
import barberSchedule from './barber-schedule.json'
import common from './common.json'
import errors from './errors.json'
import home from './home.json'
import labels from './labels.json'
import libs from './libs.json'
import privacy from './privacy.json'

/**
 * Английский каталог, разложенный по областям.
 *
 * Порядок слияния не важен: одинаковый русский ключ должен переводиться одинаково,
 * а расхождение ловит `npm run check:i18n`. Области позволяют параллельно править
 * разные экраны, не конфликтуя в одном файле.
 */
export const en: Record<string, string> = {
  ...common,
  ...labels,
  ...home,
  ...auth,
  ...account,
  ...barber,
  ...barberPublic,
  ...barberCabinet,
  ...barberSchedule,
  ...barberProfile,
  ...barberImport,
  ...barberPages,
  ...admin,
  ...adminQueues,
  ...adminCatalogue,
  ...adminAnalytics,
  ...adminLibs,
  ...privacy,
  ...libs,
  ...errors,
}
