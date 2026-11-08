import { describe, expect, it } from 'vitest'

import { setCurrentLocale } from '@/i18n'
import {
  formatCount,
  formatDelta,
  formatDuration,
  formatEventsCount,
  formatExperience,
  formatGapsCount,
  formatNumber,
  formatPercent,
  formatPrice,
  formatProfilesCount,
  formatReviewsCount,
  formatSalonsCount,
} from '@/lib/format'

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

  it.each([
    [1, '1 окно'],
    [2, '2 окна'],
    [4, '4 окна'],
    [5, '5 окон'],
    [11, '11 окон'],
    [21, '21 окно'],
  ])('formatGapsCount(%i) → «%s»', (count, expected) => {
    expect(formatGapsCount(count)).toBe(expected)
  })

  it.each([
    [1, '1 событие'],
    [2, '2 события'],
    [4, '4 события'],
    [5, '5 событий'],
    [11, '11 событий'],
    [21, '21 событие'],
  ])('formatEventsCount(%i) → «%s»', (count, expected) => {
    expect(formatEventsCount(count)).toBe(expected)
  })
})

describe('статистика периода', () => {
  it('форматирует доли и честный прочерк', () => {
    expect(formatPercent(0.047)).toBe('4,7 %')
    expect(formatPercent(0.047, 0)).toBe('5 %')
    expect(formatPercent(0.9, 0)).toBe('90 %')
    expect(formatPercent(null)).toBe('—')
  })

  it('считает дельту в процентах и п.п.', () => {
    expect(formatDelta(128, 114)).toBe('+12,3 %')
    expect(formatDelta(90, 100)).toBe('−10 %')
    expect(formatDelta(100, 100)).toBe('±0 %')
    expect(formatDelta(0.055, 0.047, 'points')).toBe('+0,8 п.п.')
    expect(formatDelta(0.047, 0.055, 'points')).toBe('−0,8 п.п.')
    expect(formatDelta(5, 0)).toBe('—')
    expect(formatDelta(null, 10)).toBe('—')
    expect(formatDelta(10, null)).toBe('—')
  })

  it('склеивает число и падеж', () => {
    expect(formatCount(1, ['визит', 'визита', 'визитов'])).toBe('1 визит')
    expect(formatCount(3, ['визит', 'визита', 'визитов'])).toBe('3 визита')
    expect(formatCount(11, ['визит', 'визита', 'визитов'])).toBe('11 визитов')
    expect(formatNumber(1284)).toMatch(/^1\s284$/)
    expect(formatNumber(26.55, 1)).toBe('26,6')
  })
})

/**
 * Английская типографика: те же функции, но `en-GB` — круглосуточное время,
 * европейский порядок даты и валюта RUB.
 */
describe('английское форматирование', () => {
  it('переводит счётчики и деньги', () => {
    setCurrentLocale('en')

    expect(formatReviewsCount(1)).toBe('1 review')
    expect(formatReviewsCount(2)).toBe('2 reviews')
    expect(formatProfilesCount(5)).toBe('5 profiles')
    expect(formatSalonsCount(2)).toBe('2 salons')
    expect(formatEventsCount(3)).toBe('3 events')
    expect(formatGapsCount(4)).toBe('4 gaps')
    expect(formatExperience(8)).toBe('8 years')
    expect(formatCount(3, ['визит', 'визита', 'визитов'])).toBe('3 visits')
    expect(formatPrice(2400)).toMatch(/RUB\s2,400/)
    expect(formatDuration(90)).toBe('1 h 30 min')
  })

  it('переводит проценты, дельты и числа', () => {
    setCurrentLocale('en')

    expect(formatPercent(0.047)).toBe('4.7%')
    expect(formatPercent(0.9, 0)).toBe('90%')
    expect(formatDelta(128, 114)).toBe('+12.3%')
    expect(formatDelta(90, 100)).toBe('−10%')
    expect(formatDelta(0.055, 0.047, 'points')).toBe('+0.8 pp')
    expect(formatNumber(1284)).toBe('1,284')
    expect(formatNumber(26.55, 1)).toBe('26.6')
  })
})
