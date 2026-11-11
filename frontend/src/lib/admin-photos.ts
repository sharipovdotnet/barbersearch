import { t } from '@/i18n'
import { ApiError, apiRequest } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import type { PhotoModerationStatus } from '@/lib/photo-moderation'
import {
  isAdminAvatarPhotoPage,
  isAdminPortfolioPhotoPage,
} from '@/lib/response-guards'

/**
 * Очередь премодерации фото: два чтения и четыре решения администратора.
 *
 * Контракт отдельный от публичных карточек: очередь отдаёт владельца кадра, статус
 * и момент поступления, а не весь профиль. Оба чтения — только `Pending`, FIFO;
 * решения — cookie-мутации через `mutateWithCsrf` и отвечают `204`. Повторное
 * решение по уже проверенному кадру — `409`, его текст говорит обновить список.
 * Ошибки ветвятся по `status` и `errorCodes`; английский `detail` не показывается.
 */

/** Строка очереди аватаров: владелец, сырой URL и статус. */
export type AdminAvatarPhoto = {
  userId: string
  firstName: string
  lastName: string
  email: string
  /** Сырой URL: очередь содержит только кадры с файлом. */
  avatarUrl: string
  moderationStatus: PhotoModerationStatus
  /** UTC: когда кадр встал в очередь; FIFO по этому полю. */
  submittedAt: string
}

/** Строка очереди портфолио: кадр, его барбер и владелец профиля. */
export type AdminPortfolioPhoto = {
  photoId: string
  barberProfileId: string
  barberDisplayName: string
  /** Email владельца профиля — по нему очередь ищется (PII, только админский ответ). */
  ownerEmail: string
  url: string
  description: string | null
  /** Намерение владельца; у `Pending` кадр всё равно не публичен. */
  isPublished: boolean
  moderationStatus: PhotoModerationStatus
  submittedAt: string
}

export type AdminAvatarPhotosPage = {
  items: AdminAvatarPhoto[]
  nextCursor: string | null
}

export type AdminPortfolioPhotosPage = {
  items: AdminPortfolioPhoto[]
  nextCursor: string | null
}

/** Размер страницы совпадает с дефолтом сервера. */
export const ADMIN_PHOTOS_PAGE_SIZE = 20

/** Максимальная длина поисковой строки на сервере; длиннее он ответит `400`. */
export const ADMIN_PHOTO_SEARCH_MAX_LENGTH = 100

function photosQuery(
  search: string | undefined,
  cursor: string | null | undefined
): string {
  const query = new URLSearchParams({
    pageSize: String(ADMIN_PHOTOS_PAGE_SIZE),
  })

  if (search) {
    query.set('search', search)
  }

  if (cursor) {
    query.set('cursor', cursor)
  }

  return query.toString()
}

/** Очередь аватаров «На проверке», FIFO по времени поступления. */
export async function fetchAdminAvatarPhotos({
  search,
  cursor,
  signal,
}: {
  search?: string
  cursor?: string | null
  signal?: AbortSignal
}): Promise<AdminAvatarPhotosPage> {
  return apiRequest<AdminAvatarPhotosPage>(
    `/admin/photos/avatars?${photosQuery(search, cursor)}`,
    { signal },
    { validate: isAdminAvatarPhotoPage }
  )
}

/** Очередь работ портфолио «На проверке», FIFO по времени поступления. */
export async function fetchAdminPortfolioPhotos({
  search,
  cursor,
  signal,
}: {
  search?: string
  cursor?: string | null
  signal?: AbortSignal
}): Promise<AdminPortfolioPhotosPage> {
  return apiRequest<AdminPortfolioPhotosPage>(
    `/admin/photos/portfolio?${photosQuery(search, cursor)}`,
    { signal },
    { validate: isAdminPortfolioPhotoPage }
  )
}

/** Одобрить аватар: кадр становится публичным, если не заблокирован. */
export async function approveAvatar(userId: string): Promise<void> {
  await mutateWithCsrf<void>(`/admin/users/${userId}/avatar/approval`, {
    method: 'POST',
  })
}

