import { apiRequest } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'
import type { Page } from '@/lib/paging'
import { isBarberBookingPage } from '@/lib/response-guards'

/**
 * Записи клиентов глазами барбера.
 *
 * `GET api/barber/bookings` отдаёт записи профиля из claim'а `barber_profile_id`:
 * query-параметра с профилем нет, поэтому чужой список запросить нельзя. Профиль
 * салона приходит в поясе `salon.timeZoneId`, а даты остаются ISO-инстантами:
 * форматирует UI, а не транспорт.
 */

export type BarberBookingScope = 'upcoming' | 'past'

export type BarberBookingCustomer = {
  displayName: string
  /** Телефон — PII: только строка списка и `tel:`-ссылка. */
  phoneNumber: string | null
}

export type BarberBooking = {
  id: string
  status: 'Pending' | 'Confirmed' | 'Cancelled' | 'Completed' | 'NoShow'
  startAt: string
  endAt: string
  price: number
  customerNote: string | null
  barberNote: string | null
  customer: BarberBookingCustomer
  service: { name: string }
  salon: { name: string; address: string; city: string; timeZoneId: string }
}

/** Двадцать строк на страницу — дефолт сервера; больше кабинет не просит. */
export const BARBER_BOOKINGS_PAGE_SIZE = 20

export async function fetchBarberBookings(
  scope: BarberBookingScope,
  cursor?: string | null,
  signal?: AbortSignal
): Promise<Page<BarberBooking>> {
  const query = new URLSearchParams({
    scope,
    pageSize: String(BARBER_BOOKINGS_PAGE_SIZE),
  })

  if (cursor) {
    query.set('cursor', cursor)
  }

  // Чтение без CSRF: токен нужен только мутациям.
  return apiRequest<Page<BarberBooking>>(
    `/barber/bookings?${query.toString()}`,
    signal ? { signal } : {},
    { validate: isBarberBookingPage }
  )
}

/** `Pending → Confirmed`; ответ — `204` без тела. */
export async function confirmBooking(id: string): Promise<void> {
  await mutateWithCsrf<void>(`/bookings/${encodeURIComponent(id)}/confirmation`, {
    method: 'POST',
  })
}

/** `Confirmed → Completed`. */
export async function completeBooking(id: string): Promise<void> {
  await mutateWithCsrf<void>(`/bookings/${encodeURIComponent(id)}/completion`, {
    method: 'POST',
  })
}

/** `Confirmed → NoShow`; раньше `start_at` сервер ответит `400`. */
export async function markBookingNoShow(id: string): Promise<void> {
  await mutateWithCsrf<void>(`/bookings/${encodeURIComponent(id)}/no-show`, {
    method: 'POST',
  })
}

/** `Pending|Confirmed → Cancelled`; отмена освобождает слот. */
export async function cancelBooking(id: string): Promise<void> {
  await mutateWithCsrf<void>(`/bookings/${encodeURIComponent(id)}/cancellation`, {
    method: 'POST',
  })
}

/**
 * Заметка барбера; пустая строка очищает её.
 *
 * Обрезку по краям и `null` для пустой строки делает сервер — клиент повторяет
 * правило только для счётчика и блокировки кнопки.
 */
export async function saveBarberNote(id: string, note: string): Promise<void> {
  await mutateWithCsrf<void>(`/bookings/${encodeURIComponent(id)}/barber-note`, {
    method: 'PUT',
    body: JSON.stringify({ note }),
  })
}
