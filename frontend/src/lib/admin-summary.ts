import { t } from '@/i18n'
import { ApiError, apiRequest } from '@/lib/api'
import { describeApiFailure } from '@/lib/errors'
import { isAdminSummary } from '@/lib/response-guards'

/**
 * Админская сводка «что требует внимания» (`GET api/admin/summary`).
 *
 * Единственное агрегатное чтение админки: счётчики разделов в одном ответе, без параметров,
 * пагинации и курсора. Счётчики считает сервер теми же предикатами, что фильтры разделов
 * (`is_published = false`, `is_verified = false`, `is_email_verified = false`, `Pending`
 * у фото, заявок и салонов, `Confirmed` у броней), поэтому число в сводке — это ровно
 * размер очереди раздела, а «самый старый» — первая строка FIFO-очереди отзывов.
 *
 * Здесь только чтение: мутаций у сводки нет, действия остаются в разделах. Email и телефон
 * клиента в ответ не входят — только имена, поэтому в адресе и в состоянии экрана нет PII.
 */

/** Самая старая строка очереди модерации — компактно, без визита и email. */
export type AdminSummaryOldestReview = {
  id: string
  rating: number
  /**
   * UTC: с какого момента отзыв ждёт решения. Экран показывает ожидание словами,
   * а не дату: правка опубликованного отзыва возвращает его в очередь, и ожидание
   * начинается заново — это не дата создания.
   */
  awaitingSince: string
  barberDisplayName: string
  customerDisplayName: string
}

export type AdminSummary = {
  reviews: { pendingCount: number; oldest: AdminSummaryOldestReview | null }
  barbers: { unverifiedCount: number }
  salons: {
    inactiveCount: number
    /** Салоны на проверке: ровно фильтр `moderation_status = Pending` каталога. */
    pendingModerationCount: number
  }
  /**
   * Очереди премодерации фото: ровно размеры вкладок `/admin/photos`.
   * Числа считает сервер теми же предикатами, что и чтения очередей; клиент
   * их не пересчитывает и не складывает.
   */
  photos: { pendingAvatarsCount: number; pendingPortfolioCount: number }
  /** Неподтверждённый email среди всех ролей: фильтр `status=unverified` + `role=all`. */
  users: { unverifiedCount: number }
  /** Очередь «Стать барбером»: ровно `status = Pending`. */
  barberRequests: { pendingCount: number }
  /**
   * Все брони в статусе `Confirmed`, включая прошлые: ровно фильтр «Подтверждена»
   * раздела броней, а не «предстоящие».
   */
  bookings: { confirmedCount: number }
}

/**
 * Числа для счётчиков рейла «Разделы».
 *
 * Считаются из одного снимка сводки, поэтому рейл и экран «Сводка» не расходятся.
 * `photos` — сумма двух очередей: бейдж один, разбивка остаётся в `photosAvatars`
 * и `photosPortfolio` для `aria-label`.
 */
export type AdminSectionCounts = {
  reviews: number
  photos: number
  photosAvatars: number
  photosPortfolio: number
  barbers: number
  users: number
  barberRequests: number
  salons: number
  bookings: number
}

/**
 * Раскладывает снимок сводки по пунктам рейла.
 *
 * Предикаты здесь не повторяются: сервер уже посчитал их и прислал числа. Единственная
 * арифметика — сумма очередей фото, и она ровно та же, что обещает пункт «Фото —
 * Аватары + Портфолио».
 */
export function selectSectionCounts(
  summary: AdminSummary
): AdminSectionCounts {
  const photosAvatars = summary.photos.pendingAvatarsCount
  const photosPortfolio = summary.photos.pendingPortfolioCount

  return {
    reviews: summary.reviews.pendingCount,
    photos: photosAvatars + photosPortfolio,
    photosAvatars,
    photosPortfolio,
    barbers: summary.barbers.unverifiedCount,
    users: summary.users.unverifiedCount,
    barberRequests: summary.barberRequests.pendingCount,
    salons: summary.salons.pendingModerationCount,
    bookings: summary.bookings.confirmedCount,
  }
}

/**
 * Число внутри бейджа: `99+` после сотни.
 *
 * Кап нужен, чтобы пилюля не превращалась в плашку на полпункта; точное число
 * остаётся в `aria-label` ссылки.
 */
export function formatAdminBadgeCount(count: number): string {
  return count > 99 ? '99+' : String(count)
}

/**
 * Читает сводку.
 *
 * Ни `status`, ни `pageSize`, ни `cursor`: у эндпоинта нет параметров. Тело проверяется
 * строгим guard'ом `isAdminSummary`, поэтому битый ответ становится ошибкой экрана,
 * а не сломанными числами.
 */
export async function fetchAdminSummary(
  signal?: AbortSignal
): Promise<AdminSummary> {
  return apiRequest<AdminSummary>(
    '/admin/summary',
    { signal },
    { validate: isAdminSummary }
  )
}

/**
 * Текст ошибки сводки: `403` — это роль, `429` — частота запросов.
 *
 * Английский `detail` не показывается никогда: неизвестный сбой уходит в запасной
 * русский текст экрана.
 */
export function describeAdminSummaryError(
  cause: unknown,
  fallback: string
): string {
  if (!(cause instanceof ApiError)) {
    return t(fallback)
  }

  if (cause.status === 403 || cause.hasCode('auth.forbidden')) {
    return t('Раздел доступен только администратору.')
  }

  if (cause.status === 429) {
    return t('Слишком много запросов. Подождите минуту и повторите.')
  }

  return describeApiFailure(cause, fallback)
}
