import { ApiError } from '@/lib/api'

/**
 * Единый перевод сетевых сбоев в текст для кабинета.
 *
 * Ответы API приходят как ProblemDetails, поэтому клиентские ветки смотрят на код
 * ошибки, а не на английский `detail` (см. `lib/api.ts`).
 */
export function describeApiFailure(error: unknown, fallback: string): string {
  if (!(error instanceof ApiError)) {
    return fallback
  }

  if (error.status === 0) {
    return 'Нет связи с сервером. Проверьте подключение и повторите попытку.'
  }

  if (error.status === 401) {
    return 'Сессия истекла — войдите снова.'
  }

  if (error.status === 429) {
    return 'Слишком много запросов. Подождите минуту и повторите.'
  }

  return fallback
}

export type BarberActionFailure = {
  message: string
  /** `409`: запись уже изменилась, помочь может только повторное чтение. */
  canReload: boolean
}

/**
 * Ошибки действий барбера с записью.
 *
 * Коды важнее статуса: `409` на любой из пяти мутаций значит одно — строка устарела.
 * `detail` не показываем никогда, он английский.
 */
export function describeBarberBookingFailure(
  error: unknown,
  fallback: string
): BarberActionFailure {
  if (!(error instanceof ApiError)) {
    return { message: fallback, canReload: false }
  }

  if (error.hasCode('booking.markNoShow.tooEarly')) {
    return {
      message: 'Неявку нельзя отметить до начала визита.',
      canReload: false,
    }
  }

  if (error.hasCode('booking.barberNote.tooLong')) {
    return {
      message: 'Слишком длинная заметка — максимум 1000 символов.',
      canReload: false,
    }
  }

  if (error.status === 404 || error.hasCode('booking.notFound')) {
    return { message: 'Запись не найдена.', canReload: false }
  }

  if (error.status === 403 || error.hasCode('auth.forbidden')) {
    return { message: 'Это запись другого барбера.', canReload: false }
  }

  if (error.status === 409) {
    return {
      message: 'Запись уже изменилась — обновите список.',
      canReload: true,
    }
  }

  return { message: describeApiFailure(error, fallback), canReload: false }
}
