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

  if (error.hasCode('booking.cancel.reasonRequired')) {
    return { message: 'Укажите причину отмены.', canReload: false }
  }

  if (error.hasCode('booking.cancel.reasonTooLong')) {
    return {
      message: 'Слишком длинная причина — максимум 500 символов.',
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

export type BarberBookingCreateFailure = {
  message: string
  /** Перезапросить слоты: время заняли, услуга/салон исчезли или старт вне окна. */
  reloadSlots: boolean
}

/**
 * Ошибки `POST api/barber/bookings`.
 *
 * Барбер записывает зарегистрированного пользователя или нового человека без
 * аккаунта; `404 booking.create.customerNotFound` означает, что пользователь
 * исчез или заблокирован, а не что «страница не найдена», а
 * `booking.create.guest*` — что не заполнены обязательные имя и телефон гостя.
 * `409 booking.add.overlap` и `400 booking.create.startAt.*` лечатся только новым
 * свободным временем, поэтому просим форму перезапросить слоты. `detail` не
 * показываем никогда, он английский.
 */
export function describeBarberBookingCreateFailure(
  error: unknown,
  fallback: string
): BarberBookingCreateFailure {
  if (!(error instanceof ApiError)) {
    return { message: fallback, reloadSlots: false }
  }

  if (error.status === 401) {
    return { message: 'Сессия истекла — войдите снова.', reloadSlots: false }
  }

  if (error.status === 403) {
    return {
      message: 'Нет доступа к записи клиентов. Войдите под аккаунтом барбера.',
      reloadSlots: false,
    }
  }

  if (error.hasCode('booking.create.customerNotFound')) {
    return {
      message:
        'Пользователь не найден — возможно, аккаунт заблокирован. Повторите поиск.',
      reloadSlots: false,
    }
  }

  if (error.hasCode('booking.create.customerAmbiguous')) {
    return {
      message:
        'Выберите зарегистрированного клиента или заполните данные нового — не оба варианта сразу.',
      reloadSlots: false,
    }
  }

  if (
    error.hasCode('booking.create.customer')
    || error.hasCode('booking.create.guestFirstName')
    || error.hasCode('booking.create.guestPhone')
  ) {
    return {
      message: 'Укажите имя и телефон клиента.',
      reloadSlots: false,
    }
  }

  if (error.hasCode('booking.add.overlap')) {
    return {
      message: 'Это время только что заняли. Выберите другое.',
      reloadSlots: true,
    }
  }

  if (
    error.hasCode('booking.create.startAt.inPast')
    || error.hasCode('booking.create.startAt.tooFar')
  ) {
    return {
      message: 'Это время уже недоступно. Обновите свободное время.',
      reloadSlots: true,
    }
  }

  if (error.status === 404) {
    return {
      message: 'Услуга или салон больше недоступны. Выберите другой вариант.',
      reloadSlots: true,
    }
  }

  if (error.hasCode('booking.add.lockTimeout')) {
    return {
      message: 'Не удалось занять время, попробуйте ещё раз.',
      reloadSlots: false,
    }
  }

  return { message: describeApiFailure(error, fallback), reloadSlots: false }
}
