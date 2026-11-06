import { apiRequest } from '@/lib/api'
import { isSalonDirectoryList } from '@/lib/response-guards'

/**
 * Публичный каталог салонов для выбора места работы.
 *
 * `GET api/salons` отдаёт голый массив активных строк без пагинации: это справочник,
 * как `GET api/specializations`, а не список с пейджером. Пустые фильтры не
 * отправляются — сервер трактует их как «без фильтра», но лишний `?city=` только
 * засоряет URL и кэш.
 */

export type SalonDirectoryItem = {
  id: string
  name: string
  city: string
  address: string
  timeZoneId: string
}

/** Минимум символов поиска: один символ вернул бы почти весь справочник. */
export const SALON_SEARCH_MIN_LENGTH = 2

/** Ограничение сервера на длину `city`/`search`; длиннее — `400`. */
export const SALON_FILTER_MAX_LENGTH = 100

export type SalonDirectoryFilters = {
  city?: string
  search?: string
  signal?: AbortSignal
}

export function salonDirectoryUrl({ city, search }: SalonDirectoryFilters = {}): string {
  const params = new URLSearchParams()
  const trimmedCity = city?.trim()
  const trimmedSearch = search?.trim()

  if (trimmedCity) {
    params.set('city', trimmedCity)
  }

  if (trimmedSearch) {
    params.set('search', trimmedSearch)
  }

  const query = params.toString()

  return query === '' ? '/salons' : `/salons?${query}`
}

export function fetchSalonDirectory({
  city,
  search,
  signal,
}: SalonDirectoryFilters = {}): Promise<SalonDirectoryItem[]> {
  return apiRequest<SalonDirectoryItem[]>(
    salonDirectoryUrl({ city, search }),
    signal ? { signal } : {},
    { validate: isSalonDirectoryList }
  )
}
