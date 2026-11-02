import { describe, expect, it } from 'vitest'

import {
  ACCOUNT_PATH,
  ACCOUNT_PROFILE_PATH,
  ACCOUNT_REVIEWS_PATH,
  ACCOUNT_SECURITY_PATH,
  accountSection,
  isAccountPath,
  isAccountProfilePath,
  isAccountReviewsPath,
  isAccountSecurityPath,
} from '@/lib/account-path'

/**
 * Разделы кабинета: один префикс `/account`, четыре точных адреса.
 *
 * Неизвестный вложенный путь не должен открывать кабинет: иначе `/account/foo`
 * показывал бы записи под чужим заголовком.
 */
describe('accountSection', () => {
  it.each([
    [ACCOUNT_PATH, 'bookings'],
    [ACCOUNT_REVIEWS_PATH, 'reviews'],
    [ACCOUNT_PROFILE_PATH, 'profile'],
    [ACCOUNT_SECURITY_PATH, 'security'],
  ] as const)('%s — раздел %s', (path, section) => {
    expect(accountSection(path)).toBe(section)
  })

  it('неизвестный вложенный путь — не кабинет', () => {
    expect(accountSection('/account/unknown')).toBeNull()
    expect(accountSection('/account/profile/extra')).toBeNull()
    expect(accountSection('/accounting')).toBeNull()
  })
})

describe('предикаты кабинета', () => {
  it('все четыре раздела считаются кабинетом', () => {
    for (const path of [
      ACCOUNT_PATH,
      ACCOUNT_REVIEWS_PATH,
      ACCOUNT_PROFILE_PATH,
      ACCOUNT_SECURITY_PATH,
    ]) {
      expect(isAccountPath(path)).toBe(true)
    }
  })

  it('точечные предикаты не пересекаются', () => {
    expect(isAccountReviewsPath(ACCOUNT_REVIEWS_PATH)).toBe(true)
    expect(isAccountReviewsPath(ACCOUNT_PROFILE_PATH)).toBe(false)

    expect(isAccountProfilePath(ACCOUNT_PROFILE_PATH)).toBe(true)
    expect(isAccountProfilePath(ACCOUNT_SECURITY_PATH)).toBe(false)

    expect(isAccountSecurityPath(ACCOUNT_SECURITY_PATH)).toBe(true)
    expect(isAccountSecurityPath(ACCOUNT_PATH)).toBe(false)
  })
})
