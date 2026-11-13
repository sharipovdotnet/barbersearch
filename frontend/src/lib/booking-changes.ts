import { t } from '@/i18n'
import { bookingStatusLabel } from '@/lib/booking-status'
import type { BookingStatus } from '@/lib/bookings'
import {
  formatBookingsCount,
  formatShortDay,
  formatShortName,
  formatTime,
} from '@/lib/format'
import type { PageChange } from '@/lib/page-changes'

/**
 * Тексты баннера для автоматического обновления кабинетов.
 *
 * Точность важнее краткости: «отменена» говорится только там, где это следует из
 * данных. Строка, ушедшая из «Предстоящих» уже после начала визита, могла быть и
 * завершена, и отменена, поэтому о ней молчим — её видно в истории.
 *
 * Строки-аргументы описаны минимумом полей: сообщению нужны только момент визита,
 * статус и (барберу) имя клиента. Полные DTO подходят структурно.
 */

/** Строка клиента: время и статус. */
export type BookingChangeRow = {
  id: string
  status: BookingStatus
  startAt: string
}

/** Строка барбера: плюс имя клиента и пояс салона. */
export type BarberBookingChangeRow = BookingChangeRow & {
  customer: { displayName: string }
  salon: { timeZoneId: string }
}

/** «5 нояб. в 14:00» — момент визита в поясе салона. */
function moment(iso: string, timeZoneId: string | undefined): string {
  return t('{date} в {time}', {
    date: formatShortDay(iso, timeZoneId),
    time: formatTime(iso, timeZoneId),
  })
}

/**
 * Что показать барберу после тихого обновления «Ближайших».
 *
 * Изменение статуса не объявляется: его меняет сам барбер своими действиями, и
 * строка уже обновлена локально. Клиент отменить запись может — это и есть повод
 * для баннера.
 */
export function describeBarberBookingsChange(
  change: PageChange<BarberBookingChangeRow>,
  now: number
): string | null {
  const firstAdded = change.added[0]

  if (firstAdded !== undefined) {
    if (change.added.length === 1) {
      return t('Новая запись: {name}, {when}.', {
        name: formatShortName(firstAdded.customer.displayName),
        when: moment(firstAdded.startAt, firstAdded.salon.timeZoneId),
      })
    }

    return t('Появилось {count}.', {
      count: formatBookingsCount(change.added.length),
    })
  }

  // До начала визита запись из «Ближайших» уводит только отмена.
  const cancelled = change.removed.filter(
    (booking) => Date.parse(booking.startAt) > now
  )
  const firstCancelled = cancelled[0]

  if (firstCancelled !== undefined) {
    if (cancelled.length === 1) {
      return t('Запись отменена: {name}, {when}.', {
        name: formatShortName(firstCancelled.customer.displayName),
        when: moment(firstCancelled.startAt, firstCancelled.salon.timeZoneId),
      })
    }

    return t('Отменено {count}.', {
      count: formatBookingsCount(cancelled.length),
    })
  }

  // Собственный перенос барбер применяет локально, поэтому сюда попадает только
  // чужое действие — клиент. Статус при переносе не меняется (D14).
  const rescheduled = change.updated.find(
    ({ before, after }) =>
      before.status === after.status && before.startAt !== after.startAt
  )

  if (rescheduled !== undefined) {
    return t('Клиент перенёс запись на {when}.', {
      when: moment(rescheduled.after.startAt, rescheduled.after.salon.timeZoneId),
    })
  }

  return null
}

/**
 * Что показать клиенту после тихого обновления «Предстоящих».
 *
 * Пояс салона берётся из карточки барбера: сама бронь его не несёт. Если карточка
 * ещё не загрузилась, `formatTime` падает обратно на пояс браузера.
 *
 * Тип строки — параметр: экран передаёт полные брони и свой резолвер пояса, а
 * сообщению достаточно трёх полей.
 */
export function describeCustomerBookingsChange<T extends BookingChangeRow>(
  change: PageChange<T>,
  now: number,
  timeZoneOf: (booking: T) => string | undefined
): string | null {
  const firstAdded = change.added[0]

  if (firstAdded !== undefined) {
    if (change.added.length === 1) {
      return t('Новая запись: {when}.', {
        when: moment(firstAdded.startAt, timeZoneOf(firstAdded)),
      })
    }

    return t('Появилось {count}.', {
      count: formatBookingsCount(change.added.length),
    })
  }

  // Подтверждение — главная новость клиента; о нём говорим отдельной фразой.
  const confirmed = change.updated.find(
    ({ before, after }) =>
      before.status !== 'Confirmed' && after.status === 'Confirmed'
  )

  if (confirmed !== undefined) {
    return t('Барбер подтвердил запись на {when}.', {
      when: moment(confirmed.after.startAt, timeZoneOf(confirmed.after)),
    })
  }

  // Перенос не меняет статус (D14), поэтому узнаётся по одному лишь времени.
  const rescheduled = change.updated.find(
    ({ before, after }) =>
      before.status === after.status && before.startAt !== after.startAt
  )

  if (rescheduled !== undefined) {
    return t('Барбер перенёс запись на {when}.', {
      when: moment(rescheduled.after.startAt, timeZoneOf(rescheduled.after)),
    })
  }

  const statusChange = change.updated.find(
    ({ before, after }) => before.status !== after.status
  )

  if (statusChange !== undefined) {
    return t('Запись на {when}: {status}.', {
      when: moment(statusChange.after.startAt, timeZoneOf(statusChange.after)),
      status: bookingStatusLabel(statusChange.after.status).toLowerCase(),
    })
  }

  // До начала визита из «Предстоящих» запись уводит только отмена.
  const cancelled = change.removed.filter(
    (booking) => Date.parse(booking.startAt) > now
  )
  const firstCancelled = cancelled[0]

  if (firstCancelled !== undefined) {
    if (cancelled.length === 1) {
      return t('Запись на {when} отменена.', {
        when: moment(firstCancelled.startAt, timeZoneOf(firstCancelled)),
      })
    }

    return t('Отменено {count}.', {
      count: formatBookingsCount(cancelled.length),
    })
  }

  return null
}
