import { describe, expect, it } from 'vitest'

import {
  describeBarberBookingsChange,
  describeCustomerBookingsChange,
  type BarberBookingChangeRow,
  type BookingChangeRow,
} from '@/lib/booking-changes'
import { formatPrice } from '@/lib/format'

/**
 * Тексты баннера — это копия: строки сравниваются целиком и могут меняться вместе
 * с продуктом. Даты проверяются в поясе Europe/Moscow, чтобы «12:00» не зависело
 * от часового пояса машины; сумма собирается `formatPrice`, чтобы тест не зависел
 * от вида пробела между разрядами.
 */

const NOW = Date.parse('2026-11-05T10:00:00Z')
const FUTURE = '2026-11-06T09:00:00Z'
const PAST = '2026-11-05T06:00:00Z'

const row = (
  id: string,
  startAt: string,
  status: BookingChangeRow['status'] = 'Pending',
  price = 1500
): BookingChangeRow => ({ id, startAt, status, price })

const barberRow = (
  id: string,
  startAt: string,
  displayName = 'Анна Петрова',
  price = 1500
): BarberBookingChangeRow => ({
  ...row(id, startAt, 'Pending', price),
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

  it('объявляет правку цены администратором', () => {
    expect(
      describeBarberBookingsChange(
        {
          ...emptyChange<BarberBookingChangeRow>(),
          updated: [
            {
              before: barberRow('priced', FUTURE, 'Анна Петрова', 1500),
              after: barberRow('priced', FUTURE, 'Анна Петрова', 2000),
            },
          ],
        },
        NOW
      )
    ).toBe(
      `Администратор изменил цену визита (Анна П., 6 нояб. в 12:00): ${formatPrice(1500)} → ${formatPrice(2000)}.`
    )
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

  it('объявляет правку цены барбером', () => {
    expect(
      describeCustomerBookingsChange(
        {
          ...emptyChange<BookingChangeRow>(),
          updated: [
            {
              before: row('priced', FUTURE, 'Confirmed', 1500),
              after: row('priced', FUTURE, 'Confirmed', 2000),
            },
          ],
        },
        NOW,
        moscow
      )
    ).toBe(
      `Барбер изменил цену визита 6 нояб. в 12:00: ${formatPrice(1500)} → ${formatPrice(2000)}.`
    )
  })

  it('цена приоритетнее смены статуса, но ниже подтверждения и переноса', () => {
    // Строка изменила и сумму, и статус: новость про деньги — первая (D14).
    expect(
      describeCustomerBookingsChange(
        {
          ...emptyChange<BookingChangeRow>(),
          updated: [
            {
              before: row('both', FUTURE, 'Confirmed', 1500),
              after: row('both', FUTURE, 'NoShow', 2000),
            },
          ],
        },
        NOW,
        moscow
      )
    ).toBe(
      `Барбер изменил цену визита 6 нояб. в 12:00: ${formatPrice(1500)} → ${formatPrice(2000)}.`
    )

    // Перенос важнее цены: он меняет само время визита.
    expect(
      describeCustomerBookingsChange(
        {
          ...emptyChange<BookingChangeRow>(),
          updated: [
            {
              before: row('moved', FUTURE, 'Confirmed', 1500),
              after: row('moved', '2026-11-07T09:00:00Z', 'Confirmed', 2000),
            },
          ],
        },
        NOW,
        moscow
      )
    ).toBe('Барбер перенёс запись на 7 нояб. в 12:00.')
  })
})
