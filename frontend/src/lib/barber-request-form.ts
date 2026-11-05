import { BARBER_REQUEST_LIMITS } from '@/lib/barber-requests'

/**
 * Клиентские проверки формы заявки «Стать барбером».
 *
 * Вынесены из компонента: чистая функция тестируется без рендера, а файл формы
 * остаётся файлом одного компонента (fast refresh). Границы берутся из
 * `BARBER_REQUEST_LIMITS`, то есть из домена: 121-й символ имени или стаж 71
 * отклоняются до запроса. `about` необязателен — пустая строка уходит на сервер
 * как `null`, а не как «».
 */

export type BarberRequestFormValues = {
  displayName: string
  yearsOfExperience: string
  city: string
  about: string
}

export const BARBER_REQUEST_FIELD_ORDER = [
  'displayName',
  'yearsOfExperience',
  'city',
  'about',
] as const

export type BarberRequestField = (typeof BARBER_REQUEST_FIELD_ORDER)[number]

export type BarberRequestFormErrors = Partial<
  Record<BarberRequestField, string>
>

const YEARS_REQUIRED_ERROR = 'Укажите стаж в годах.'
const YEARS_RANGE_ERROR = `Стаж — целое число от 1 года до ${BARBER_REQUEST_LIMITS.yearsOfExperience} лет.`
const DISPLAY_NAME_REQUIRED_ERROR = 'Введите имя, которое увидят клиенты.'
const CITY_REQUIRED_ERROR = 'Укажите город.'

export function validateBarberRequestForm(
  values: BarberRequestFormValues
): BarberRequestFormErrors {
  const errors: BarberRequestFormErrors = {}
  const displayName = values.displayName.trim()
  const city = values.city.trim()
  const years = values.yearsOfExperience.trim()

  if (!displayName) {
    errors.displayName = DISPLAY_NAME_REQUIRED_ERROR
  } else if (displayName.length > BARBER_REQUEST_LIMITS.displayName) {
    errors.displayName = `Имя — не длиннее ${BARBER_REQUEST_LIMITS.displayName} символов.`
  }

  if (!years) {
    errors.yearsOfExperience = YEARS_REQUIRED_ERROR
  } else {
    const parsed = Number(years)

    if (
      !Number.isInteger(parsed) ||
      parsed < 1 ||
      parsed > BARBER_REQUEST_LIMITS.yearsOfExperience
    ) {
      errors.yearsOfExperience = YEARS_RANGE_ERROR
    }
  }

  if (!city) {
    errors.city = CITY_REQUIRED_ERROR
  } else if (city.length > BARBER_REQUEST_LIMITS.city) {
    errors.city = `Город — не длиннее ${BARBER_REQUEST_LIMITS.city} символов.`
  }

  if (values.about.trim().length > BARBER_REQUEST_LIMITS.about) {
    errors.about = `О себе — не длиннее ${BARBER_REQUEST_LIMITS.about} символов.`
  }

  return errors
}
