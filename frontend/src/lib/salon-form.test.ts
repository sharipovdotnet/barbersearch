import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  emptySalonForm,
  salonContactInput,
  salonCreateInput,
  salonFieldErrors,
  salonFormValues,
  salonLocationInput,
  validateSalonForm,
  describeSalonSaveError,
  type SalonFormValues,
} from '@/lib/salon-form'
import { problemDetails } from '@/test/msw/handlers'

/**
 * Общая форма салона: правила полей и сборка тел запросов.
 *
 * Проверяем то, что одинаково важно обоим входам — админскому каталогу и кабинету
 * барбера: обязательные поля, диапазоны координат, сайт `http(s)`, телефон до 32
 * символов и отсутствие выдуманного лимита названия.
 */

function validForm(overrides: Partial<SalonFormValues> = {}): SalonFormValues {
  return {
    ...emptySalonForm(),
    name: 'Барбершоп на Ленина',
    address: 'ул. Ленина, 1',
    city: 'Екатеринбург',
    country: 'Россия',
    timeZoneId: 'Asia/Yekaterinburg',
    latitude: '56.8389',
    longitude: '60.6057',
    ...overrides,
  }
}

describe('validateSalonForm', () => {
  it('пустая форма требует название, адрес, город, страну, пояс и координаты', () => {
    const errors = validateSalonForm(emptySalonForm())

    expect(errors).toMatchObject({
      name: 'Введите название.',
      address: 'Введите адрес.',
      city: 'Введите город.',
      country: 'Введите страну.',
      latitude: 'От -90 до 90.',
      longitude: 'От -180 до 180.',
    })
  })

  it('корректная форма ошибок не даёт', () => {
    expect(validateSalonForm(validForm())).toStrictEqual({})
  })

  it('не выдумывает лимит длины названия', () => {
    const longName = 'С'.repeat(250)

    expect(validateSalonForm(validForm({ name: longName }))).toStrictEqual({})
  })

  it('проверяет диапазоны координат', () => {
    expect(
      validateSalonForm(validForm({ latitude: '90.1' })).latitude
    ).toBe('От -90 до 90.')
    expect(
      validateSalonForm(validForm({ longitude: '-180.1' })).longitude
    ).toBe('От -180 до 180.')
    expect(
      validateSalonForm(validForm({ latitude: 'не число' })).latitude
    ).toBe('От -90 до 90.')
  })

  it('принимает только http(s) и телефон не длиннее 32 символов', () => {
    expect(
      validateSalonForm(validForm({ websiteUrl: 'ftp://example.com' }))
        .websiteUrl
    ).toBe('Адрес сайта начинается с http:// или https://.')
    expect(
      validateSalonForm(validForm({ websiteUrl: 'https://example.com' }))
    ).toStrictEqual({})
    expect(validateSalonForm(validForm({ phoneNumber: '1'.repeat(33) })))
      .toMatchObject({ phoneNumber: 'Не длиннее 32 символов.' })
  })
})

describe('salonFormValues', () => {
  it('оставляет необязательные поля пустыми, если источник их не прислал', () => {
    const values = salonFormValues({
      name: 'E2E Studio',
      address: 'ул. Тестовая, 1',
      city: 'Москва',
      timeZoneId: 'Europe/Moscow',
    })

    expect(values).toMatchObject({
      name: 'E2E Studio',
      address: 'ул. Тестовая, 1',
      city: 'Москва',
      timeZoneId: 'Europe/Moscow',
      country: '',
      postalCode: '',
      latitude: '',
      longitude: '',
      phoneNumber: '',
      websiteUrl: '',
    })
  })

  it('берёт координаты и контакты, если они пришли', () => {
    const values = salonFormValues({
      name: 'E2E Studio',
      address: 'ул. Тестовая, 1',
      city: 'Москва',
      timeZoneId: 'Europe/Moscow',
      country: 'Russia',
      postalCode: '101000',
      latitude: 55.7,
      longitude: 37.6,
      phoneNumber: '+70000000009',
      websiteUrl: null,
    })

    expect(values).toMatchObject({
      country: 'Russia',
      postalCode: '101000',
      latitude: '55.7',
      longitude: '37.6',
      phoneNumber: '+70000000009',
      websiteUrl: '',
    })
  })
})

