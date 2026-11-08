import { t } from '@/i18n'
import { ApiError } from '@/lib/api'
import { describeApiFailure } from '@/lib/errors'
import {
  defaultIsListed,
  isListedAllowed,
  isPlaceKind,
  type PlaceKind,
} from '@/lib/place-kind'

/**
 * Поля формы салона, её валидация и тела запросов.
 *
 * Модуль общий для админского диалога и кабинета барбера: правила адреса, координат
 * и контактов не должны расходиться между двумя входами. UI-поля живут в
 * `components/salon/salon-form.tsx`, а чистые правила и сборка тел — здесь (§10.1).
 */

export type SalonScope = 'admin' | 'owner'

export type SalonFormMode = 'create' | 'edit'

export type SalonFormValues = {
  name: string
  address: string
  city: string
  country: string
  postalCode: string
  timeZoneId: string
  latitude: string
  longitude: string
  phoneNumber: string
  websiteUrl: string
  /** Тип места: влияет на подписи и на доступность каталога. */
  kind: PlaceKind
  /** «Показывать в каталоге»; у `Home`/`Mobile` всегда `false`. */
  isListed: boolean
}

export type SalonFormErrors = Partial<Record<keyof SalonFormValues, string>>

/**
 * Значения, из которых форма берёт начальное состояние.
 *
 * `BarberSalonSummary` отдаёт полный набор полей салона, поэтому правка владельца
 * заполняет адрес, координаты и контакты настоящими значениями. Необязательность
 * оставлена для частичных источников: пропуски форма заполняет пустыми строками.
 */
export type SalonFormSalon = {
  name: string
  address: string
  city: string
  country?: string | null
  postalCode?: string | null
  timeZoneId: string
  latitude?: number | null
  longitude?: number | null
  phoneNumber?: string | null
  websiteUrl?: string | null
  /** Тип места; `null`/отсутствие — старое место по умолчанию барбершоп. */
  kind?: PlaceKind | null
  /** Скрытость места; отсутствие — значение по умолчанию для типа. */
  isListed?: boolean | null
}

export type SalonLocationInput = {
  address: string
  city: string
  country: string
  timeZoneId: string
  latitude: number
  longitude: number
  postalCode: string | null
}

export type SalonContactInput = {
  phoneNumber: string | null
  websiteUrl: string | null
}

export type SalonInput = SalonLocationInput &
  SalonContactInput & {
    name: string
    /** Тип места уходит строкой — имя значения enum в API. */
    kind: PlaceKind
    /** Скрытое место не попадает в каталог; у `Home`/`Mobile` всегда `false`. */
    isListed: boolean
  }

export const MAX_PHONE_LENGTH = 32
export const MAX_URL_LENGTH = 2048
export const MIN_LATITUDE = -90
export const MAX_LATITUDE = 90
export const MIN_LONGITUDE = -180
export const MAX_LONGITUDE = 180

/** IANA-зоны: свободный ввод часового пояса ломает расчёт слотов. */
function supportedTimeZones(): readonly string[] {
  try {
    return Intl.supportedValuesOf('timeZone')
  } catch {
    return []
  }
}

const TIME_ZONES = supportedTimeZones()

const DEFAULT_TIME_ZONE =
  TIME_ZONES.find(
    (zone) => zone === new Intl.DateTimeFormat().resolvedOptions().timeZone
  ) ?? 'Europe/Moscow'

/**
 * Список зон для `<select>`: текущее значение всегда в нём, даже если браузер его
 * не знает — иначе сохранённый пояс нельзя было бы оставить без изменения.
 */
export function salonTimeZones(current: string): readonly string[] {
  return TIME_ZONES.includes(current) ? TIME_ZONES : [current, ...TIME_ZONES]
}

export function emptySalonForm(): SalonFormValues {
  return {
    name: '',
    address: '',
    city: '',
    country: '',
    postalCode: '',
    timeZoneId: DEFAULT_TIME_ZONE,
    latitude: '',
    longitude: '',
    phoneNumber: '',
    websiteUrl: '',
    kind: 'Salon',
    isListed: defaultIsListed('Salon'),
  }
}

