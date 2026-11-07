import { describe, expect, it } from 'vitest'

import { salonDateTimeToUtc } from '@/lib/salon-time'

/**
 * Пояс салона — единственное место, где «10:00» становится инстантом.
 *
 * Екатеринбург держит постоянное смещение +5, Берлин переходит на летнее время:
 * на нём и проверяются обе беды перехода — пропавший и повторяющийся час.
 */

describe('salonDateTimeToUtc', () => {
  it('Екатеринбург: 10:00 — это 05:00Z', () => {
    expect(
      salonDateTimeToUtc('2026-11-12', '10:00', 'Asia/Yekaterinburg')
    ).toBe('2026-11-12T05:00:00.000Z')
  })

  it.each([
    ['лето: 14:00 — 12:00Z', '2026-07-15', '14:00', '2026-07-15T12:00:00.000Z'],
    ['зима: 14:00 — 13:00Z', '2026-12-15', '14:00', '2026-12-15T13:00:00.000Z'],
  ])('Берлин, %s', (_title, date, time, expected) => {
    expect(salonDateTimeToUtc(date, time, 'Europe/Berlin')).toBe(expected)
  })

  it('Берлин: 29.03.2026 02:30 не существует — null', () => {
    expect(salonDateTimeToUtc('2026-03-29', '02:30', 'Europe/Berlin')).toBeNull()
  })

  it('Берлин: 25.10.2026 02:30 дважды — берётся первое вхождение', () => {
    // 00:30Z — ещё летнее смещение (+2), 01:30Z — уже зимнее (+1). Первое
    // вхождение наступает раньше и остаётся за строкой импорта.
    expect(
      salonDateTimeToUtc('2026-10-25', '02:30', 'Europe/Berlin')
    ).toBe('2026-10-25T00:30:00.000Z')
  })

  it('мусорный пояс — null, без исключения', () => {
    expect(salonDateTimeToUtc('2026-11-12', '10:00', 'Mars/Olympus')).toBeNull()
    expect(salonDateTimeToUtc('2026-11-12', '10:00', '')).toBeNull()
  })

  it.each([
    ['кривая дата', '12.11.2026', '10:00'],
    ['несуществующая дата', '2026-02-30', '10:00'],
    ['кривое время', '2026-11-12', '25:00'],
    ['время без минут', '2026-11-12', '10'],
  ])('%s — null', (_title, date, time) => {
    expect(salonDateTimeToUtc(date, time, 'Asia/Yekaterinburg')).toBeNull()
  })
})
