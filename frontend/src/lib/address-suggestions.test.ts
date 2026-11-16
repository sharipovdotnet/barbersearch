import { describe, expect, it } from 'vitest'

import {
  addressSuggestionSnapshot,
  addressSuggestionsUrl,
  applyAddressSuggestion,
  applyCitySuggestion,
  citySuggestionsUrl,
  clearSuggestedFields,
  detachSuggestedField,
  type AddressSuggestion,
  type CitySuggestion,
} from '@/lib/address-suggestions'
import { emptySalonForm, type SalonFormValues } from '@/lib/salon-form'

const CITY: CitySuggestion = {
  providerId: '2763c110-cb8b-416a-9dac-ad28a55b4402',
  display: 'г. Екатеринбург',
  city: 'Екатеринбург',
  region: 'Свердловская обл.',
  country: 'Россия',
  latitude: 56.8389,
  longitude: 60.5975,
}

const ADDRESS: AddressSuggestion = {
  providerId: 'f0a1b2c3-0000-4000-8000-000000000001',
  display: 'г. Екатеринбург, ул. Ленина, д. 1',
  address: 'ул. Ленина, 1',
  city: 'Екатеринбург',
  region: 'Свердловская обл.',
  country: 'Россия',
  postalCode: '620000',
  latitude: 56.8389,
  longitude: 60.5975,
  timeZoneId: null,
}

/** Заполненная вручную форма: подсказка не должна трогать эти значения. */
function manualValues(
  overrides: Partial<SalonFormValues> = {}
): SalonFormValues {
  return {
    ...emptySalonForm(),
    name: 'Место приёма',
    address: 'ул. Ручная, 2',
    city: 'Москва',
    country: 'Россия',
    postalCode: '101000',
    timeZoneId: 'Asia/Yekaterinburg',
    latitude: '55.7558',
    longitude: '37.6173',
    ...overrides,
  }
}

describe('addressSuggestionsUrl', () => {
  it('обрезает пробелы и кодирует кириллицу', () => {
    const url = citySuggestionsUrl('  Екат ')

    expect(url.startsWith('/addresses/cities?')).toBe(true)
    expect(url).toContain('query=%D0%95%D0%BA%D0%B0%D1%82')
    expect(url).toContain('limit=8')
  })

  it('в городе отправляет свободный текст, когда нет providerId', () => {
    const params = new URL(
      addressSuggestionsUrl({ query: ' ул. Лен ', city: ' Екатеринбург ' }),
      'http://localhost'
    ).searchParams

    expect(params.get('query')).toBe('ул. Лен')
    expect(params.get('city')).toBe('Екатеринбург')
    expect(params.get('cityProviderId')).toBeNull()
    expect(params.get('limit')).toBe('8')
  })

  it('providerId важнее свободного текста города', () => {
    const params = new URL(
      addressSuggestionsUrl({
        query: 'ул. Лен',
        cityProviderId: CITY.providerId,
        city: 'Екатеринбург',
      }),
      'http://localhost'
    ).searchParams

    expect(params.get('cityProviderId')).toBe(CITY.providerId)
    expect(params.get('city')).toBeNull()
  })

  it('не отправляет пустые city и cityProviderId', () => {
    const url = addressSuggestionsUrl({ query: 'ул. Лен', city: '   ' })
    const params = new URL(url, 'http://localhost').searchParams

    expect(params.has('city')).toBe(false)
    expect(params.has('cityProviderId')).toBe(false)
  })
})

describe('applyCitySuggestion', () => {
  it('заполняет город и страну, не трогая адрес, индекс и координаты', () => {
    const next = applyCitySuggestion(manualValues(), CITY)

    expect(next.city).toBe('Екатеринбург')
    expect(next.country).toBe('Россия')
    expect(next.address).toBe('ул. Ручная, 2')
    expect(next.postalCode).toBe('101000')
    expect(next.latitude).toBe('55.7558')
    expect(next.longitude).toBe('37.6173')
  })

  it('пустая страна из подсказки не затирает введённую', () => {
    const next = applyCitySuggestion(manualValues({ country: 'Россия' }), {
      ...CITY,
      country: '',
    })

    expect(next.country).toBe('Россия')
  })
})

