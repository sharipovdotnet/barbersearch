import { t } from '@/i18n'
import { ApiError } from '@/lib/api'

/**
 * Скрытие и возврат аватара администратором.
 *
 * Аватар — поле `users`, а не отдельная сущность, поэтому обе админские карточки
 * (пользователь и барбер) ходят в один и тот же эндпоинт
 * `POST|DELETE api/admin/users/{userId}/avatar/block` и делят тексты ошибок.
 * Держать их в двух `describe…ActionError` нельзя: карточки разошлись бы по
 * формулировкам одного и того же отказа.
 */

export type AvatarModerationAction = 'blockAvatar' | 'unblockAvatar'

/**
 * Отказ скрытия или возврата аватара.
 *
 * Порядок — как у остальных мутаций: роль, сессия, исчезнувший пользователь,
 * затем доменные коды (`user.blockAvatar.noAvatar`), устаревший CSRF и лимит.
 * Английский `detail` не показывается: неизвестный отказ получает русский фолбэк.
 */
export function describeAvatarModerationError(
  error: unknown,
  action: AvatarModerationAction
): string {
  const fallback =
    action === 'blockAvatar'
      ? t('Не удалось скрыть фото. Попробуйте ещё раз.')
      : t('Не удалось вернуть фото. Попробуйте ещё раз.')

  if (!(error instanceof ApiError)) {
    return fallback
  }

  if (error.status === 403 || error.hasCode('auth.forbidden')) {
    return t('Раздел доступен только администратору.')
  }

  if (error.status === 401) {
    return t('Сессия истекла — войдите снова.')
  }

  // `user.notFound` — контракт админских мутаций аватара; `404` покрывает и его.
  if (error.status === 404 || error.hasCode('user.notFound')) {
    return t('Пользователь больше не существует. Обновите список.')
  }

  if (error.hasCode('user.blockAvatar.noAvatar')) {
    return t('У пользователя нет фото — скрывать нечего.')
  }

  if (error.hasCode('auth.csrf.invalid')) {
    return t('Сессия устарела. Обновите страницу и повторите действие.')
  }

  if (error.status === 429) {
    return t('Слишком много действий подряд. Подождите минуту и повторите.')
  }

  return fallback
}
