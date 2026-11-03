import { describe, expect, it } from 'vitest'

import {
  isPrivacyPath,
  PRIVACY_PATH,
  PRIVACY_POLICY_DATE,
  PRIVACY_POLICY_VERSION,
} from '@/lib/legal'

describe('isPrivacyPath', () => {
  it('совпадает только с точным адресом Политики', () => {
    expect(isPrivacyPath(PRIVACY_PATH)).toBe(true)
    expect(isPrivacyPath('/privacy/')).toBe(false)
    expect(isPrivacyPath('/privacyx')).toBe(false)
    expect(isPrivacyPath('/privacy#cookies')).toBe(false)
    expect(isPrivacyPath('/')).toBe(false)
  })
})

describe('редакция Политики', () => {
  it('версия согласия совпадает с датой редакции', () => {
    expect(PRIVACY_POLICY_VERSION).toBe(PRIVACY_POLICY_DATE)
  })

  it('держит литерал 2026-11-03: он же в PrivacyPolicy.Version на сервере', () => {
    // Guard от расхождения с backend-константой: компилятор два языка не сверит,
    // поэтому литерал зафиксирован здесь и в RegistrationConsentTests на сервере.
    expect(PRIVACY_POLICY_VERSION).toBe('2026-11-03')
  })
})

describe('дата редакции', () => {
  it('поднята до даты правки Политики', () => {
    expect(PRIVACY_POLICY_DATE).toBe('2026-11-03')
  })
})
