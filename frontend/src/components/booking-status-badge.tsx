import type { BookingStatus } from '@/lib/bookings'
import { bookingStatusLabel, bookingStatusStyle } from '@/lib/booking-status'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'

/**
 * Статус брони.
 *
 * Точка и текст дублируют цвет: статус читается и без цветового зрения,
 * и в чёрно-белой печати.
 */
export function BookingStatusBadge({
  status,
  className,
}: {
  status: BookingStatus
  className?: string
}) {
  return (
    <Badge
      variant="outline"
      className={cn('gap-1.5 font-medium', bookingStatusStyle(status), className)}
    >
      <span aria-hidden className="size-1.5 rounded-full bg-current" />
      {bookingStatusLabel(status)}
    </Badge>
  )
}
