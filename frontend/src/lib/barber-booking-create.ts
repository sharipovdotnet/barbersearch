import { mutateWithCsrf } from '@/lib/csrf'
import { isCreatedId } from '@/lib/response-guards'

/**
 * Запись клиента силами барбера.
 *
 * `POST api/barber/bookings` — cookie-мутация: клиент идёт через `mutateWithCsrf`,
 * а профиль барбера сервер берёт из claim'а, поэтому в теле его нет. Клиент должен
 * быть найден через `GET api/barber/clients`; `customerId` — его идентификатор.
 * Ключ идемпотентности создаётся на попытку и переживает сетевые повторы.
 */

export type CreateBarberBookingInput = {
  customerId: string
  barberServiceId: string
  salonId: string
  startAt: string
  endAt: string
  idempotencyKey: string
  customerNote: string | null
}

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
