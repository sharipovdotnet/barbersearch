import { apiRequest } from '@/lib/api'
import { isBarberClientList } from '@/lib/response-guards'

/**
 * Поиск зарегистрированного пользователя для ручной записи.
 *
 * `GET api/barber/clients` доступен только роли `Barber` и ищет по email или
 * телефону среди активных пользователей любой роли — клиента, администратора,
 * барбера и т. д. Ответ — не страница, а короткая подсказка: максимум десять
 * строк, без курсора. Контакты — PII, поэтому запрос уходит только по явному
 * вводу барбера, а не при открытии формы.
 */

export type BarberClient = {
  id: string
  firstName: string
  lastName: string
  email: string
  phoneNumber: string | null
}

/** Минимум сервера (`barberClient.search.tooShort`); клиент повторяет его до запроса. */
export const MIN_BARBER_CLIENT_SEARCH = 3

/** Сколько символов сервер принимает в поиске (`barberClient.search.tooLong`). */
export const MAX_BARBER_CLIENT_SEARCH = 100

export async function searchBarberClients(
  search: string,
  signal?: AbortSignal
): Promise<BarberClient[]> {
  const query = new URLSearchParams({ search })

  return apiRequest<BarberClient[]>(
    `/barber/clients?${query.toString()}`,
    signal ? { signal } : {},
    { validate: isBarberClientList }
  )
}

/** «Имя Фамилия» для подписи выбранного клиента и объявления после записи. */
export function formatBarberClientName(client: BarberClient): string {
  return `${client.firstName} ${client.lastName}`.trim()
}
