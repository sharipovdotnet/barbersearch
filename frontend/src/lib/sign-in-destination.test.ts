import { describe, expect, it } from 'vitest'

import { signInDestination } from '@/lib/sign-in-destination'

/**
 * Куда вести после входа.
 *
 * `null` — остаться на текущей странице (выбор слота на карточке барбера).
 * `undefined` — явного возврата нет, роль открывает свой рабочий раздел.
 * `/account` — общий кабинет, а не рабочий раздел барбера или администратора.
 */
describe('signInDestination', () => {
  it.each([
    ['Barber', undefined, '/barber'],
    ['Admin', undefined, '/admin'],
    ['Customer', undefined, '/account'],
    ['Barber', '/account', '/barber'],
    ['Admin', '/account', '/admin'],
    ['Customer', '/account', '/account'],
    ['Barber', null, null],
    ['Admin', null, null],
    ['Barber', '/barber/schedule', '/barber/schedule'],
    ['Barber', '/admin/reviews', '/barber'],
    ['Admin', '/barber/schedule', '/admin'],
    ['Customer', '/barber/schedule', '/account'],
    ['Admin', '/admin/barbers?status=all', '/admin/barbers?status=all'],
    ['Barber', '/become-a-barber', '/become-a-barber'],
  ] as const)(
    'роль %s, возврат %s → %s',
    (role, requested, expected) => {
      expect(signInDestination(role, requested)).toBe(expected)
    }
  )
})
