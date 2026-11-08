import { describe, expect, it } from 'vitest'

import {
  formatMessage,
  getLocale,
  LOCALE_STORAGE_KEY,
  readStoredLocale,
  setCurrentLocale,
  t,
  tp,
  translate,
} from '@/i18n'

/**
 * Ядро каталога: русский — исходный текст, английский — пара к нему. Проверяем
 * подстановку параметров и склонения, потому что от них зависят все счётчики.
 */
describe('i18n', () => {
  it('по умолчанию говорит по-русски и переводит только в en', () => {
    setCurrentLocale('ru')
    expect(getLocale()).toBe('ru')
    expect(t('Мой кабинет')).toBe('Мой кабинет')

    setCurrentLocale('en')
    expect(getLocale()).toBe('en')
    expect(t('Мой кабинет')).toBe('My account')

    setCurrentLocale('ru')
  })

  it('подставляет параметры и оставляет неизвестный плейсхолдер', () => {
    expect(formatMessage('Салон: {name}', { name: 'Барбер' })).toBe(
      'Салон: Барбер'
    )
    expect(formatMessage('Салон: {name}')).toBe('Салон: {name}')
    expect(formatMessage('{known} и {unknown}', { known: 'x' })).toBe(
      'x и {unknown}'
    )
  })

  it('переводит для явно заданного языка', () => {
    expect(translate('ru', 'Повторить')).toBe('Повторить')
    expect(translate('en', 'Повторить')).toBe('Retry')
    expect(translate('en', 'Повторить', {})).toBe('Retry')
  })

  it('разводит один русский текст с разным смыслом через context', () => {
    setCurrentLocale('en')

    expect(t('Подтвердить', { context: 'verify' })).toBe('Verify')
    expect(t('Подтвердить')).toBe('Confirm')

    setCurrentLocale('ru')
  })

  it('склоняет счётчики по правилам языка', () => {
    setCurrentLocale('ru')
    expect(tp('reviews', 1)).toBe('1 отзыв')
    expect(tp('reviews', 2)).toBe('2 отзыва')
    expect(tp('reviews', 5)).toBe('5 отзывов')
    expect(tp('reviews', 11)).toBe('11 отзывов')
    expect(tp('reviews', 21)).toBe('21 отзыв')

    setCurrentLocale('en')
    expect(tp('reviews', 1)).toBe('1 review')
    expect(tp('reviews', 2)).toBe('2 reviews')
    expect(tp('reviews', 21)).toBe('21 reviews')

    setCurrentLocale('ru')
  })

  it('читает сохранённый язык и игнорирует неизвестное значение', () => {
    window.localStorage.setItem(LOCALE_STORAGE_KEY, 'en')
    expect(readStoredLocale()).toBe('en')

    window.localStorage.setItem(LOCALE_STORAGE_KEY, 'de')
    expect(readStoredLocale()).toBe('ru')

    window.localStorage.removeItem(LOCALE_STORAGE_KEY)
    expect(readStoredLocale()).toBe('ru')
  })
})
