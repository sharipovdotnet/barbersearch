import { apiRequest } from '@/lib/api'
import type { ServiceCategory } from '@/lib/barber-profile-labels'
import { isSpecializationList } from '@/lib/response-guards'

/**
 * Публичный справочник специализаций.
 *
 * `GET api/specializations` отдаёт голый массив активных строк без пагинации:
 * справочник маленький, витрине нужен весь список для выпадающего фильтра.
 * Категория приходит вместе со строкой: у специализации она ровно одна.
 */

export type SpecializationOption = {
  id: string
  name: string
  description: string | null
  category: ServiceCategory
}

/** Активные специализации в порядке сервера (по имени). */
export function fetchSpecializations(
  signal?: AbortSignal
): Promise<SpecializationOption[]> {
  return apiRequest<SpecializationOption[]>(
    '/specializations',
    { signal },
    { validate: isSpecializationList }
  )
}
