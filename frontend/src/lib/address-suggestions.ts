import { apiRequest } from '@/lib/api'
import type { SalonFormValues } from '@/lib/salon-form'
import {
  isAddressSuggestionList,
  isCitySuggestionList,
} from '@/lib/response-guards'

/**
 * Подсказки города и адреса для формы салона.
 *
 * Сервер проксирует DaData и сам фильтрует выдачу: клиент только строит URL, зовёт
 * `apiRequest` и раскладывает выбранную подсказку по полям формы. `providerId` —
 * непрозрачный токен провайдера, живёт в состоянии компонента и никогда не попадает
 * в тело запроса (§2 среза). Пустой массив — не ошибка, а «ничего не найдено».
 */

export type CitySuggestion = {
  /** Непрозрачный идентификатор у провайдера; `null`, если провайдер его не вернул. */
  providerId: string | null
  /** Строка для списка: «г. Екатеринбург». */
  display: string
  /** Голое имя города: его и пишем в поле. */
  city: string
  region: string | null
  country: string
  latitude: number | null
  longitude: number | null
}

export type AddressSuggestion = {
  providerId: string | null
  display: string
  /** Улица и дом без города; `null`, когда улицу не удалось выделить. */
  address: string | null
  city: string
  region: string | null
  country: string
  postalCode: string | null
  latitude: number | null
  longitude: number | null
  /** Зарезервирован под следующий срез, сейчас всегда `null`. */
  timeZoneId: string | null
}

/** Минимум символов запроса города: сервер отвечает `400` на более короткую строку. */
export const CITY_SUGGESTIONS_MIN_LENGTH = 2

/** Минимум символов запроса адреса. */
export const ADDRESS_SUGGESTIONS_MIN_LENGTH = 3

/** Больше восьми подсказок сервер не отдаёт — это же значение по умолчанию. */
export const SUGGESTIONS_LIMIT = 8

/** Задержка перед запросом: три нажатия дают один запрос, а не три. */
export const SUGGESTIONS_DEBOUNCE_MS = 250

export type AddressSuggestionFilters = {
  query: string
  /** `providerId` выбранного города: он точнее свободного текста. */
  cityProviderId?: string | null
  /** Свободный текст города, когда подсказка города не выбрана. */
  city?: string | null
  signal?: AbortSignal
}

export function citySuggestionsUrl(query: string): string {
  const params = new URLSearchParams()

  params.set('query', query.trim())
  params.set('limit', String(SUGGESTIONS_LIMIT))

  return `/addresses/cities?${params.toString()}`
}

/**
 * URL подсказок адреса внутри города.
 *
 * `cityProviderId` важнее `city`: сервер игнорирует свободный текст, когда пришёл
 * провайдерский токен. Пустые параметры не отправляются — сервер понимает их как
 * «фильтра нет», но лишний `?city=` только засоряет URL и кэш.
 */
export function addressSuggestionsUrl({
  query,
  cityProviderId,
  city,
}: AddressSuggestionFilters): string {
  const params = new URLSearchParams()
  const providerId = cityProviderId?.trim()
  const cityText = city?.trim()

  params.set('query', query.trim())

  if (providerId) {
    params.set('cityProviderId', providerId)
  } else if (cityText) {
    params.set('city', cityText)
  }

  params.set('limit', String(SUGGESTIONS_LIMIT))

  return `/addresses/suggest?${params.toString()}`
}

export function fetchCitySuggestions(
  query: string,
  signal?: AbortSignal
): Promise<CitySuggestion[]> {
  return apiRequest<CitySuggestion[]>(
    citySuggestionsUrl(query),
    signal ? { signal } : {},
    { validate: isCitySuggestionList }
  )
}

export function fetchAddressSuggestions(
  filters: AddressSuggestionFilters
): Promise<AddressSuggestion[]> {
  return apiRequest<AddressSuggestion[]>(
    addressSuggestionsUrl(filters),
    filters.signal ? { signal: filters.signal } : {},
    { validate: isAddressSuggestionList }
  )
}

/** Поля формы, которые заполняет выбранный адрес. */
export type SuggestedAddressField =
  | 'address'
  | 'postalCode'
  | 'latitude'
  | 'longitude'

/**
 * Снимок подсказанных значений.
 *
 * Хранится в состоянии компонента и отвечает на вопрос «что именно пришло из
 * подсказки»: ручная правка поля выводит его из снимка, а смена города очищает только
 * те поля, которые всё ещё равны снимку. Поле, которого в снимке нет, подсказка не
 * заполняла (например, скрытый индекс).
 */
