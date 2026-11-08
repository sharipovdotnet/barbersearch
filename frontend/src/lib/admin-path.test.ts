import { describe, expect, it } from 'vitest'

import {
  barberRequestsPath,
  barbersPath,
  bookingsPath,
  isBarberRequestId,
  isBookingId,
  parseAdminPath,
} from '@/lib/admin-path'

/**
 * Маршрут броней: `/admin/bookings` — список, `/admin/bookings/{id}` — карточка.
 *
 * Раньше оба адреса попадали в `notFound`; тест фиксирует, что раздел стал
 * настоящим маршрутом, а не «Раздел не найден».
 */

const BOOKING_ID = '9e900000-0000-4000-8000-000000000001'
const BARBER_ID = '9e100000-0000-4000-8000-000000000001'

describe('parseAdminPath: брони', () => {
  it('список броней больше не notFound', () => {
    expect(parseAdminPath('/admin/bookings', '')).toStrictEqual({
      kind: 'bookings',
      bookingId: null,
    })
  })

  it('карточка брони разбирает id из пути', () => {
    expect(
      parseAdminPath(`/admin/bookings/${BOOKING_ID}`, '')
    ).toStrictEqual({
      kind: 'bookings',
      bookingId: BOOKING_ID,
    })
  })

  it('лишний вложенный путь остаётся notFound', () => {
    expect(parseAdminPath('/admin/bookings/a/b', '')).toStrictEqual({
      kind: 'notFound',
    })
  })

  it('фильтры броней не являются частью маршрута', () => {
    const route = parseAdminPath('/admin/bookings', '?status=Pending&search=e2e')

    expect(route).toStrictEqual({ kind: 'bookings', bookingId: null })
  })
})

describe('bookingsPath', () => {
  it('без id ведёт к списку', () => {
    expect(bookingsPath()).toBe('/admin/bookings')
  })

  it('с id ведёт к карточке', () => {
    expect(bookingsPath(BOOKING_ID)).toBe(`/admin/bookings/${BOOKING_ID}`)
  })
})

describe('parseAdminPath: барберы', () => {
  it('по умолчанию открывает очередь «На проверке»', () => {
    expect(parseAdminPath('/admin/barbers', '')).toStrictEqual({
      kind: 'barbers',
      barberId: null,
      status: 'pending',
    })
  })

  it('читает фильтр из ?status=', () => {
    expect(parseAdminPath('/admin/barbers', '?status=hidden')).toStrictEqual({
      kind: 'barbers',
      barberId: null,
      status: 'hidden',
    })
  })

  it('неизвестный фильтр возвращает очередь проверки', () => {
    expect(parseAdminPath('/admin/barbers', '?status=rejected')).toStrictEqual({
      kind: 'barbers',
      barberId: null,
      status: 'pending',
    })
  })

  it('карточка сохраняет фильтр из адреса', () => {
    expect(
      parseAdminPath(`/admin/barbers/${BARBER_ID}`, '?status=all')
    ).toStrictEqual({
      kind: 'barbers',
      barberId: BARBER_ID,
      status: 'all',
    })
  })
})

describe('barbersPath', () => {
  it('без id и фильтра ведёт в очередь проверки', () => {
    expect(barbersPath()).toBe('/admin/barbers')
  })

  it('«Все» и «Скрытые» попадают в query', () => {
    expect(barbersPath(null, 'all')).toBe('/admin/barbers?status=all')
    expect(barbersPath(null, 'hidden')).toBe('/admin/barbers?status=hidden')
  })

  it('фильтр едет вместе с выбранной карточкой', () => {
    expect(barbersPath(BARBER_ID, 'hidden')).toBe(
      `/admin/barbers/${BARBER_ID}?status=hidden`
    )
  })
})

