import { describe, expect, it } from 'vitest'

import { icsIdempotencyKey } from '@/lib/ics-identity'

/**
 * Ключ — часть контракта идемпотентности импорта (spec §5.6): одинаковый вход
 * должен давать одинаковый ключ в любой сессии, а формат — совпадать с тем,
 * что ожидает сервер.
 */
describe('icsIdempotencyKey', () => {
  it('одинаковые UID и recurrenceId дают одинаковый ключ при повторном вызове', () => {
    const first = icsIdempotencyKey('uid-1', '2026-11-12T10:00:00Z')
    const second = icsIdempotencyKey('uid-1', '2026-11-12T10:00:00Z')

    expect(first).toBe(second)
  })

  it('разные recurrenceId дают разные ключи', () => {
    const first = icsIdempotencyKey('uid-1', '2026-11-12T10:00:00Z')
    const second = icsIdempotencyKey('uid-1', '2026-11-19T10:00:00Z')

    expect(first).not.toBe(second)
  })

  it('разные UID дают разные ключи', () => {
    const first = icsIdempotencyKey('uid-1', null)
    const second = icsIdempotencyKey('uid-2', null)

    expect(first).not.toBe(second)
  })

  it('ключ начинается с ics: и содержит ровно 32 hex-символа', () => {
    const key = icsIdempotencyKey('uid-1', null)

    expect(key.startsWith('ics:')).toBe(true)
    expect(key).toHaveLength(36)
    expect(key.slice('ics:'.length)).toMatch(/^[0-9a-f]{32}$/)
  })

  it('обрезает пробелы вокруг UID', () => {
    expect(icsIdempotencyKey(' uid ', null)).toBe(icsIdempotencyKey('uid', null))
  })

  it('считает null и пустой recurrenceId одним и тем же входом', () => {
    expect(icsIdempotencyKey('uid-1', null)).toBe(icsIdempotencyKey('uid-1', ''))
  })

  it('обрезает пробелы вокруг recurrenceId', () => {
    expect(icsIdempotencyKey('uid-1', ' 2026-11-12T10:00:00Z ')).toBe(
      icsIdempotencyKey('uid-1', '2026-11-12T10:00:00Z')
    )
  })

  it('бросает ошибку на пустом UID', () => {
    expect(() => icsIdempotencyKey('', null)).toThrow(
      new Error('Пустой UID события.')
    )
  })

  it('бросает ошибку на UID из одних пробелов', () => {
    expect(() => icsIdempotencyKey('   ', null)).toThrow(
      new Error('Пустой UID события.')
    )
  })
})
