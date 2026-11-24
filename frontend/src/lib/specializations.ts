import { apiRequest } from '@/lib/api'
import { isSpecializationList } from '@/lib/response-guards'

/**
 * Публичный справочник специализаций.
 *
 * `GET api/specializations` отдаёт голый массив активных строк без пагинации:
 * справочник маленький, витрине нужен весь список для выпадающего фильтра.
 */

export type SpecializationOption = {
  id: string
  name: string
  description: string | null
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
