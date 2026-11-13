import { describe, expect, it } from 'vitest'

import {
  canChangeBookingPrice,
  canRescheduleBooking,
} from '@/lib/booking-status'

/**
 * Перенос — только активная бронь в будущем: терминальные статусы и начавшийся
 * визит двигать поздно (D4). «Сейчас» тоже не разрешено — сравнение строгое.
 */

const NOW = Date.parse('2026-11-05T10:00:00Z')
const FUTURE = '2026-11-06T09:00:00Z'
const PAST = '2026-11-05T09:59:59Z'
const SAME_MOMENT = '2026-11-05T10:00:00Z'

describe('canRescheduleBooking', () => {
  it('разрешает Pending и Confirmed в будущем', () => {
    expect(canRescheduleBooking('Pending', FUTURE, NOW)).toBe(true)
    expect(canRescheduleBooking('Confirmed', FUTURE, NOW)).toBe(true)
  })

  it('запрещает начавшийся и уже прошедший визит', () => {
    expect(canRescheduleBooking('Confirmed', PAST, NOW)).toBe(false)
    expect(canRescheduleBooking('Confirmed', SAME_MOMENT, NOW)).toBe(false)
  })

  it('запрещает терминальные статусы даже с будущим временем', () => {
    expect(canRescheduleBooking('Cancelled', FUTURE, NOW)).toBe(false)
    expect(canRescheduleBooking('Completed', FUTURE, NOW)).toBe(false)
    expect(canRescheduleBooking('NoShow', FUTURE, NOW)).toBe(false)
  })

  it('битую дату считает непереносимой', () => {
    expect(canRescheduleBooking('Confirmed', 'не дата', NOW)).toBe(false)
  })
})

/**
 * Цена визита: сумма есть у предстоящего и у состоявшегося визита, а отменённая
 * запись и неявка не оплачиваются — ровно как в домене (`booking.price.status`).
 */
describe('canChangeBookingPrice', () => {
  it.each([
    ['Pending', true],
    ['Confirmed', true],
    ['Completed', true],
    ['Cancelled', false],
    ['NoShow', false],
  ] as const)('%s → %s', (status, expected) => {
    expect(canChangeBookingPrice(status)).toBe(expected)
  })
})
