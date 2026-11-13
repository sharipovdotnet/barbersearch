import { t } from '@/i18n'
import { ApiError } from '@/lib/api'

/**
 * Единый перевод сетевых сбоев в текст для кабинета.
 *
 * Ответы API приходят как ProblemDetails, поэтому клиентские ветки смотрят на код
 * ошибки, а не на английский `detail` (см. `lib/api.ts`).
 */
export function describeApiFailure(error: unknown, fallback: string): string {
  if (!(error instanceof ApiError)) {
    return t(fallback)
  }

  if (error.status === 0) {
    return t('Нет связи с сервером. Проверьте подключение и повторите попытку.')
  }

  if (error.status === 401) {
    return t('Сессия истекла — войдите снова.')
  }

  if (error.status === 429) {
    return t('Слишком много запросов. Подождите минуту и повторите.')
  }

  return t(fallback)
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
    return { message: t(fallback), canReload: false }
  }

  if (error.hasCode('booking.markNoShow.tooEarly')) {
    return {
      message: t('Неявку нельзя отметить до начала визита.'),
      canReload: false,
    }
  }

  if (error.hasCode('booking.barberNote.tooLong')) {
    return {
      message: t('Слишком длинная заметка — максимум 1000 символов.'),
      canReload: false,
    }
  }

  if (error.hasCode('booking.cancel.reasonRequired')) {
    return { message: t('Укажите причину отмены.'), canReload: false }
  }

  if (error.hasCode('booking.cancel.reasonTooLong')) {
    return {
      message: t('Слишком длинная причина — максимум 500 символов.'),
      canReload: false,
    }
  }

  // Цена визита: те же правила, что у домена. Сумма — деньги, поэтому текст
  // говорит, что именно не так, а не «неверный запрос».
  if (error.hasCode('booking.price.required')) {
    return { message: t('Укажите цену.'), canReload: false }
  }

  if (error.hasCode('booking.price.negative')) {
    return { message: t('Цена не может быть отрицательной.'), canReload: false }
  }

  if (error.hasCode('booking.price.tooLarge')) {
    return {
      message: t('Слишком большая цена — максимум 99 999 999,99 ₽.'),
      canReload: false,
    }
  }

  if (error.hasCode('booking.price.precision')) {
    return {
      message: t('Цена — не больше двух знаков после запятой.'),
      canReload: false,
    }
  }

  if (error.hasCode('booking.price.status')) {
    return {
      message: t('Цену нельзя изменить у отменённой записи или у неявки.'),
      canReload: false,
    }
  }

  if (error.status === 404 || error.hasCode('booking.notFound')) {
    return { message: t('Запись не найдена.'), canReload: false }
  }

  if (error.status === 403 || error.hasCode('auth.forbidden')) {
    return { message: t('Это запись другого барбера.'), canReload: false }
  }

  if (error.status === 409) {
    return {
      message: t('Запись уже изменилась — обновите список.'),
      canReload: true,
    }
  }

  return { message: describeApiFailure(error, fallback), canReload: false }
}

export type RescheduleFailure = {
  message: string
  /** Перезапросить сетку: время заняли, слот исчез или запись устарела. */
  reloadSlots: boolean
}

/**
 * Ошибки переноса визита — общий маппер двух кабинетов.
 *
 * Коды важнее статуса: `booking.reschedule.overlap` и остаточная гонка
 * `booking.update.overlap` значат одно — время занято; `booking.reschedule.slot`
 * — сетка устарела; `booking.update.concurrencyConflict` — строку изменили
 * параллельно. `detail` не показываем: он английский.
 */
export function describeRescheduleFailure(
  error: unknown,
  fallback: string
): RescheduleFailure {
  if (!(error instanceof ApiError)) {
    return { message: t(fallback), reloadSlots: false }
  }

  if (
    error.hasCode('booking.reschedule.overlap') ||
    error.hasCode('booking.update.overlap')
  ) {
    return {
      message: t('Это время уже занято. Выберите другое.'),
      reloadSlots: true,
    }
  }

  if (error.hasCode('booking.reschedule.slot')) {
    return {
      message: t('Это время больше не доступно. Выберите другое.'),
      reloadSlots: true,
    }
  }

  if (error.hasCode('booking.reschedule.startAt.inPast')) {
    return {
      message: t('Это время уже прошло. Выберите другое.'),
      reloadSlots: true,
    }
  }

  if (error.hasCode('booking.reschedule.startAt.tooFar')) {
    return {
      message: t('Слишком далеко в будущем.'),
      reloadSlots: false,
    }
  }

  if (error.hasCode('booking.reschedule.status')) {
    return {
      message: t('Запись уже нельзя перенести — обновите страницу.'),
      reloadSlots: false,
    }
  }

  if (error.hasCode('booking.reschedule.lockTimeout')) {
    return {
      message: t('Не удалось перенести: попробуйте ещё раз.'),
      reloadSlots: true,
    }
  }

  if (error.hasCode('booking.update.concurrencyConflict')) {
    return {
      message: t('Запись изменилась — обновите страницу.'),
      reloadSlots: true,
    }
  }

  if (error.status === 404 || error.hasCode('booking.notFound')) {
    return { message: t('Запись не найдена.'), reloadSlots: false }
  }

  if (error.status === 403) {
    return { message: t('Это запись другого клиента.'), reloadSlots: false }
  }

  return { message: describeApiFailure(error, fallback), reloadSlots: false }
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
    return { message: t(fallback), reloadSlots: false }
  }

  if (error.status === 401) {
    return {
      message: t('Сессия истекла — войдите снова.'),
      reloadSlots: false,
    }
  }

  if (error.status === 403) {
    return {
      message: t(
        'Нет доступа к записи клиентов. Войдите под аккаунтом барбера.'
      ),
      reloadSlots: false,
    }
  }

  if (error.hasCode('booking.create.customerNotFound')) {
    return {
      message: t(
        'Пользователь не найден — возможно, аккаунт заблокирован. Повторите поиск.'
      ),
      reloadSlots: false,
    }
  }

  if (error.hasCode('booking.create.customerAmbiguous')) {
    return {
      message: t(
        'Выберите зарегистрированного клиента или заполните данные нового — не оба варианта сразу.'
      ),
      reloadSlots: false,
    }
  }

  if (
    error.hasCode('booking.create.customer')
    || error.hasCode('booking.create.guestFirstName')
    || error.hasCode('booking.create.guestPhone')
  ) {
    return {
      message: t('Укажите имя и телефон клиента.'),
      reloadSlots: false,
    }
  }

  if (error.hasCode('booking.add.overlap')) {
    return {
      message: t('Это время только что заняли. Выберите другое.'),
      reloadSlots: true,
    }
  }

  if (
    error.hasCode('booking.create.startAt.inPast')
    || error.hasCode('booking.create.startAt.tooFar')
  ) {
    return {
      message: t('Это время уже недоступно. Обновите свободное время.'),
      reloadSlots: true,
    }
  }

  if (error.status === 404) {
    return {
      message: t('Услуга или салон больше недоступны. Выберите другой вариант.'),
      reloadSlots: true,
    }
  }

  if (error.hasCode('booking.add.lockTimeout')) {
    return {
      message: t('Не удалось занять время, попробуйте ещё раз.'),
      reloadSlots: false,
    }
  }

  return { message: describeApiFailure(error, fallback), reloadSlots: false }
}