describe('applyAddressSuggestion', () => {
  it('заполняет адрес, индекс и координаты, не меняя часовой пояс', () => {
    const next = applyAddressSuggestion(manualValues(), ADDRESS, {
      showPostalCode: true,
    })

    expect(next.address).toBe('ул. Ленина, 1')
    expect(next.postalCode).toBe('620000')
    expect(next.latitude).toBe('56.8389')
    expect(next.longitude).toBe('60.5975')
    expect(next.timeZoneId).toBe('Asia/Yekaterinburg')
    expect(next.city).toBe('Москва')
    expect(next.country).toBe('Россия')
  })

  it('null-координаты не затирают введённые руками', () => {
    const next = applyAddressSuggestion(
      manualValues(),
      { ...ADDRESS, latitude: null, longitude: null, postalCode: null },
      { showPostalCode: true }
    )

    expect(next.latitude).toBe('55.7558')
    expect(next.longitude).toBe('37.6173')
    expect(next.postalCode).toBe('101000')
  })

  it('скрытый индекс не пишется', () => {
    const next = applyAddressSuggestion(manualValues(), ADDRESS, {
      showPostalCode: false,
    })

    expect(next.postalCode).toBe('101000')
    expect(next.address).toBe('ул. Ленина, 1')
  })

  it('без улицы в поле адреса уходит display', () => {
    const next = applyAddressSuggestion(
      manualValues(),
      { ...ADDRESS, address: null },
      { showPostalCode: true }
    )

    expect(next.address).toBe(ADDRESS.display)
  })
})

describe('addressSuggestionSnapshot', () => {
  it('запоминает только заполненные поля', () => {
    expect(addressSuggestionSnapshot(ADDRESS, { showPostalCode: true })).toEqual(
      {
        address: 'ул. Ленина, 1',
        postalCode: '620000',
        latitude: '56.8389',
        longitude: '60.5975',
      }
    )
    expect(
      addressSuggestionSnapshot(ADDRESS, { showPostalCode: false })
    ).toEqual({
      address: 'ул. Ленина, 1',
      latitude: '56.8389',
      longitude: '60.5975',
    })
    expect(
      addressSuggestionSnapshot(
        { ...ADDRESS, latitude: null, longitude: null },
        { showPostalCode: true }
      )
    ).toEqual({ address: 'ул. Ленина, 1', postalCode: '620000' })
  })
})

describe('clearSuggestedFields', () => {
  it('очищает подсказанное и оставляет введённое руками', () => {
    const snapshot = addressSuggestionSnapshot(ADDRESS, {
      showPostalCode: true,
    })
    // Адрес и широта пришли из подсказки, индекс и долгота введены руками.
    const values: SalonFormValues = {
      ...manualValues(),
      address: 'ул. Ленина, 1',
      latitude: '56.8389',
    }

    const next = clearSuggestedFields(values, snapshot)

    expect(next.address).toBe('')
    expect(next.latitude).toBe('')
    expect(next.postalCode).toBe('101000')
    expect(next.longitude).toBe('37.6173')
  })

  it('без снимка возвращает значения как есть', () => {
    const values = manualValues()

    expect(clearSuggestedFields(values, null)).toBe(values)
  })
})

describe('detachSuggestedField', () => {
  it('правка адреса снимает индекс и координаты выбранного дома', () => {
    const values = applyAddressSuggestion(manualValues(), ADDRESS, {
      showPostalCode: true,
    })
    const snapshot = addressSuggestionSnapshot(ADDRESS, {
      showPostalCode: true,
    })

    const next = detachSuggestedField(
      { ...values, address: 'ул. Ленина, 1а' },
      snapshot,
      'address'
    )

    expect(next.address).toBe('ул. Ленина, 1а')
    expect(next.postalCode).toBe('')
    expect(next.latitude).toBe('')
    expect(next.longitude).toBe('')
  })

  it('правка координаты не трогает остальные поля', () => {
    const values = applyAddressSuggestion(manualValues(), ADDRESS, {
      showPostalCode: true,
    })
    const snapshot = addressSuggestionSnapshot(ADDRESS, {
      showPostalCode: true,
    })

    const next = detachSuggestedField(
      { ...values, latitude: '56.9' },
      snapshot,
      'latitude'
    )

    expect(next.latitude).toBe('56.9')
    expect(next.address).toBe('ул. Ленина, 1')
    expect(next.postalCode).toBe('620000')
    expect(next.longitude).toBe('60.5975')
  })

  it('не трогает координаты, введённые руками', () => {
    const values = manualValues()
    const snapshot = addressSuggestionSnapshot(
      { ...ADDRESS, latitude: null, longitude: null },
      { showPostalCode: true }
    )

    const next = detachSuggestedField(values, snapshot, 'address')

    expect(next.latitude).toBe('55.7558')
    expect(next.longitude).toBe('37.6173')
    expect(next.address).toBe('ул. Ручная, 2')
  })
})
