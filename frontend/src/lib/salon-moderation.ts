/**
 * Состояние проверки салона администратором.
 *
 * Это не `isActive`: видимость в поиске — отдельный флаг, а салон участвует в витрине
 * только когда он одобрен **и** включён. Значения приходят из JSON именами, поэтому
 * тип — строковый союз, а не число.
 */

import { t } from '@/i18n'

export type SalonModerationStatus = 'Pending' | 'Active' | 'Rejected'

export const SALON_MODERATION_STATUSES: readonly SalonModerationStatus[] = [
  'Pending',
  'Active',
  'Rejected',
]

/** Подписи для бейджей и карточек: статус всегда словом, не только цветом. */
export const SALON_MODERATION_LABELS: Record<SalonModerationStatus, string> = {
  Active: 'Одобрен',
  Pending: 'На проверке',
  Rejected: 'Отклонён',
}

/** Максимальная длина причины отказа на сервере; длиннее он ответит `400`. */
export const SALON_REJECTION_MAX_LENGTH = 500

export function isSalonModerationStatus(
  value: unknown
): value is SalonModerationStatus {
  return (
    value === 'Pending' || value === 'Active' || value === 'Rejected'
  )
}

/**
 * Клиентская проверка причины отказа: пустую строку сервер отклонит, длинную —
 * тоже. Возвращает русский текст ошибки или `null`, если причина корректна.
 */
export function validateSalonRejectionReason(reason: string): string | null {
  const trimmed = reason.trim()

  if (trimmed === '') {
    return t('Укажите причину отказа.')
  }

  if (trimmed.length > SALON_REJECTION_MAX_LENGTH) {
    return t('Слишком длинная причина — максимум {max} символов.', {
      max: SALON_REJECTION_MAX_LENGTH,
    })
  }

  return null
}
