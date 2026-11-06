import { mutateWithCsrf } from '@/lib/csrf'
import { isCreatedId } from '@/lib/response-guards'

/**
 * Запись клиента силами барбера.
 *
 * `POST api/barber/bookings` — cookie-мутация: клиент идёт через `mutateWithCsrf`,
 * а профиль барбера сервер берёт из claim'а, поэтому в теле его нет. Пользователь может
 * быть зарегистрированным (`customerId` из `GET api/barber/clients`, роль не важна) или
 * новым: тогда аккаунт не создаётся, а в брони сохраняются имя и телефон гостя.
 * Смешивать два варианта нельзя.
 * Ключ идемпотентности создаётся на попытку и переживает сетевые повторы.
 */

/** Пределы гостевых полей совпадают с серверными (`booking.create.guest*`). */
export const MAX_GUEST_FIRST_NAME_LENGTH = 100
export const MAX_GUEST_LAST_NAME_LENGTH = 100
export const MAX_GUEST_PHONE_LENGTH = 32

type CreateBarberBookingBase = {
  barberServiceId: string
  salonId: string
  startAt: string
  endAt: string
  idempotencyKey: string
  customerNote: string | null
}

/** Запись зарегистрированного пользователя: аккаунт уже есть. */
export type CreateRegisteredBarberBookingInput = CreateBarberBookingBase & {
  customerId: string
}

/**
 * Запись человека без аккаунта.
 *
 * Достаточно имени и телефона: email не нужен, потому что аккаунта и рассылки нет.
 * Фамилия необязательна — в списке барбера имя собирается из двух полей.
 */
export type CreateGuestBarberBookingInput = CreateBarberBookingBase & {
  customerId: null
  guestFirstName: string
  guestLastName: string | null
  guestPhone: string
}

export type CreateBarberBookingInput =
  | CreateRegisteredBarberBookingInput
  | CreateGuestBarberBookingInput

/** Создаёт запись и возвращает её идентификатор. */
export async function createBarberBooking(
  input: CreateBarberBookingInput
): Promise<string> {
  const created = await mutateWithCsrf<{ id: string }>('/barber/bookings', {
    method: 'POST',
    body: JSON.stringify(input),
    validate: isCreatedId,
  })

  return created.id
}

/** «Имя Фамилия» для подписи выбранного гостя и объявления после записи. */
export function formatGuestName(
  firstName: string,
  lastName: string | null
): string {
  return `${firstName.trim()} ${lastName?.trim() ?? ''}`.trim()
}
