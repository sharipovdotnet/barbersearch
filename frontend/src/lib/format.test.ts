import { describe, expect, it } from 'vitest'

import { formatProfilesCount, formatSalonsCount } from '@/lib/format'

/**
 * Формы счётчиков сводки: число — часть текста, поэтому падеж проверяется
 * на трёх формах сразу (1, 2 и 5), а не на одной.
 */
describe('счётчики сводки', () => {
  it.each([
    [1, '1 профиль'],
    [2, '2 профиля'],
    [5, '5 профилей'],
    [11, '11 профилей'],
    [21, '21 профиль'],
  ])('formatProfilesCount(%i) → «%s»', (count, expected) => {
    expect(formatProfilesCount(count)).toBe(expected)
  })

  it.each([
    [1, '1 салон'],
    [2, '2 салона'],
    [5, '5 салонов'],
    [11, '11 салонов'],
    [21, '21 салон'],
  ])('formatSalonsCount(%i) → «%s»', (count, expected) => {
    expect(formatSalonsCount(count)).toBe(expected)
  })
})
