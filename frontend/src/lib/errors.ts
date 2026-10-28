import { ApiError } from '@/lib/api'

/**
 * Единый перевод сетевых сбоев в текст для кабинета.
 *
 * Ответы API приходят как ProblemDetails, поэтому клиентские ветки смотрят на код
 * ошибки, а не на английский `detail` (см. `lib/api.ts`).
 */
export function describeApiFailure(error: unknown, fallback: string): string {
  if (!(error instanceof ApiError)) {
    return fallback
  }

  if (error.status === 0) {
    return 'Нет связи с сервером. Проверьте подключение и повторите попытку.'
  }

  if (error.status === 401) {
    return 'Сессия истекла — войдите снова.'
  }

  if (error.status === 429) {
    return 'Слишком много запросов. Подождите минуту и повторите.'
  }

  return fallback
}
