import { Check, Star, X } from 'lucide-react'

import { BookingStatusBadge } from '@/components/booking-status-badge'
import type { BookingView } from '@/components/account/booking-view'
import { Button } from '@/components/ui/button'
import { canCancelBooking, canReviewBooking } from '@/lib/booking-status'
import {
  formatDateTime,
  formatPrice,
  formatShortDay,
  formatTime,
  formatWeekday,
} from '@/lib/format'

/**
 * Строка истории или предстоящего визита.
 *
 * Действия — только иконки с подписью для скринридера: в списке из двадцати строк
 * текстовые кнопки превратились бы в шум. На узком экране строка складывается
 * в две колонки, на широком — в четыре.
 */
export function BookingRow({
  view,
  isReviewed,
  onRequestCancel,
  onRequestReview,
}: {
  view: BookingView
  isReviewed: boolean
  onRequestCancel: () => void
  onRequestReview: () => void
}) {
  const { booking } = view
  const timeZone = view.timeZoneId
  const when = formatDateTime(booking.startAt, timeZone)
  const canCancel = canCancelBooking(booking.status)
  const canReview = canReviewBooking(booking.status) && !isReviewed

  return (
    <li className="grid grid-cols-[4.25rem_minmax(0,1fr)] items-start gap-x-3 gap-y-1 px-4 py-3 transition-colors hover:bg-muted/40 sm:grid-cols-[4.5rem_minmax(0,1fr)_5.5rem_auto] sm:items-center sm:gap-x-4">
      <div className="col-start-1 row-span-2 row-start-1 sm:row-span-1">
        <p className="text-sm font-medium tabular-nums">
          {formatShortDay(booking.startAt, timeZone)}
        </p>
        <p className="text-xs text-muted-foreground tabular-nums">
          {formatWeekday(booking.startAt, timeZone)},{' '}
          {formatTime(booking.startAt, timeZone)}
        </p>
      </div>

      <div className="col-start-2 row-start-1 min-w-0">
        <div className="flex items-baseline justify-between gap-3">
          <p className="truncate text-sm font-medium">{view.barberName}</p>
          <span className="shrink-0 text-sm tabular-nums sm:hidden">
            {formatPrice(booking.price)}
          </span>
        </div>
        <p className="truncate text-sm text-muted-foreground">
          {view.serviceName} · {view.salonName}
        </p>
      </div>

      <div className="hidden text-right text-sm tabular-nums sm:col-start-3 sm:row-start-1 sm:block">
        {formatPrice(booking.price)}
      </div>

      <div className="col-start-2 row-start-2 flex items-center justify-between gap-2 sm:col-start-4 sm:row-start-1 sm:justify-end">
        <BookingStatusBadge status={booking.status} />

        <div className="flex items-center gap-0.5">
          {canCancel && (
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label={`Отменить запись ${when}`}
              title="Отменить запись"
              className="text-muted-foreground hover:text-destructive"
              onClick={onRequestCancel}
            >
              <X aria-hidden />
            </Button>
          )}

          {canReview && (
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label={`Оставить отзыв о визите ${when}`}
              title="Оставить отзыв"
              className="text-muted-foreground hover:text-foreground"
              onClick={onRequestReview}
            >
              <Star aria-hidden />
            </Button>
          )}

          {canReviewBooking(booking.status) && isReviewed && (
            <span
              title="Отзыв отправлен"
              className="inline-flex size-7 items-center justify-center text-muted-foreground"
            >
              <Check className="size-3.5" aria-hidden />
              <span className="sr-only">Отзыв отправлен</span>
            </span>
          )}
        </div>
      </div>
    </li>
  )
}
