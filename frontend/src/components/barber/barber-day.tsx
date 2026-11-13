import { Fragment, type RefObject } from 'react'
import { CalendarX, Clock, TriangleAlert } from 'lucide-react'

import {
  CabinetBookingRow,
  type RowHandle,
} from '@/components/barber/cabinet-booking-row'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import type { BarberDayController } from '@/hooks/use-barber-day'
import { t } from '@/i18n'
import type { DayGap } from '@/lib/barber-day'
import type { BarberBooking } from '@/lib/barber-bookings'
import {
  formatBookingsCount,
  formatCountdown,
  formatGapsCount,
  formatLongDay,
  formatTime,
} from '@/lib/format'

const SKELETON_KEYS = [
  'day-skeleton-1',
  'day-skeleton-2',
  'day-skeleton-3',
]

/**
 * Лента дня в кабинете барбера.
 *
 * Экран отвечает на один вопрос — «кто придёт сегодня и что у меня в ближайший
 * час». День читается сверху вниз: строки записей вперемешку с окнами между ними.
 * Окна считаются только между записями (рабочих часов из расписания здесь нет), а
 * пустое окно сразу открывает диалог записи с этим временем.
 *
 * Действия — те же пять, через тот же `CabinetBookingRow`: второй реализации
 * статусов не появляется.
 */
export function BarberDay({
  day,
  now,
  headingRef,
  onFillGap,
  onCreate,
  onConfirm,
  onComplete,
  onOpenCancel,
  onOpenNoShow,
  onOpenNote,
  onOpenReschedule,
  onReload,
  registerRow,
}: {
  day: BarberDayController
  now: number
  headingRef: RefObject<HTMLHeadingElement | null>
  /** Клик по окну: экран открывает диалог с этим временем. */
  onFillGap: (gap: DayGap) => void
  /** Пустое состояние: кнопка «Записать клиента» открывает тот же диалог. */
  onCreate: () => void
  onConfirm: (id: string) => Promise<void>
  onComplete: (id: string) => Promise<void>
  onOpenCancel: (booking: BarberBooking) => void
  onOpenNoShow: (booking: BarberBooking) => void
  onOpenNote: (booking: BarberBooking) => void
  onOpenReschedule: (booking: BarberBooking) => void
  onReload: () => void
  registerRow: (id: string, handle: RowHandle | null) => void
}) {
  const isLoading = day.status === 'loading'
  const isEmpty = day.status === 'ready' && day.items.length === 0
  const liveMessage = isLoading
    ? t('Загружаем записи…')
    : isEmpty
      ? t('На сегодня записей нет.')
      : ''

  const gapByStart = new Map(day.gaps.map((gap) => [gap.endAt, gap]))
  const nextLine = describeNext(day.nextBooking, now)

  return (
    <section aria-labelledby="barber-day-heading" className="space-y-3">
      <p aria-live="polite" className="sr-only">
        {liveMessage}
      </p>

      <div className="space-y-1">
        <h2
          id="barber-day-heading"
          ref={headingRef}
          tabIndex={-1}
          className="scroll-mt-20 rounded-md font-heading text-lg font-semibold tracking-tight focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
        >
          {t('Сегодня')}
        </h2>
        <p className="text-sm text-muted-foreground">{daySubtitle(day, now)}</p>
        {nextLine !== null && <p className="text-sm">{nextLine}</p>}
      </div>

      {day.partialError !== null && (
        <div
          role="alert"
          className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-destructive/30 px-3 py-2 text-sm"
        >
          <p className="flex items-start gap-2 text-muted-foreground">
            <TriangleAlert
              className="mt-0.5 size-4 shrink-0 text-destructive"
              aria-hidden
            />
            <span>{day.partialError}</span>
          </p>
          <Button
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            onClick={day.retryFailed}
          >
            {t('Повторить')}
          </Button>
        </div>
      )}

      {isLoading && <DaySkeleton />}

      {day.status === 'error' && (
        <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
          <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
            <TriangleAlert className="size-4 text-destructive" aria-hidden />
          </div>
          <div className="space-y-1">
            <p className="font-medium">{t('Не удалось загрузить записи.')}</p>
            <p
              role="alert"
              className="mx-auto max-w-sm text-sm text-muted-foreground"
            >
              {day.error ?? t('Не удалось загрузить записи.')}
            </p>
          </div>
          <Button
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            onClick={day.retryFailed}
          >
            {t('Повторить')}
          </Button>
        </div>
      )}

      {isEmpty && (
        <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
          <div className="flex size-10 items-center justify-center rounded-full bg-muted">
            <CalendarX className="size-4 text-muted-foreground" aria-hidden />
          </div>
          <div className="space-y-1">
            <p className="font-medium">{t('На сегодня записей нет.')}</p>
            <p className="mx-auto max-w-sm text-sm text-muted-foreground">
              {t('Запишите клиента, если он придёт без записи.')}
            </p>
          </div>
          <Button
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            onClick={onCreate}
          >
            {t('Записать клиента')}
          </Button>
        </div>
      )}

      {day.status === 'ready' && day.items.length > 0 && (
        <div className="space-y-2">
          <ol
            aria-label={t('Расписание на сегодня')}
            className="divide-y rounded-xl border bg-card"
          >
            {day.items.map((booking) => {
              const gap = gapByStart.get(booking.startAt)

              return (
                <Fragment key={booking.id}>
                  {gap !== undefined && (
                    <GapRow
                      gap={gap}
                      timeZoneId={booking.salon.timeZoneId}
                      onFill={onFillGap}
                    />
                  )}
                  <CabinetBookingRow
                    booking={booking}
                    scope="upcoming"
                    now={now}
                    onConfirm={onConfirm}
                    onComplete={onComplete}
                    onOpenCancel={onOpenCancel}
                    onOpenNoShow={onOpenNoShow}
                    onOpenNote={onOpenNote}
                    onOpenReschedule={onOpenReschedule}
                    onReload={onReload}
                    registerRow={registerRow}
                  />
                </Fragment>
              )
            })}
          </ol>

          {day.isTruncated && (
            <p className="text-sm text-muted-foreground">
              {t('Показаны первые 20 записей дня.')}
            </p>
          )}
        </div>
      )}
    </section>
  )
}