describe('тела запросов', () => {
  it('тримят значения и отправляют пустые необязательные поля как null', () => {
    const values = validForm({
      name: '  Новый салон  ',
      address: '  ул. Мира, 2  ',
      city: '  Москва  ',
      country: '  Россия  ',
      postalCode: '  ',
      phoneNumber: '  +79990000000  ',
      websiteUrl: '  ',
    })

    expect(salonLocationInput(values)).toStrictEqual({
      address: 'ул. Мира, 2',
      city: 'Москва',
      country: 'Россия',
      timeZoneId: 'Asia/Yekaterinburg',
      latitude: 56.8389,
      longitude: 60.6057,
      postalCode: null,
    })

    expect(salonContactInput(values)).toStrictEqual({
      phoneNumber: '+79990000000',
      websiteUrl: null,
    })

    expect(salonCreateInput(values)).toStrictEqual({
      name: 'Новый салон',
      address: 'ул. Мира, 2',
      city: 'Москва',
      country: 'Россия',
      timeZoneId: 'Asia/Yekaterinburg',
      latitude: 56.8389,
      longitude: 60.6057,
      postalCode: null,
      phoneNumber: '+79990000000',
      websiteUrl: null,
    })
  })
})

describe('describeSalonSaveError', () => {
  it('у владельца объясняет чужой салон, у админа — закрытый раздел', () => {
    const forbidden = new ApiError(403, problemDetails(403), 'Forbidden.')

    expect(describeSalonSaveError(forbidden, 'owner', 'edit')).toBe(
      'Это чужой салон — править его может только владелец.'
    )
    expect(describeSalonSaveError(forbidden, 'admin', 'edit')).toBe(
      'Раздел доступен только администратору.'
    )
  })

  it('у владельца отдельный текст для бара без профиля', () => {
    const error = new ApiError(
      403,
      problemDetails(403, { errorCodes: ['barberProfile.notFound'] }),
      'Forbidden.'
    )

    expect(describeSalonSaveError(error, 'owner', 'create')).toBe(
      'Профиль барбера не найден. Создайте профиль и повторите.'
    )
  })

  it('переводит конфликт правки и неизвестный пояс, остальное — запасной текст', () => {
    const conflict = new ApiError(
      409,
      problemDetails(409, {
        errorCodes: ['salon.update.concurrencyConflict'],
      }),
      'Conflict.'
    )
    const zone = new ApiError(
      400,
      problemDetails(400, { errorCodes: ['salon.timeZoneId.unknown'] }),
      'Invalid.'
    )

    expect(describeSalonSaveError(conflict, 'admin', 'edit')).toBe(
      'Запись изменили одновременно. Обновите список.'
    )
    expect(describeSalonSaveError(zone, 'owner', 'edit')).toBe(
      'Неизвестный часовой пояс. Выберите зону из списка.'
    )
    expect(describeSalonSaveError(new Error('сеть'), 'owner', 'create')).toBe(
      'Не удалось создать салон. Попробуйте ещё раз.'
    )
  })
})

describe('salonFieldErrors', () => {
  it('показывает серверные коды у полей, включая город домена', () => {
    const error = new ApiError(
      400,
      problemDetails(400, {
        errorCodes: ['salon.city', 'salon.websiteUrl'],
      }),
      'Invalid.'
    )

    expect(salonFieldErrors(error)).toStrictEqual({
      city: 'Город не может быть пустым.',
      websiteUrl: 'Сайт должен быть адресом http(s) не длиннее 2048 символов.',
    })
  })
})