/** Начальные значения формы: отсутствующие в DTO поля остаются пустыми. */
export function salonFormValues(
  salon: SalonFormSalon | null | undefined
): SalonFormValues {
  const kind = isPlaceKind(salon?.kind) ? salon.kind : 'Salon'
  // Скрытое место не может быть «показано», даже если источник пришёл с флагом.
  const isListed =
    isListedAllowed(kind) && typeof salon?.isListed === 'boolean'
      ? salon.isListed
      : defaultIsListed(kind)

  return {
    name: salon?.name ?? '',
    address: salon?.address ?? '',
    city: salon?.city ?? '',
    country: salon?.country ?? '',
    postalCode: salon?.postalCode ?? '',
    timeZoneId: salon?.timeZoneId ?? DEFAULT_TIME_ZONE,
    latitude: salon?.latitude != null ? String(salon.latitude) : '',
    longitude: salon?.longitude != null ? String(salon.longitude) : '',
    phoneNumber: salon?.phoneNumber ?? '',
    websiteUrl: salon?.websiteUrl ?? '',
    kind,
    isListed,
  }
}

export function validateSalonForm(values: SalonFormValues): SalonFormErrors {
  const errors: SalonFormErrors = {}

  // Длину названия сервер не ограничивает — выдумывать лимит нельзя.
  if (values.name.trim() === '') {
    errors.name = t('Введите название.')
  }

  if (values.address.trim() === '') {
    errors.address = t('Введите адрес.')
  }

  if (values.city.trim() === '') {
    errors.city = t('Введите город.')
  }

  if (values.country.trim() === '') {
    errors.country = t('Введите страну.')
  }

  if (values.timeZoneId.trim() === '') {
    errors.timeZoneId = t('Выберите часовой пояс из списка.')
  }

  const latitude = Number(values.latitude)

  if (
    values.latitude.trim() === '' ||
    !Number.isFinite(latitude) ||
    latitude < MIN_LATITUDE ||
    latitude > MAX_LATITUDE
  ) {
    errors.latitude = t('От {min} до {max}.', {
      min: MIN_LATITUDE,
      max: MAX_LATITUDE,
    })
  }

  const longitude = Number(values.longitude)

  if (
    values.longitude.trim() === '' ||
    !Number.isFinite(longitude) ||
    longitude < MIN_LONGITUDE ||
    longitude > MAX_LONGITUDE
  ) {
    errors.longitude = t('От {min} до {max}.', {
      min: MIN_LONGITUDE,
      max: MAX_LONGITUDE,
    })
  }

  const website = values.websiteUrl.trim()

  if (website !== '' && !isHttpUrl(website)) {
    errors.websiteUrl = t('Адрес сайта начинается с http:// или https://.')
  }

  if (values.phoneNumber.trim().length > MAX_PHONE_LENGTH) {
    errors.phoneNumber = t('Не длиннее {max} символов.', {
      max: MAX_PHONE_LENGTH,
    })
  }

  if (!isListedAllowed(values.kind) && values.isListed) {
    errors.isListed = t('Для этого типа места каталог недоступен.')
  }

  if (
    (values.kind === 'Salon' || values.kind === 'RentalStudio') &&
    !values.isListed
  ) {
    errors.isListed = t('Барбершоп и студию нельзя скрыть из каталога.')
  }

  return errors
}

export function salonLocationInput(
  values: SalonFormValues
): SalonLocationInput {
  return {
    address: values.address.trim(),
    city: values.city.trim(),
    country: values.country.trim(),
    timeZoneId: values.timeZoneId,
    latitude: Number(values.latitude),
    longitude: Number(values.longitude),
    postalCode: optional(values.postalCode),
  }
}

export function salonContactInput(values: SalonFormValues): SalonContactInput {
  return {
    phoneNumber: optional(values.phoneNumber),
    websiteUrl: optional(values.websiteUrl),
  }
}

export function salonCreateInput(values: SalonFormValues): SalonInput {
  return {
    name: values.name.trim(),
    kind: values.kind,
    // Домен запрещает показывать Home/Mobile в каталоге, поэтому флаг
    // нормализуется здесь, а не отправляется как есть.
    isListed: isListedAllowed(values.kind) ? values.isListed : false,
    ...salonLocationInput(values),
    ...salonContactInput(values),
  }
}

/** Пустая строка не отправляется: сервер понимает `null` как «поля нет». */
function optional(value: string): string | null {
  const trimmed = value.trim()

  return trimmed === '' ? null : trimmed
}