describe('isBookingId', () => {
  it('принимает GUID и отвергает свободные строки', () => {
    expect(isBookingId(BOOKING_ID)).toBe(true)
    expect(isBookingId('e2e-bookings')).toBe(false)
    expect(isBookingId('')).toBe(false)
  })
})

describe('parseAdminPath: сводка', () => {
  it('/admin/summary больше не notFound', () => {
    expect(parseAdminPath('/admin/summary', '')).toStrictEqual({
      kind: 'summary',
    })
  })

  it('вложенный путь сводки остаётся notFound', () => {
    expect(parseAdminPath('/admin/summary/extra', '')).toStrictEqual({
      kind: 'notFound',
    })
  })

  it('/admin по-прежнему открывает очередь отзывов, а не сводку', () => {
    expect(parseAdminPath('/admin', '')).toStrictEqual({
      kind: 'reviews',
      tab: 'pending',
      reviewId: null,
    })
  })
})

describe('parseAdminPath: заявки', () => {
  const REQUEST_ID = '9ea00000-0000-4000-8000-000000000001'

  it('по умолчанию открывает очередь неразобранных заявок', () => {
    expect(parseAdminPath('/admin/barber-requests', '')).toStrictEqual({
      kind: 'barberRequests',
      requestId: null,
      status: 'pending',
    })
  })

  it('читает фильтр из ?status=', () => {
    expect(
      parseAdminPath('/admin/barber-requests', '?status=all')
    ).toStrictEqual({
      kind: 'barberRequests',
      requestId: null,
      status: 'all',
    })
  })

  it('неизвестный фильтр возвращает очередь', () => {
    expect(
      parseAdminPath('/admin/barber-requests', '?status=rejected')
    ).toStrictEqual({
      kind: 'barberRequests',
      requestId: null,
      status: 'pending',
    })
  })

  it('карточка сохраняет фильтр из адреса', () => {
    expect(
      parseAdminPath(`/admin/barber-requests/${REQUEST_ID}`, '?status=all')
    ).toStrictEqual({
      kind: 'barberRequests',
      requestId: REQUEST_ID,
      status: 'all',
    })
  })

  it('лишний вложенный путь остаётся notFound', () => {
    expect(
      parseAdminPath('/admin/barber-requests/a/b', '')
    ).toStrictEqual({ kind: 'notFound' })
  })
})

describe('barberRequestsPath', () => {
  const REQUEST_ID = '9ea00000-0000-4000-8000-000000000001'

  it('без id и фильтра ведёт в очередь', () => {
    expect(barberRequestsPath()).toBe('/admin/barber-requests')
  })

  it('«Все» попадает в query', () => {
    expect(barberRequestsPath(null, 'all')).toBe(
      '/admin/barber-requests?status=all'
    )
  })

  it('фильтр едет вместе с выбранной заявкой', () => {
    expect(barberRequestsPath(REQUEST_ID, 'all')).toBe(
      `/admin/barber-requests/${REQUEST_ID}?status=all`
    )
  })
})

describe('isBarberRequestId', () => {
  it('принимает GUID и отвергает свободные строки', () => {
    expect(isBarberRequestId('9ea00000-0000-4000-8000-000000000001')).toBe(true)
    expect(isBarberRequestId('e2e-applicant')).toBe(false)
    expect(isBarberRequestId('')).toBe(false)
  })
})

describe('statistics', () => {
  it('точный адрес — статистика, а не отзывы', () => {
    expect(parseAdminPath('/admin/statistics', '')).toStrictEqual({
      kind: 'statistics',
    })
  })

  it('период в query не меняет маршрут', () => {
    expect(
      parseAdminPath(
        '/admin/statistics',
        '?from=2026-10-10&to=2026-11-08&compare=1'
      )
    ).toStrictEqual({ kind: 'statistics' })
  })

  it('вложенный путь остаётся notFound', () => {
    expect(parseAdminPath('/admin/statistics/2026', '')).toStrictEqual({
      kind: 'notFound',
    })
  })
})
