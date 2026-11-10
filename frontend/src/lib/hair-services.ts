import { apiRequest } from '@/lib/api'
import type { ServiceCategory } from '@/lib/barber-profile-labels'
import { isHairServiceList } from '@/lib/response-guards'

/**
 * Публичный справочник услуг.
 *
 * `GET api/hair-services` отдаёт голый массив активных строк без пагинации.
 * `defaultDurationMinutes` — длительность по умолчанию: ею предзаполняется диалог
 * добавления услуги в прайс барбера, поэтому поле обязательно в контракте.
 */

export type HairServiceOption = {
  id: string
  name: string
  category: ServiceCategory
  defaultDurationMinutes: number
}

/** Активные услуги в порядке сервера (категория, затем имя). */
export function fetchHairServices(
  signal?: AbortSignal
): Promise<HairServiceOption[]> {
  return apiRequest<HairServiceOption[]>(
    '/hair-services',
    { signal },
    { validate: isHairServiceList }
  )
}