function isHttpUrl(value: string): boolean {
  if (value.length > MAX_URL_LENGTH) {
    return false
  }

  try {
    const url = new URL(value)

    return url.protocol === 'http:' || url.protocol === 'https:'
  } catch {
    return false
  }
}

/** Коды серверных ошибок, которые показываются у конкретного поля формы. */
export type SalonField =
  | 'name'
  | 'address'
  | 'city'
  | 'country'
  | 'timeZoneId'
  | 'websiteUrl'
  | 'isListed'

const SALON_FIELD_MESSAGES: Readonly<
  Record<SalonField, { codes: readonly string[]; message: string }>
> = {
  name: { codes: ['salon.name'], message: 'Введите название.' },
  address: { codes: ['salon.address'], message: 'Введите адрес.' },
  city: {
    // `salon.city` — код домена, `salon.city.invalid` — прежний код клиента.
    codes: ['salon.city', 'salon.city.invalid'],
    message: 'Город не может быть пустым.',
  },
  country: { codes: ['salon.country'], message: 'Введите страну.' },
  timeZoneId: {
    codes: ['salon.timeZoneId.unknown', 'salon.timeZoneId'],
    message: 'Неизвестный часовой пояс. Выберите зону из списка.',
  },
  websiteUrl: {
    codes: ['salon.websiteUrl'],
    message: 'Сайт должен быть адресом http(s) не длиннее 2048 символов.',
  },
  isListed: {
    codes: ['salon.isListed.kind'],
    message: 'Барбершоп и студию нельзя скрыть из каталога.',
  },
}

/**
 * Ошибки полей формы салона из ответа сервера.
 *
 * Домен проверяет поля сам, и опечатка в часовом поясе или пустой город приходят кодом,
 * а не текстом; показывать их нужно у поля, иначе пользователь ищет причину в общем alert.
 */
export function salonFieldErrors(
  cause: unknown
): Partial<Record<SalonField, string>> {
  if (!(cause instanceof ApiError)) {
    return {}
  }

  const errors: Partial<Record<SalonField, string>> = {}

  for (const [field, rule] of Object.entries(SALON_FIELD_MESSAGES) as [
    SalonField,
    { codes: readonly string[]; message: string },
  ][]) {
    if (rule.codes.some((code) => cause.hasCode(code))) {
      errors[field] = t(rule.message)
    }
  }

  return errors
}

/**
 * Ошибка сохранения салона.
 *
 * `403` у админа и у владельца значит разное: администратору закрыт раздел, а барберу —
 * чужой салон или салон без профиля. Английский `detail` наружу не показывается.
 */
export function describeSalonSaveError(
  cause: unknown,
  scope: SalonScope,
  mode: SalonFormMode
): string {
  const fallback =
    mode === 'create'
      ? 'Не удалось создать салон. Попробуйте ещё раз.'
      : 'Не удалось сохранить салон. Попробуйте ещё раз.'

  if (!(cause instanceof ApiError)) {
    return t(fallback)
  }

  if (cause.status === 403 || cause.hasCode('auth.forbidden')) {
    if (scope === 'owner') {
      if (cause.hasCode('barberProfile.notFound')) {
        return t('Профиль барбера не найден. Создайте профиль и повторите.')
      }

      return t('Это чужой салон — править его может только владелец.')
    }

    return t('Раздел доступен только администратору.')
  }

  if (cause.status === 404 || cause.hasCode('salon.notFound')) {
    return t('Салон не найден — обновите список.')
  }

  if (
    cause.status === 409 &&
    cause.codes.some((code) => code.endsWith('update.concurrencyConflict'))
  ) {
    return t('Запись изменили одновременно. Обновите список.')
  }

  if (cause.hasCode('salon.timeZoneId.unknown')) {
    return t('Неизвестный часовой пояс. Выберите зону из списка.')
  }

  if (cause.hasCode('salon.city') || cause.hasCode('salon.city.invalid')) {
    return t('Город не может быть пустым.')
  }

  if (cause.hasCode('salon.isListed.kind')) {
    return t('Барбершоп и студию нельзя скрыть из каталога.')
  }

  if (cause.hasCode('salon.kind.unknown')) {
    return t(
      'Неизвестный тип места. Обновите страницу и попробуйте ещё раз.'
    )
  }

  if (cause.status === 429) {
    return t('Слишком много действий подряд. Подождите минуту.')
  }

  return describeApiFailure(cause, fallback)
}