export type AddressSuggestionSnapshot = Partial<
  Record<SuggestedAddressField, string>
>

/** Поля, которые сбрасываются при смене города. */
const SUGGESTED_FIELDS: readonly SuggestedAddressField[] = [
  'address',
  'postalCode',
  'latitude',
  'longitude',
]

/**
 * Поля, которые теряют смысл при ручной правке улицы.
 *
 * Индекс и координаты относились к выбранному дому: пользователь начал вводить другой
 * адрес, и оставлять их было бы враньём. Координаты, введённые руками, не трогаем —
 * их нет в снимке.
 */
const ADDRESS_DEPENDENT_FIELDS: readonly SuggestedAddressField[] = [
  'postalCode',
  'latitude',
  'longitude',
]

/** Город и страна из подсказки; адрес, индекс и координаты остаются как были. */
export function applyCitySuggestion(
  values: SalonFormValues,
  suggestion: CitySuggestion
): SalonFormValues {
  const country = suggestion.country.trim()

  return {
    ...values,
    city: suggestion.city,
    // Пустая страна не должна затирать введённую руками.
    country: country === '' ? values.country : suggestion.country,
  }
}

/** Улица с домом без города: при `null` сервер отдаёт в `display` весь адрес. */
function suggestedAddress(suggestion: AddressSuggestion): string {
  const address = suggestion.address?.trim() ?? ''

  return address === '' ? suggestion.display : address
}

/** Координата в виде строки формы; `null` не затирает уже введённое значение. */
function coordinate(value: number | null): string | null {
  return value !== null && Number.isFinite(value) ? String(value) : null
}

export function applyAddressSuggestion(
  values: SalonFormValues,
  suggestion: AddressSuggestion,
  { showPostalCode }: { showPostalCode: boolean }
): SalonFormValues {
  const next: SalonFormValues = {
    ...values,
    address: suggestedAddress(suggestion),
  }

  if (showPostalCode) {
    const postalCode = suggestion.postalCode?.trim() ?? ''

    if (postalCode !== '') {
      next.postalCode = postalCode
    }
  }

  const latitude = coordinate(suggestion.latitude)
  const longitude = coordinate(suggestion.longitude)

  if (latitude !== null) {
    next.latitude = latitude
  }

  if (longitude !== null) {
    next.longitude = longitude
  }

  return next
}

/**
 * Снимок ровно тех полей, которые заполнила подсказка.
 *
 * Собирается отдельной функцией, чтобы «что записали» и «что запомнили» не разошлись:
 * скрытый индекс и `null`-координаты в снимок не попадают, значит смена города их не
 * тронет.
 */
export function addressSuggestionSnapshot(
  suggestion: AddressSuggestion,
  { showPostalCode }: { showPostalCode: boolean }
): AddressSuggestionSnapshot {
  const snapshot: AddressSuggestionSnapshot = {
    address: suggestedAddress(suggestion),
  }

  if (showPostalCode) {
    const postalCode = suggestion.postalCode?.trim() ?? ''

    if (postalCode !== '') {
      snapshot.postalCode = postalCode
    }
  }

  const latitude = coordinate(suggestion.latitude)
  const longitude = coordinate(suggestion.longitude)

  if (latitude !== null) {
    snapshot.latitude = latitude
  }

  if (longitude !== null) {
    snapshot.longitude = longitude
  }

  return snapshot
}

/** Смена города очищает только подсказанные поля; введённое руками остаётся. */
export function clearSuggestedFields(
  values: SalonFormValues,
  snapshot: AddressSuggestionSnapshot | null
): SalonFormValues {
  if (snapshot === null) {
    return values
  }

  const next = { ...values }

  for (const field of SUGGESTED_FIELDS) {
    const suggested = snapshot[field]

    if (suggested !== undefined && next[field] === suggested) {
      next[field] = ''
    }
  }

  return next
}

/**
 * Ручная правка подсказанного поля.
 *
 * Правка улицы снимает индекс и координаты выбранного дома (они относились к нему),
 * правка координаты или индекса не трогает остальные поля.
 */
export function detachSuggestedField(
  values: SalonFormValues,
  snapshot: AddressSuggestionSnapshot | null,
  field: SuggestedAddressField
): SalonFormValues {
  if (snapshot === null || field !== 'address') {
    return values
  }

  const next = { ...values }

  for (const dependent of ADDRESS_DEPENDENT_FIELDS) {
    const suggested = snapshot[dependent]

    if (suggested !== undefined && next[dependent] === suggested) {
      next[dependent] = ''
    }
  }

  return next
}
