import { apiRequest } from '@/lib/api'
import { isAdditionalServiceList } from '@/lib/response-guards'

/**
 * Подсказки фильтра «дополнительные услуги».
 *
 * `GET api/barbers/additional-services` отдаёт различные значения из карточек
 * активных и подтверждённых барберов (до 100 строк, по алфавиту). Это не справочник:
 * фильтр поиска принимает подстроку, поэтому значение из ответа — удобный пример,
 * а не обязательный выбор. Запрос вспомогательный: его ошибка не должна ломать поиск.
 */

/** Различные дополнительные услуги в порядке сервера (по значению). */
export function fetchAdditionalServices(
  signal?: AbortSignal
): Promise<string[]> {
  return apiRequest<string[]>(
    '/barbers/additional-services',
    { signal },
    { validate: isAdditionalServiceList }
  )
}
