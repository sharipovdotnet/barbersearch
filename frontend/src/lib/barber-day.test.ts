import { describe, expect, it } from 'vitest'

import {
  findDayGaps,
  findNextBooking,
  isSameLocalDay,
  mergeDayBookings,
  MIN_VISIBLE_GAP_MINUTES,
} from '@/lib/barber-day'
import type { BarberBooking } from '@/lib/barber-bookings'

/**
 * Лента дня: фильтр по локальной дате, слияние двух скоупов и окна между визитами.
 *
 * Даты строятся в поясе браузера, а не литералами UTC: «сегодня» — это календарный
 * день пользователя, и тест не должен зависеть от пояса машины, на которой идёт.
 */

const NOW = new Date(2026, 10, 8, 12, 0, 0, 0).getTime()

/** Местное время 8 ноября 2026 (день, сдвинутый на `dayOffset`). */
function at(hours: number, minutes: number, dayOffset = 0): string {
  const date = new Date(2026, 10, 8, hours, minutes, 0, 0)
  date.setDate(date.getDate() + dayOffset)

  return date.toISOString()
}

function booking(
  id: string,
  startAt: string,
  endAt: string,
  status: BarberBooking['status'] = 'Confirmed'
): BarberBooking {
  return {
    id,
    status,
    startAt,
    endAt,
    price: 2000,
    customerNote: null,
    barberNote: null,
    cancellationReason: null,
    customer: { displayName: 'Анна Петрова', phoneNumber: null },
    service: { name: 'Стрижка' },
    salon: {
      name: 'Салон на Ленина',
      address: 'ул. Ленина, 1',
      city: 'Екатеринбург',
      timeZoneId: 'Asia/Yekaterinburg',
    },
  }
}

describe('barber-day: локальная дата', () => {
  it('23:59 сегодня — тот же день, 00:01 завтра — уже другой', () => {
    expect(isSameLocalDay(at(23, 59), NOW)).toBe(true)
    expect(isSameLocalDay(at(0, 1, 1), NOW)).toBe(false)
    expect(isSameLocalDay(at(0, 1), NOW)).toBe(true)
    expect(isSameLocalDay(at(23, 59, -1), NOW)).toBe(false)
  })
})

describe('barber-day: слияние дня', () => {
  it('берёт только сегодняшние строки из обоих списков', () => {
    const upcoming = [
      booking('a', at(10, 0), at(10, 30)),
      booking('tomorrow', at(10, 0, 1), at(10, 30, 1)),
    ]
    const past = [booking('b', at(9, 0), at(9, 30), 'Completed')]

    expect(mergeDayBookings(upcoming, past, NOW).map((item) => item.id)).toStrictEqual([
      'b',
      'a',
    ])
  })

  it('пустые списки — пустой день', () => {
    expect(mergeDayBookings([], [], NOW)).toStrictEqual([])
  })

  it('дубликат по id решается в пользу upcoming', () => {
    const upcoming = [booking('same', at(10, 0), at(10, 30), 'Confirmed')]
    const past = [booking('same', at(10, 0), at(10, 30), 'Pending')]

    const day = mergeDayBookings(upcoming, past, NOW)

    expect(day).toHaveLength(1)
    expect(day[0].status).toBe('Confirmed')
  })

  it('upcoming по возрастанию и past по убыванию дают общий возрастающий порядок', () => {
    const upcoming = [
      booking('u1', at(10, 0), at(10, 30)),
      booking('u2', at(12, 0), at(12, 30)),
      booking('u3', at(18, 0), at(18, 30)),
    ]
    const past = [
      booking('p2', at(16, 0), at(16, 30), 'Completed'),
      booking('p1', at(9, 0), at(9, 30), 'Completed'),
    ]

    expect(mergeDayBookings(upcoming, past, NOW).map((item) => item.id)).toStrictEqual([
      'p1',
      'u1',
      'u2',
      'p2',
      'u3',
    ])
  })

  it('не мутирует входные массивы', () => {
    const upcoming = [booking('u1', at(12, 0), at(12, 30)), booking('u2', at(10, 0), at(10, 30))]
    const past: BarberBooking[] = []

    mergeDayBookings(upcoming, past, NOW)

    expect(upcoming.map((item) => item.id)).toStrictEqual(['u1', 'u2'])
    expect(past).toStrictEqual([])
  })

  it('одинаковое начало сортируется по id', () => {
    const day = mergeDayBookings(
      [booking('b', at(10, 0), at(10, 30)), booking('a', at(10, 0), at(10, 30))],
      [],
      NOW
    )

    expect(day.map((item) => item.id)).toStrictEqual(['a', 'b'])
  })
})

describe('barber-day: окна', () => {
  it('окно ровно 20 минут показывается, 19 минут — нет', () => {
    const twenty = [
      booking('a', at(10, 0), at(10, 30)),
      booking('b', at(10, 50), at(11, 20)),
    ]
    const nineteen = [
      booking('a', at(10, 0), at(10, 30)),
      booking('b', at(10, 49), at(11, 20)),
    ]

    expect(findDayGaps(twenty)).toStrictEqual([
      { startAt: at(10, 30), endAt: at(10, 50), minutes: MIN_VISIBLE_GAP_MINUTES },
    ])
    expect(findDayGaps(nineteen)).toStrictEqual([])
  })

  it('три записи дают два окна, первая и последняя границ не создают', () => {
    const gaps = findDayGaps([
      booking('a', at(9, 0), at(9, 30)),
      booking('b', at(10, 0), at(10, 30)),
      booking('c', at(11, 40), at(12, 10)),
    ])

    expect(gaps).toStrictEqual([
      { startAt: at(9, 30), endAt: at(10, 0), minutes: 30 },
      { startAt: at(10, 30), endAt: at(11, 40), minutes: 70 },
    ])
  })

  it('пересечение записей окна не даёт', () => {
    expect(
      findDayGaps([
        booking('long', at(9, 0), at(13, 0)),
        booking('inside', at(10, 0), at(10, 30)),
      ])
    ).toStrictEqual([])
  })

  it('пустой и одиночный день окон не имеют', () => {
    expect(findDayGaps([])).toStrictEqual([])
    expect(findDayGaps([booking('a', at(10, 0), at(10, 30))])).toStrictEqual([])
  })
})

describe('barber-day: следующая запись', () => {
  it('идущий визит — следующая запись', () => {
    const day = [
      booking('done', at(9, 0), at(9, 30), 'Completed'),
      booking('now', at(11, 30), at(12, 30), 'Confirmed'),
      booking('later', at(15, 0), at(15, 30), 'Pending'),
    ]

    expect(findNextBooking(day, NOW)?.id).toBe('now')
  })

  it('все визиты завершены — null', () => {
    expect(
      findNextBooking([booking('done', at(9, 0), at(9, 30), 'Completed')], NOW)
    ).toBeNull()
  })

  it('пустой день — null', () => {
    expect(findNextBooking([], NOW)).toBeNull()
  })

  it('первая по началу строка с концом в будущем — следующая, статус не важен', () => {
    const day = [
      booking('cancelled', at(13, 0), at(13, 30), 'Cancelled'),
      booking('later', at(18, 0), at(18, 30), 'Confirmed'),
    ]

    expect(findNextBooking(day, NOW)?.id).toBe('cancelled')
  })
})
