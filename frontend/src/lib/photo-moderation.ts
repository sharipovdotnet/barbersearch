import { t } from '@/i18n'

/**
 * Премодерация фотографий: аватар пользователя и работа в портфолио.
 *
 * Статус — один на оба агрегата и приходит из JSON именем, а не числом: числа
 * (`Pending = 1`, …) остаются в базе, клиент видит строку. `Approved` — допуск
 * к публикации, но не сама публичность: у портфолио остаются `isPublished`
 * и `isBlocked`, у аватара — `isAvatarBlocked`, поэтому видимость решают три
 * условия сразу. `Rejected` и блокировка — разные вещи: первое — решение о новом
 * кадре, второе — действие над уже опубликованным.
 */

export type PhotoModerationStatus = 'Pending' | 'Approved' | 'Rejected'

export const PHOTO_MODERATION_STATUSES: readonly PhotoModerationStatus[] = [
  'Pending',
  'Approved',
  'Rejected',
]

/** Подписи для бейджей и карточек: статус всегда словом, не только цветом. */
export const PHOTO_MODERATION_LABELS: Record<PhotoModerationStatus, string> = {
  Pending: 'На проверке',
  Approved: 'Одобрено',
  Rejected: 'Отклонено',
}

/** Предел причины отказа: тот же, что в домене; длиннее сервер ответит `400`. */
export const PHOTO_REJECTION_MAX_LENGTH = 500

export function isPhotoModerationStatus(
  value: unknown
): value is PhotoModerationStatus {
  return value === 'Pending' || value === 'Approved' || value === 'Rejected'
}

/**
 * Подпись статуса. Ключ словаря — русский исходный текст, перевод берётся в `t()`
 * в момент вывода: вызывать `t()` на уровне модуля нельзя, локаль ещё неизвестна.
 */
export function photoModerationLabel(status: PhotoModerationStatus): string {
  return t(PHOTO_MODERATION_LABELS[status] ?? status)
}

/**
 * Решение администратора доступно только кадру «На проверке».
 *
 * Повторное решение домен отклоняет `409`: у `Approved` и `Rejected` кнопок
 * «Одобрить»/«Отклонить» быть не должно.
 */
export function canReviewPhoto(status: PhotoModerationStatus): boolean {
  return status === 'Pending'
}

/** Блокировать имеет смысл только одобренный кадр: у остальных он и так непубличен. */
export function canBlockPhoto(status: PhotoModerationStatus): boolean {
  return status === 'Approved'
}

/**
 * Можно ли рендерить превью аватара в шапке и блоке личности.
 *
 * `Pending` и `Approved` — да: новый кадр виден только владельцу, пока ждёт
 * проверки. `Rejected` — нет, вместо него инициалы: отклонённый кадр в шапке
 * не показываем. Заблокированный администратором кадр не виден даже владельцу
 * (§87), а без URL рендерить нечего.
 */
export function canRenderAvatarPreview(avatar: {
  avatarUrl: string | null
  isAvatarBlocked: boolean
  avatarModerationStatus: PhotoModerationStatus
}): boolean {
  return (
    avatar.avatarUrl !== null &&
    !avatar.isAvatarBlocked &&
    avatar.avatarModerationStatus !== 'Rejected'
  )
}

/**
 * Превью в карточке «Аватар» кабинета.
 *
 * В кабинете владелец видит и отклонённый кадр — рядом бейдж «Отклонено»
 * и причина отказа, чтобы было понятно, что именно не прошло проверку.
 * Заблокированный кадр не рендерится и здесь: файл ему не отдаётся.
 */
export function canRenderAvatarCardPreview(avatar: {
  avatarUrl: string | null
  isAvatarBlocked: boolean
}): boolean {
  return avatar.avatarUrl !== null && !avatar.isAvatarBlocked
}

/**
 * Клиентская проверка причины отказа: пустую строку сервер отклонит, длинную —
 * тоже. Возвращает русский текст ошибки или `null`, если причина корректна.
 */
export function validatePhotoRejectionReason(reason: string): string | null {
  const trimmed = reason.trim()

  if (trimmed === '') {
    return t('Укажите причину отказа.')
  }

  if (trimmed.length > PHOTO_REJECTION_MAX_LENGTH) {
    return t('Слишком длинная причина — максимум {max} символов.', {
      max: PHOTO_REJECTION_MAX_LENGTH,
    })
  }

  return null
}
