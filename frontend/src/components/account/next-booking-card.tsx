import {
  CalendarClock,
  CalendarPlus,
  Clock,
  MapPin,
  StickyNote,
  X,
} from 'lucide-react'

import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardFooter,
  CardHeader,
} from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'
import { BookingStatusBadge } from '@/components/account/booking-status-badge'
import { StatusTimeline } from '@/components/account/status-timeline'
import type { BookingView } from '@/components/account/booking-view'
import { downloadBookingCalendar } from '@/lib/calendar'
import {
  formatCountdown,
  formatDuration,
  formatLongDay,
  formatPrice,
  formatShortDay,
  formatTime,
  getInitials,
  relativeDayLabel,
} from '@/lib/format'

/**
 * Герой кабинета: единственная запись, которую нужно увидеть сразу.
 *
 * Время крупным моноширинным набором, рядом — человеческая дата и обратный отсчёт.
 * Действия живут в подвале карточки, чтобы случайно не отменить визит, читая статус.
 */
export function NextBookingCard({
  view,
  now,
  onRequestCancel,
}: {
  view: BookingView
  now: number
  onRequestCancel: () => void
}) {
  const { booking } = view
  const timeZone = view.timeZoneId
  const relative = relativeDayLabel(booking.startAt, now, timeZone)
  const countdown = formatCountdown(booking.startAt, now)
  const address = [view.salonName, view.salonAddress]
    .filter(Boolean)
    .join(', ')

  // «Сегодня» без времени бесполезно, а «через 1 день» дублирует «Завтра»:
  // показываем либо обратный отсчёт, либо календарную дату.
  const dateLine = relative
    ? relative === 'Сегодня' && countdown
      ? `Сегодня · ${countdown}`
      : `${relative}, ${formatShortDay(booking.startAt, timeZone)}`
    : formatLongDay(booking.startAt, timeZone)

  function handleAddToCalendar() {
    downloadBookingCalendar({
      id: booking.id,
      title: `${view.serviceName} — ${view.barberName}`,
      description: [
        `Запись в BarberSearch.`,
        `Услуга: ${view.serviceName}.`,
        `Мастер: ${view.barberName}.`,
        `Салон: ${address}.`,
      ].join(' '),
      location: [view.salonCity, address].filter(Boolean).join(', '),
      startAt: booking.startAt,
      endAt: booking.endAt,
    })
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="inline-flex items-center gap-1.5 rounded-full bg-primary px-2.5 py-1 text-xs font-medium text-primary-foreground">
            <CalendarClock className="size-3.5" aria-hidden />
            Ближайшая запись
          </span>
          <BookingStatusBadge status={booking.status} />
        </div>
      </CardHeader>

      <CardContent className="space-y-5">
        <div className="grid gap-5 sm:grid-cols-[auto_minmax(0,1fr)_auto] sm:items-start sm:gap-8">
          <div>
            <p className="font-heading text-3xl font-semibold tracking-tight tabular-nums sm:text-4xl">
              {formatTime(booking.startAt, timeZone)}
            </p>
            <p className="mt-1 text-sm text-muted-foreground">{dateLine}</p>
          </div>

          <div className="flex min-w-0 items-start gap-3">
            <Avatar size="lg" aria-hidden>
              <AvatarFallback>{getInitials(view.barberName)}</AvatarFallback>
            </Avatar>
            <div className="min-w-0 space-y-1">
              <p className="truncate font-medium">{view.barberName}</p>
              <p className="flex items-center gap-1.5 text-sm text-muted-foreground">
                <Clock className="size-3.5 shrink-0" aria-hidden />
                <span className="truncate">
                  {view.serviceName} · {formatDuration(view.durationMinutes)}
                </span>
              </p>
              <p className="flex items-start gap-1.5 text-sm text-muted-foreground">
                <MapPin className="mt-0.5 size-3.5 shrink-0" aria-hidden />
                <span className="min-w-0">{address}</span>
              </p>
            </div>
          </div>

          <div className="flex items-baseline gap-2 sm:block sm:text-right">
            <p className="font-heading text-lg font-medium tabular-nums">
              {formatPrice(booking.price)}
            </p>
            <p className="text-xs text-muted-foreground">оплата в салоне</p>
          </div>
        </div>

        <Separator />

        <StatusTimeline status={booking.status} />

        {(booking.customerNote || booking.barberNote) && (
          <div className="space-y-2 rounded-lg bg-muted/50 p-3 text-sm">
            {booking.barberNote && (
              <p className="flex items-start gap-2">
                <StickyNote
                  className="mt-0.5 size-3.5 shrink-0 text-muted-foreground"
                  aria-hidden
                />
                <span>
                  <span className="text-muted-foreground">Барбер: </span>
                  {booking.barberNote}
                </span>
              </p>
            )}
            {booking.customerNote && (
              <p className="flex items-start gap-2">
                <StickyNote
                  className="mt-0.5 size-3.5 shrink-0 text-muted-foreground"
                  aria-hidden
                />
                <span>
                  <span className="text-muted-foreground">Вы: </span>
                  {booking.customerNote}
                </span>
              </p>
            )}
          </div>
        )}
      </CardContent>

      <CardFooter className="flex-wrap justify-between gap-2">
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" size="sm" onClick={onRequestCancel}>
            <X data-icon="inline-start" aria-hidden />
            Отменить
          </Button>
          <Button variant="outline" size="sm" onClick={handleAddToCalendar}>
            <CalendarPlus data-icon="inline-start" aria-hidden />
            В календарь
          </Button>
        </div>
        <p className="hidden text-xs text-muted-foreground sm:block">
          Отмена доступна до начала визита
        </p>
      </CardFooter>
    </Card>
  )
}
