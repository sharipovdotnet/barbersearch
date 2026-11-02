import { describe, expect, it } from 'vitest'

import { acknowledgeCookieNotice, shouldShowCookieNotice } from '@/lib/cookie-notice'

describe('cookie-notice', () => {
  it('до первого закрытия уведомление показывается', () => {
    expect(shouldShowCookieNotice()).toBe(true)
  })

  it('после «Понятно» отметка хранится в cookie и уведомление не повторяется', () => {
    acknowledgeCookieNotice()

    expect(shouldShowCookieNotice()).toBe(false)
    expect(document.cookie).toContain('barbersearch.cookie-notice=1')
  })
})
