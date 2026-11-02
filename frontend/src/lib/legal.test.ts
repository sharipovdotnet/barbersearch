import { describe, expect, it } from 'vitest'

import { isPrivacyPath, PRIVACY_PATH } from '@/lib/legal'

describe('isPrivacyPath', () => {
  it('совпадает только с точным адресом Политики', () => {
    expect(isPrivacyPath(PRIVACY_PATH)).toBe(true)
    expect(isPrivacyPath('/privacy/')).toBe(false)
    expect(isPrivacyPath('/privacyx')).toBe(false)
    expect(isPrivacyPath('/privacy#cookies')).toBe(false)
    expect(isPrivacyPath('/')).toBe(false)
  })
})
