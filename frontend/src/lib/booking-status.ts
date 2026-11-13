import { t } from '@/i18n'
import type { BookingStatus } from '@/lib/bookings'

/** Подписи и правила жизненного цикла брони — в одном месте, чтобы не расходились. */

const STATUS_LABELS: Record<BookingStatus, string> = {
  Pending: 'Ждёт подтверждения',
  Confirmed: 'Подтверждена',
  Cancelled: 'Отменена',
  Completed: 'Завершена',
  NoShow: 'Неявка',
}

const STATUS_STYLES: Record<BookingStatus, string> = {
  Pending: 'border-warning/30 bg-warning/10 text-warning',
  Confirmed: 'border-success/30 bg-success/10 text-success',
  Completed: 'border-border bg-muted text-foreground',
  Cancelled: 'border-border bg-background text-muted-foreground',
  NoShow: 'border-destructive/30 bg-destructive/10 text-destructive',
}

export function bookingStatusLabel(status: BookingStatus): string {
  return t(STATUS_LABELS[status] ?? status)
}

export function bookingStatusStyle(status: BookingStatus): string {
  return STATUS_STYLES[status] ?? STATUS_STYLES.Cancelled
}

/** Бронь ещё живая: заявка или подтверждённый визит. */
export function isActiveBooking(status: BookingStatus): boolean {
  return status === 'Pending' || status === 'Confirmed'
}

/** Клиент может отменить заявку и подтверждённую бронь — ровно как домен. */
export function canCancelBooking(status: BookingStatus): boolean {
  return isActiveBooking(status)
}

/** Отзыв возможен только по завершённому визиту. */
export function canReviewBooking(status: BookingStatus): boolean {
  return status === 'Completed'
}

/**
 * Цену визита правят у предстоящего и у состоявшегося визита — как домен.
 *
 * Отменённая запись и неявка не оплачиваются, поэтому там сумма заморожена; это
 * правило одно на кабинет барбера и карточку администратора.
 */
export function canChangeBookingPrice(status: BookingStatus): boolean {
  return status !== 'Cancelled' && status !== 'NoShow'
}

/**
 * Перенос — только у активной брони, которая ещё не началась.
 *
 * Терминальные статусы двигать поздно, а начавшийся визит закрывают завершением
 * или неявкой. `now` передаётся снаружи, чтобы правило проверялось без часов.
 */
export function canRescheduleBooking(
  status: BookingStatus,
  startAt: string,
  now: number
): boolean {
  return isActiveBooking(status) && Date.parse(startAt) > now
}
