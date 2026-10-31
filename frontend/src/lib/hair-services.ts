import { apiRequest } from '@/lib/api'
import type { ServiceCategory } from '@/lib/barber-profile-labels'
import { isHairServiceList } from '@/lib/response-guards'

/**
 * Публичный справочник услуг.
 *
 * `GET api/hair-services` отдаёт голый массив активных строк без пагинации.
 * Длительность и описание UI фильтра не читает, поэтому в типе их нет: guard
 * пропускает лишние поля сервера.
 */

export type HairServiceOption = {
  id: string
  name: string
  category: ServiceCategory
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
