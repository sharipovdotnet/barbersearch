import { apiRequest } from '@/lib/api'
import type { BarberGrade } from '@/lib/barber-grade'
import type { Page } from '@/lib/paging'
import { isBarberSearchPage } from '@/lib/response-guards'

/**
 * Публичный поиск барберов.
 *
 * `GET api/barbers` не умеет текстовый поиск и не отдаёт цены, специализации или
 * услуги: витрина фильтрует только по городу и дате. Город сервер сравнивает точно
 * (`salon.City == city`), поэтому клиент лишь обрезает пробелы и не меняет регистр.
 * Справочные `specializationId`/`hairServiceId` API принимает, но UI их пока не шлёт:
 * списочных GET для справочников нет.
 */

export type BarberSearchFilters = {
  /** Город салона; пустая строка — без фильтра. */
  city: string
  /** `YYYY-MM-DD` либо пустая строка — без фильтра. */
  date: string
}

/** Строка выдачи; полей цены и услуг здесь нет по контракту. */
export type BarberSearchItem = {
  id: string
  displayName: string
  grade: BarberGrade
  yearsOfExperience: number
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

  if (cursor) {
    query.set('cursor', cursor)
  }

  return apiRequest<Page<BarberSearchItem>>(
    `/barbers?${query.toString()}`,
    { signal },
    { validate: isBarberSearchPage }
  )
}