/** «сб, 8 ноября · 3 записи · 2 окна»; при нуле окон — только записи. */
function daySubtitle(day: BarberDayController, now: number): string {
  const parts = [formatLongDay(new Date(now).toISOString())]

  if (day.items.length > 0) {
    parts.push(formatBookingsCount(day.items.length))
  }

  if (day.gaps.length > 0) {
    parts.push(formatGapsCount(day.gaps.length))
  }

  return parts.join(' · ')
}

/**
 * Строка «следующего клиента».
 *
 * Обратный отсчёт не в `aria-live`: он тикает каждую минуту и не должен
 * проговариваться. Идущий визит важнее отсчёта — у него нет «через».
 */
function describeNext(
  booking: BarberBooking | null,
  now: number
): string | null {
  if (booking === null) {
    return null
  }

  if (Date.parse(booking.startAt) <= now) {
    return t('Идёт визит до {time}', {
      time: formatTime(booking.endAt, booking.salon.timeZoneId),
    })
  }

  const countdown = formatCountdown(booking.startAt, now)

  if (countdown !== null) {
    return t('Следующий клиент {countdown}', { countdown })
  }

  return t('Следующий клиент в {time}', {
    time: formatTime(booking.startAt, booking.salon.timeZoneId),
  })
}

/**
 * Окно между визитами: слева длительность, справа «Занять» 44 px.
 *
 * Время окна — в поясе салона следующей записи: именно туда придёт новая бронь.
 * Доступное имя кнопки уточняет время и начинается с видимого слова (WCAG 2.5.3).
 */
function GapRow({
  gap,
  timeZoneId,
  onFill,
}: {
  gap: DayGap
  timeZoneId: string
  onFill: (gap: DayGap) => void
}) {
  const from = formatTime(gap.startAt, timeZoneId)
  const to = formatTime(gap.endAt, timeZoneId)

  return (
    <li className="p-3 sm:p-4">
      <div className="flex flex-wrap items-center justify-between gap-2 rounded-lg bg-muted/50 px-3 py-2">
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <Clock className="size-4 shrink-0" aria-hidden />
          {t('свободно {minutes} мин', { minutes: gap.minutes })}
        </p>
        <Button
          variant="outline"
          size="sm"
          className="h-11 sm:h-8"
          aria-label={t('Занять время с {from} до {to}', { from, to })}
          onClick={() => onFill(gap)}
        >
          {t('Занять')}
        </Button>
      </div>
    </li>
  )
}

function DaySkeleton() {
  return (
    <ul aria-hidden="true" className="divide-y rounded-xl border bg-card">
      {SKELETON_KEYS.map((key) => (
        <li key={key} className="space-y-2 p-4">
          <Skeleton className="h-4 w-40" />
          <Skeleton className="h-4 w-56" />
          <Skeleton className="h-4 w-full max-w-sm" />
        </li>
      ))}
    </ul>
  )
}
