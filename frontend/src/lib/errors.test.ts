import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import { describeRescheduleFailure } from '@/lib/errors'
import { problemDetails } from '@/test/msw/handlers'

/**
 * Маппер ошибок переноса — общий для двух кабинетов.
 *
 * Код важнее статуса: `409` бывает и «время занято», и «запись уже нельзя
 * перенести». `detail` английский и наружу не выходит.
 */

function apiError(
  status: number,
  codes: string[] = [],
  detail = 'English detail'
): ApiError {
  return new ApiError(status, problemDetails(status, { errorCodes: codes, detail }), detail)
}

const FALLBACK = 'Не удалось перенести запись.'

describe('describeRescheduleFailure', () => {
  it.each([
    ['booking.reschedule.overlap', 'Это время уже занято. Выберите другое.', true],
    ['booking.update.overlap', 'Это время уже занято. Выберите другое.', true],
    [
      'booking.reschedule.slot',
      'Это время больше не доступно. Выберите другое.',
      true,
    ],
    [
      'booking.reschedule.startAt.inPast',
      'Это время уже прошло. Выберите другое.',
      true,
    ],
    ['booking.reschedule.startAt.tooFar', 'Слишком далеко в будущем.', false],
    [
      'booking.reschedule.status',
      'Запись уже нельзя перенести — обновите страницу.',
      false,
    ],
    [
      'booking.reschedule.lockTimeout',
      'Не удалось перенести: попробуйте ещё раз.',
      true,
    ],
    [
      'booking.update.concurrencyConflict',
      'Запись изменилась — обновите страницу.',
      true,
    ],
  ])('код %s → свой текст и reloadSlots=%s', (code, message, reloadSlots) => {
    expect(describeRescheduleFailure(apiError(409, [code]), FALLBACK)).toStrictEqual(
      { message, reloadSlots }
    )
  })

  it('404 и booking.notFound — «Запись не найдена.» без перезапроса', () => {
    expect(describeRescheduleFailure(apiError(404), FALLBACK)).toStrictEqual({
      message: 'Запись не найдена.',
      reloadSlots: false,
    })
    expect(
      describeRescheduleFailure(apiError(400, ['booking.notFound']), FALLBACK)
    ).toStrictEqual({ message: 'Запись не найдена.', reloadSlots: false })
  })

  it('403 — «Это запись другого клиента.» без перезапроса', () => {
    expect(describeRescheduleFailure(apiError(403), FALLBACK)).toStrictEqual({
      message: 'Это запись другого клиента.',
      reloadSlots: false,
    })
  })

  it('неизвестный ответ — общий текст, detail не показывается', () => {
    const result = describeRescheduleFailure(
      apiError(500, [], 'Something exploded'),
      FALLBACK
    )

    expect(result).toStrictEqual({ message: FALLBACK, reloadSlots: false })
    expect(result.message).not.toContain('Something exploded')
  })

  it('не-ApiError — запасной текст', () => {
    expect(
      describeRescheduleFailure(new Error('сеть'), FALLBACK)
    ).toStrictEqual({ message: FALLBACK, reloadSlots: false })
  })
})