/** Отклонить аватар: причина обязательна и видна владельцу в кабинете. */
export async function rejectAvatar(
  userId: string,
  reason: string
): Promise<void> {
  await mutateWithCsrf<void>(`/admin/users/${userId}/avatar/rejection`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  })
}

/** Одобрить работу портфолио; публичность дальше решают `isPublished`/`isBlocked`. */
export async function approvePortfolioPhoto(
  barberProfileId: string,
  photoId: string
): Promise<void> {
  await mutateWithCsrf<void>(
    `/barber-profiles/${barberProfileId}/photos/${photoId}/approval`,
    { method: 'POST' }
  )
}

/** Отклонить работу портфолио: причина обязательна и видна владельцу. */
export async function rejectPortfolioPhoto(
  barberProfileId: string,
  photoId: string,
  reason: string
): Promise<void> {
  await mutateWithCsrf<void>(
    `/barber-profiles/${barberProfileId}/photos/${photoId}/rejection`,
    { method: 'POST', body: JSON.stringify({ reason }) }
  )
}

const ACTION_FALLBACK = 'Не удалось выполнить действие с фото. Попробуйте ещё раз.'

/**
 * Отказ действия с кадром.
 *
 * Порядок: роль, сессия, исчезнувший владелец/кадр, повторное решение (`409`),
 * причина отказа и устаревший CSRF. Английский `detail` не показывается никогда:
 * неизвестный отказ уходит в русский фолбэк (`0`/`429`/`5xx` разбирает
 * `describeApiFailure`).
 */
export function describePhotoModerationError(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return t(ACTION_FALLBACK)
  }

  if (error.status === 403 || error.hasCode('auth.forbidden')) {
    return t('Раздел доступен только администратору.')
  }

  if (error.status === 401) {
    return t('Сессия истекла — войдите снова.')
  }

  // `user.avatarModeration.noAvatar` — тоже `409`, но это не «уже проверено».
  if (error.hasCode('user.avatarModeration.noAvatar')) {
    return t('У пользователя нет фото — проверять нечего.')
  }

  if (error.hasCode('user.notFound')) {
    return t('Пользователь больше не существует. Обновите список.')
  }

  if (
    error.status === 404 ||
    error.hasCode('barberProfile.notFound') ||
    error.hasCode('portfolioPhoto.moderation.notFound')
  ) {
    return t('Фото больше не существует. Обновите список.')
  }

  if (
    error.status === 409 ||
    error.hasCode('user.avatarModeration.invalidStatus') ||
    error.hasCode('portfolioPhoto.moderation.invalidStatus')
  ) {
    return t('Фото уже проверено. Обновите список.')
  }

  if (
    error.hasCode('user.avatarModeration.reasonRequired') ||
    error.hasCode('portfolioPhoto.moderation.reasonRequired')
  ) {
    return t('Укажите причину отказа.')
  }

  if (
    error.hasCode('user.avatarModeration.reasonTooLong') ||
    error.hasCode('portfolioPhoto.moderation.reasonTooLong')
  ) {
    return t('Слишком длинная причина — максимум 500 символов.')
  }

  if (error.hasCode('auth.csrf.invalid')) {
    return t('Сессия устарела. Обновите страницу и повторите действие.')
  }

  if (error.status === 429) {
    return t('Слишком много действий подряд. Подождите минуту и повторите.')
  }

  return describeApiFailure(error, ACTION_FALLBACK)
}

/**
 * Ошибка чтения очереди: `403` — роль, устаревший курсор — начало списка.
 *
 * `search`/`pageSize` экран ограничивает сам, поэтому их коды здесь не отдельные:
 * неизвестный `400` покажет запасной русский текст экрана.
 */
export function describeAdminPhotoListError(
  error: unknown,
  fallback = 'Не удалось загрузить фотографии на проверке.'
): string {
  if (error instanceof ApiError) {
    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return t('Раздел доступен только администратору.')
    }

    if (error.hasCode('admin.photos.cursor')) {
      return t('Список устарел. Обновите его и продолжите с начала.')
    }
  }

  return describeApiFailure(error, fallback)
}

/** 401 на чтении: экран показывает гейт входа, а не общий текст ошибки. */
export function isAdminPhotoUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401
}
