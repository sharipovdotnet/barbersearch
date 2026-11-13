import { describe, expect, it } from 'vitest'

import {
  describeBarberBookingsChange,
  describeCustomerBookingsChange,
  type BarberBookingChangeRow,
  type BookingChangeRow,
} from '@/lib/booking-changes'

/**
 * Тексты баннера — это копия: строки сравниваются целиком и могут меняться вместе
 * с продуктом. Даты проверяются в поясе Europe/Moscow, чтобы «12:00» не зависело
 * от часового пояса машины.
 */

const NOW = Date.parse('2026-11-05T10:00:00Z')
const FUTURE = '2026-11-06T09:00:00Z'
const PAST = '2026-11-05T06:00:00Z'

const row = (
  id: string,
  startAt: string,
  status: BookingChangeRow['status'] = 'Pending'
): BookingChangeRow => ({ id, startAt, status })

const barberRow = (
  id: string,
  startAt: string,
  displayName = 'Анна Петрова'
): BarberBookingChangeRow => ({
  ...row(id, startAt),
  customer: { displayName },
  salon: { timeZoneId: 'Europe/Moscow' },
})

const moscow = (): string => 'Europe/Moscow'

function emptyChange<T>() {
  return { added: [] as T[], removed: [] as T[], updated: [] as { before: T; after: T }[] }
}

describe('describeBarberBookingsChange', () => {
  it('называет клиента и время новой записи', () => {
    expect(
      describeBarberBookingsChange(
        { ...emptyChange<BarberBookingChangeRow>(), added: [barberRow('new', FUTURE)] },
        NOW
      )
    ).toBe('Новая запись: Анна П., 6 нояб. в 12:00.')
  })

  it('на несколько записей считает их, а не перечисляет', () => {
    expect(
      describeBarberBookingsChange(
        {
          ...emptyChange<BarberBookingChangeRow>(),
          added: [barberRow('a', FUTURE), barberRow('b', FUTURE)],
        },
        NOW
      )
    ).toBe('Появилось 2 записи.')
  })

  it('сообщает об отмене записи, которая ещё не началась', () => {
    expect(
      describeBarberBookingsChange(
        { ...emptyChange<BarberBookingChangeRow>(), removed: [barberRow('gone', FUTURE)] },
        NOW
      )
    ).toBe('Запись отменена: Анна П., 6 нояб. в 12:00.')
  })

  it('молчит о строке, ушедшей после начала визита: это могло быть завершение', () => {
    expect(
      describeBarberBookingsChange(
        { ...emptyChange<BarberBookingChangeRow>(), removed: [barberRow('done', PAST)] },
        NOW
      )
    ).toBeNull()
  })

  it('объявляет перенос клиентом: время изменилось, статус тот же', () => {
    expect(
      describeBarberBookingsChange(
        {
          ...emptyChange<BarberBookingChangeRow>(),
          updated: [
            {
              before: barberRow('moved', FUTURE),
              after: barberRow('moved', '2026-11-07T09:00:00Z'),
            },
          ],
        },
        NOW
      )
    ).toBe('Клиент перенёс запись на 7 нояб. в 12:00.')
  })

  it('не объявляет собственный перенос: локальная правка не создаёт diff', () => {
    expect(
      describeBarberBookingsChange(emptyChange<BarberBookingChangeRow>(), NOW)
    ).toBeNull()
  })
})

describe('describeCustomerBookingsChange', () => {
  it('называет время новой записи', () => {
    expect(
      describeCustomerBookingsChange(
        { ...emptyChange<BookingChangeRow>(), added: [row('new', FUTURE)] },
        NOW,
        moscow
      )
    ).toBe('Новая запись: 6 нояб. в 12:00.')
  })

  it('объявляет подтверждение отдельной фразой', () => {
    expect(
      describeCustomerBookingsChange(
        {
          ...emptyChange<BookingChangeRow>(),
          updated: [
            { before: row('b', FUTURE, 'Pending'), after: row('b', FUTURE, 'Confirmed') },
          ],
        },
        NOW,
        moscow
      )
    ).toBe('Барбер подтвердил запись на 6 нояб. в 12:00.')
  })

  it('подтверждение приоритетнее переноса', () => {
    expect(
      describeCustomerBookingsChange(
        {
          ...emptyChange<BookingChangeRow>(),
          updated: [
            {
              before: row('moved', FUTURE, 'Confirmed'),
              after: row('moved', '2026-11-07T09:00:00Z', 'Confirmed'),
            },
            {
              before: row('b', FUTURE, 'Pending'),
              after: row('b', FUTURE, 'Confirmed'),
            },
          ],
        },
        NOW,
        moscow
      )
    ).toBe('Барбер подтвердил запись на 6 нояб. в 12:00.')
  })

  it('объявляет перенос барбером: время изменилось, статус тот же', () => {
    expect(
      describeCustomerBookingsChange(
        {
          ...emptyChange<BookingChangeRow>(),
          updated: [
            {
              before: row('moved', FUTURE, 'Confirmed'),
              after: row('moved', '2026-11-07T09:00:00Z', 'Confirmed'),
            },
          ],
        },
        NOW,
        moscow
      )
    ).toBe('Барбер перенёс запись на 7 нояб. в 12:00.')
  })

  it('о собственном переносе клиента молчит: локальная правка не создаёт diff', () => {
    expect(
      describeCustomerBookingsChange(
        emptyChange<BookingChangeRow>(),
        NOW,
        moscow
      )
    ).toBeNull()
  })

  it('о прочих статусах говорит подписью статуса', () => {
    expect(
      describeCustomerBookingsChange(
        {
          ...emptyChange<BookingChangeRow>(),
          updated: [
            { before: row('b', FUTURE, 'Confirmed'), after: row('b', FUTURE, 'NoShow') },
          ],
        },
        NOW,
        moscow
      )
    ).toBe('Запись на 6 нояб. в 12:00: неявка.')
  })

  it('сообщает об отмене будущей записи и молчит о прошедшей', () => {
    expect(
      describeCustomerBookingsChange(
        { ...emptyChange<BookingChangeRow>(), removed: [row('gone', FUTURE)] },
        NOW,
        moscow
      )
    ).toBe('Запись на 6 нояб. в 12:00 отменена.')

    expect(
      describeCustomerBookingsChange(
        { ...emptyChange<BookingChangeRow>(), removed: [row('done', PAST)] },
        NOW,
        moscow
      )
    ).toBeNull()
  })
})
