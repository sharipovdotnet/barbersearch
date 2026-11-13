import { CalendarClock, Check, Star, X } from 'lucide-react'

import { BookingStatusBadge } from '@/components/booking-status-badge'
import type { BookingView } from '@/components/account/booking-view'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { useI18n } from '@/i18n'
import {
  canCancelBooking,
  canRescheduleBooking,
  canReviewBooking,
} from '@/lib/booking-status'
import {
  formatDateTime,
  formatPrice,
  formatShortDay,
  formatTime,
  formatWeekday,
  getInitials,
} from '@/lib/format'
import { avatarImageSource } from '@/lib/media'

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
  now,
  onRequestReschedule,
  onRequestCancel,
  onRequestReview,
}: {
  view: BookingView
  isReviewed: boolean
  /** Текущий момент страницы: перенос доступен только до начала визита. */
  now: number
  onRequestReschedule: () => void
  onRequestCancel: () => void
  onRequestReview: () => void
}) {
  const { t } = useI18n()
  const { booking } = view
  const timeZone = view.timeZoneId
  const when = formatDateTime(booking.startAt, timeZone)
  const canCancel = canCancelBooking(booking.status)
  const canReschedule = canRescheduleBooking(
    booking.status,
    booking.startAt,
    now
  )
  // Отзыв мог уйти в этой сессии (`isReviewed`) или прийти с сервера (`hasReview`):
  // без второго флага звезда возвращалась после перезагрузки истории.
  const isAlreadyReviewed = isReviewed || booking.hasReview
  const canReview = canReviewBooking(booking.status) && !isAlreadyReviewed

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
        <div className="flex min-w-0 items-center justify-between gap-3">
          <div className="flex min-w-0 items-center gap-2">
            <Avatar size="sm" aria-hidden>
              {view.barberAvatarUrl && (
                <AvatarImage
                  {...avatarImageSource(view.barberAvatarUrl)}
                  alt=""
                />
              )}
              <AvatarFallback>{getInitials(view.barberName)}</AvatarFallback>
            </Avatar>
            <p className="truncate text-sm font-medium">{view.barberName}</p>
          </div>
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
          {canReschedule && (
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label={t('Перенести запись {when}', { when })}
              title={t('Перенести запись')}
              className="text-muted-foreground hover:text-foreground"
              onClick={onRequestReschedule}
            >
              <CalendarClock aria-hidden />
            </Button>
          )}

          {canCancel && (
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label={t('Отменить запись {when}', { when })}
              title={t('Отменить запись')}
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
              aria-label={t('Оставить отзыв о визите {when}', { when })}
              title={t('Оставить отзыв')}
              className="text-muted-foreground hover:text-foreground"
              onClick={onRequestReview}
            >
              <Star aria-hidden />
            </Button>
          )}

          {canReviewBooking(booking.status) && isAlreadyReviewed && (
            <span
              title={t('Отзыв отправлен')}
              className="inline-flex size-7 items-center justify-center text-muted-foreground"
            >
              <Check className="size-3.5" aria-hidden />
              <span className="sr-only">{t('Отзыв отправлен')}</span>
            </span>
          )}
        </div>
      </div>

      {booking.status === 'Cancelled' && (
        <p className="col-span-full break-words text-xs text-muted-foreground">
          {booking.cancellationReason ? (
            <>
              <span>{t('Причина отмены:')} </span>
              <span className="text-foreground">
                {booking.cancellationReason}
              </span>
            </>
          ) : (
            t('Причина отмены не указана')
          )}
        </p>
      )}
    </li>
  )
}
