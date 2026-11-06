import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import { describeBarberProfileCreateError } from '@/lib/barber-profile-create'

/**
 * Перевод отказов `POST /api/barber-profiles` в русский текст.
 *
 * Коды — контракт, `detail` — нет: проверяется ветка по коду, а не английское сообщение.
 */
function apiError(
  status: number,
  codes: string[] = []
): ApiError {
  return new ApiError(status, { status, errorCodes: codes }, 'English detail')
}

describe('describeBarberProfileCreateError', () => {
  it('пустое имя и слишком длинное имя — ошибка у поля', () => {
    expect(
      describeBarberProfileCreateError(
        apiError(400, ['barberProfile.displayName'])
      )
    ).toMatchObject({ field: 'displayName', canRetry: false })

    expect(
      describeBarberProfileCreateError(
        apiError(400, ['barberProfile.displayName.tooLong'])
      )
    ).toMatchObject({
      field: 'displayName',
      message: 'Не длиннее 120 символов.',
    })
  })

  it('опыт вне диапазона — ошибка у поля', () => {
    expect(
      describeBarberProfileCreateError(
        apiError(400, ['barberProfile.yearsOfExperience'])
      )
    ).toMatchObject({ field: 'yearsOfExperience' })
  })

  it('неизвестный грейд привязан к своему полю', () => {
    expect(
      describeBarberProfileCreateError(
        apiError(400, ['barberProfile.grade.unknown'])
      )
    ).toMatchObject({
      field: 'grade',
      message: 'Выберите грейд из списка.',
    })
  })

  it('неизвестные языки и способы оплаты привязаны к своим группам', () => {
    expect(
      describeBarberProfileCreateError(
        apiError(400, ['barberProfile.languages.unknown'])
      )
    ).toMatchObject({ field: 'languages' })

    expect(
      describeBarberProfileCreateError(
        apiError(400, ['barberProfile.paymentMethods.unknown'])
      )
    ).toMatchObject({ field: 'paymentMethods' })
  })

  it('429 и обрыв сети можно повторить', () => {
    expect(describeBarberProfileCreateError(apiError(429))).toMatchObject({
      field: null,
      canRetry: true,
      message: 'Слишком много запросов. Подождите минуту и повторите.',
    })

    expect(describeBarberProfileCreateError(apiError(0))).toMatchObject({
      canRetry: true,
    })
  })

  it('5xx повторяем, неизвестный отказ — нет', () => {
    expect(describeBarberProfileCreateError(apiError(500))).toMatchObject({
      field: null,
      canRetry: true,
    })

    expect(
      describeBarberProfileCreateError(apiError(400, ['barberProfile.unknown']))
    ).toMatchObject({ field: null, canRetry: false })

    expect(describeBarberProfileCreateError(new Error('boom'))).toMatchObject({
      field: null,
      canRetry: false,
    })
  })
})
