import { apiRequest } from '@/lib/api'
import type { BarberGrade } from '@/lib/barber-grade'
import type { Language } from '@/lib/barber-profile-labels'
import type { Page } from '@/lib/paging'
import { isBarberSearchPage } from '@/lib/response-guards'

/**
 * Публичный поиск барберов.
 *
 * `GET api/barbers` не умеет текстовый поиск и не отдаёт цены, специализации или
 * услуги: витрина фильтрует по городу, дате, языку, специализации и услуге.
 * Город сервер сравнивает точно (`salon.City == city`), поэтому клиент лишь
 * обрезает пробелы и не меняет регистр. Справочные `specializationId`,
 * `hairServiceId` и `language` уходят только при выбранном значении: пустая
 * строка — «фильтра нет», и сервер такой параметр не получает вовсе.
 */

export type BarberSearchFilters = {
  /** Город салона; пустая строка — без фильтра. */
  city: string
  /** `YYYY-MM-DD` либо пустая строка — без фильтра. */
  date: string
  /** `''` — без фильтра. */
  specializationId: string
  /** `''` — без фильтра. */
  hairServiceId: string
  /** `''` — без фильтра; один язык, не мультивыбор. */
  language: Language | ''
}

/** Свежий объект пустых фильтров: форма и страница не делят одно состояние. */
export function emptyBarberSearchFilters(): BarberSearchFilters {
  return {
    city: '',
    date: '',
    specializationId: '',
    hairServiceId: '',
    language: '',
  }
}

/**
 * Нормализует фильтры перед отправкой: город обрезается, остальные значения
 * передаются как есть. Общая функция нужна и странице, и хуку, иначе новые поля
 * теряются по дороге.
 */
export function normalizeBarberSearchFilters(
  filters: BarberSearchFilters
): BarberSearchFilters {
  return {
    city: filters.city.trim(),
    date: filters.date,
    specializationId: filters.specializationId,
    hairServiceId: filters.hairServiceId,
    language: filters.language,
  }
}

/** Строка выдачи; полей цены и услуг здесь нет по контракту. */
export type BarberSearchItem = {
  id: string
  displayName: string
  grade: BarberGrade
  yearsOfExperience: number
  /** Языки общения в том же виде, что и в карточке профиля. */
  languages: Language[]
  /** `null` — отзывов ещё нет; такие барберы идут в конце выдачи. */
  rating: number | null
  reviewsCount: number
  isVerified: boolean
  primarySalonId: string | null
  primarySalonName: string | null
  city: string | null
}

/** Ровно 3 колонки × 4 ряда на десктопе. */
export const BARBER_SEARCH_PAGE_SIZE = 12

/**
 * Запрашивает одну страницу выдачи. `cursor` передаётся из прошлого ответа как есть;
 * `null` — первая страница.
 */
export async function searchBarbers(
  filters: BarberSearchFilters,
  cursor: string | null,
  signal: AbortSignal
): Promise<Page<BarberSearchItem>> {
  const query = new URLSearchParams({
    pageSize: String(BARBER_SEARCH_PAGE_SIZE),
  })

  const city = filters.city.trim()
  if (city !== '') {
    query.set('city', city)
  }

  if (filters.date !== '') {
    query.set('date', filters.date)
  }

  if (filters.specializationId !== '') {
    query.set('specializationId', filters.specializationId)
  }

  if (filters.hairServiceId !== '') {
    query.set('hairServiceId', filters.hairServiceId)
  }

  if (filters.language !== '') {
    query.set('language', filters.language)
  }

  if (cursor) {
    query.set('cursor', cursor)
  }

  return apiRequest<Page<BarberSearchItem>>(
    `/barbers?${query.toString()}`,
    { signal },
    { validate: isBarberSearchPage }
  )
}
