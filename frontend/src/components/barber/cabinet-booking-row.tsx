import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { CalendarClock, Loader2, Pencil } from 'lucide-react'

import { BookingStatusBadge } from '@/components/booking-status-badge'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { useI18n } from '@/i18n'
import type {
  BarberBooking,
  BarberBookingScope,
} from '@/lib/barber-bookings'
import {
  canChangeBookingPrice,
  canRescheduleBooking,
} from '@/lib/booking-status'
import {
  describeBarberBookingFailure,
  type BarberActionFailure,
} from '@/lib/errors'
import { formatDateTime, formatPrice, getInitials } from '@/lib/format'
import { avatarImageSource } from '@/lib/media'

/** Что именно должно получить фокус после действия: первая кнопка, «Отменить» или «Заметка». */
export type RowHandle = {
  focusPrimary: () => void
  focusAfterConfirm: () => void
  focusNote: () => void
}

/**
 * Строка записи клиента.
 *
 * Каждое действие показывает своё состояние ожидания и свою ошибку: `409` на одной
 * строке не должен выглядеть как поломка списка. Телефон — единственное место, где
 * PII попадает в DOM, и только как `tel:`-ссылка.
 */
export function CabinetBookingRow({
  booking,
  scope,
  now,
  onConfirm,
  onComplete,
  onOpenCancel,
  onOpenNoShow,
  onOpenNote,
  onOpenReschedule,
  onOpenPrice,
  onReload,
  registerRow,
}: {
  booking: BarberBooking
  scope: BarberBookingScope
  now: number
  onConfirm: (id: string) => Promise<void>
  onComplete: (id: string) => Promise<void>
  onOpenCancel: (booking: BarberBooking) => void
  onOpenNoShow: (booking: BarberBooking) => void
  onOpenNote: (booking: BarberBooking) => void
  onOpenReschedule: (booking: BarberBooking) => void
  onOpenPrice: (booking: BarberBooking) => void
  onReload: () => void
  registerRow: (id: string, handle: RowHandle | null) => void
}) {
  const { t } = useI18n()
  const [pendingAction, setPendingAction] = useState<
    'confirm' | 'complete' | null
  >(null)
  const [failure, setFailure] = useState<BarberActionFailure | null>(null)

  const itemRef = useRef<HTMLLIElement>(null)
  const firstActionRef = useRef<HTMLButtonElement>(null)
  const cancelRef = useRef<HTMLButtonElement>(null)
  const noteRef = useRef<HTMLButtonElement>(null)

  const isPendingStatus = booking.status === 'Pending'
  const isConfirmed = booking.status === 'Confirmed'
  // «Завершить» и «Неявка» — только после начала визита; минута тикает снаружи.
  const hasStarted = Date.parse(booking.startAt) <= now
  const canComplete = isConfirmed && hasStarted
  // Подтверждённую запись отменяют только в «Ближайших»: в истории визит уже прошёл.
  const canCancel = isPendingStatus || (isConfirmed && scope === 'upcoming')
  // Перенос — как у клиента: активная и ещё не начавшаяся запись.
  const canReschedule = canRescheduleBooking(
    booking.status,
    booking.startAt,
    now
  )
  // Цену правят у предстоящего и у завершённого визита; кнопка живёт рядом с суммой,
  // а не в блоке действий: тот отрисован только для активных статусов (D11).
  const canChangePrice = canChangeBookingPrice(booking.status)

  const focusPrimary = useCallback(() => {
    ;(
      firstActionRef.current ??
      cancelRef.current ??
      noteRef.current ??
      itemRef.current
    )?.focus()
  }, [])
  const focusAfterConfirm = useCallback(() => {
    // Будущую запись логично отменять, у начавшейся — писать заметку.
    const target = (!hasStarted ? cancelRef.current : null) ?? noteRef.current

    ;(target ?? itemRef.current)?.focus()
  }, [hasStarted])
  const focusNote = useCallback(() => {
    ;(noteRef.current ?? itemRef.current)?.focus()
  }, [])

  const handle = useMemo<RowHandle>(
    () => ({ focusPrimary, focusAfterConfirm, focusNote }),
    [focusPrimary, focusAfterConfirm, focusNote]
  )

  useEffect(() => {
    registerRow(booking.id, handle)

    return () => registerRow(booking.id, null)
  }, [booking.id, handle, registerRow])

  async function runAction(
    kind: 'confirm' | 'complete',
    action: (id: string) => Promise<void>,
    fallback: string
  ) {
    if (pendingAction !== null) {
      return
    }

    setPendingAction(kind)
    setFailure(null)

    try {
      await action(booking.id)
    } catch (cause) {
      setFailure(describeBarberBookingFailure(cause, fallback))
    } finally {
      setPendingAction(null)
    }
  }

  const timeZone = booking.salon.timeZoneId
  const when = formatDateTime(booking.startAt, timeZone)
  const phone = booking.customer.phoneNumber

  return (
    <li
      ref={itemRef}
      tabIndex={-1}
      className="space-y-3 p-4 outline-none transition-colors hover:bg-muted/30 focus-visible:ring-3 focus-visible:ring-ring/50"
    >
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0 space-y-1">
          <p className="text-sm font-medium tabular-nums">{when}</p>
          {/* Имя и телефон делят одну flex-строку с аватаром: на 320 px телефон
              переносится, а не растягивает список по горизонтали. */}
          <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-sm">
            <Avatar size="sm" aria-hidden>
              {booking.customer.avatarUrl && (
                <AvatarImage
                  {...avatarImageSource(booking.customer.avatarUrl)}
                  alt=""
                />
              )}
              <AvatarFallback>
                {getInitials(booking.customer.displayName)}
              </AvatarFallback>
            </Avatar>
            <span className="min-w-0 font-medium break-words">
              {booking.customer.displayName}
            </span>
            {phone && (
              <>
                <span aria-hidden className="text-muted-foreground">
                  ·
                </span>
                <a
                  href={`tel:${phone.replace(/[^\d+]/g, '')}`}
                  className="min-w-0 break-words text-muted-foreground underline-offset-3 hover:text-foreground hover:underline focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
                >
                  {phone}
                </a>
              </>
            )}
          </div>
          <p className="break-words text-sm text-muted-foreground">
            {booking.service.name} · {booking.salon.name}
          </p>
          <p className="break-words text-xs text-muted-foreground">
            {booking.salon.city}, {booking.salon.address}
          </p>
        </div>

        <div className="flex shrink-0 items-center gap-2 sm:flex-col sm:items-end">
          <div className="flex items-center gap-1">
            <span className="text-sm tabular-nums">
              {formatPrice(booking.price)}
            </span>
            {canChangePrice && (
              <Button
                size="icon"
                variant="ghost"
                className="size-11 text-muted-foreground hover:text-foreground sm:size-8"
                disabled={pendingAction !== null}
                aria-label={t('Изменить цену визита {when}', { when })}
                title={t('Изменить цену визита')}
                onClick={() => onOpenPrice(booking)}
              >
                <Pencil aria-hidden />
              </Button>
            )}
          </div>
          <BookingStatusBadge status={booking.status} />
        </div>
      </div>

      {booking.customerNote && (
        <p className="break-words text-sm">
          <span className="text-muted-foreground">
            {t('Комментарий клиента:')}{' '}
          </span>
          {booking.customerNote}
        </p>
      )}

      {booking.barberNote && (
        <p className="break-words text-sm">
          <span className="text-muted-foreground">
            {t('Ваша заметка:')}{' '}
          </span>
          {booking.barberNote}
        </p>
      )}

      {booking.status === 'Cancelled' && (
        <p className="break-words text-sm">
          {booking.cancellationReason ? (
            <>
              <span className="text-muted-foreground">
                {t('Причина отмены:')}{' '}
              </span>
              {booking.cancellationReason}
            </>
          ) : (
            <span className="text-muted-foreground">
              {t('Причина отмены не указана')}
            </span>
          )}
        </p>
      )}

      {(isPendingStatus || isConfirmed) && (
        <div className="flex flex-wrap items-center gap-2">
          {isPendingStatus && (
            <Button
              ref={firstActionRef}
              size="sm"
              className="h-11 sm:h-8"
              disabled={pendingAction !== null}
              aria-busy={pendingAction === 'confirm'}
              onClick={() =>
                void runAction(
                  'confirm',
                  onConfirm,
                  t('Не удалось подтвердить запись.')
                )
              }
            >
              {pendingAction === 'confirm' && (
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
              )}
              {t('Подтвердить')}
            </Button>
          )}

          {canComplete && (
            <Button
              ref={firstActionRef}
              size="sm"
              variant="outline"
              className="h-11 sm:h-8"
              disabled={pendingAction !== null}
              aria-busy={pendingAction === 'complete'}
              onClick={() =>
                void runAction(
                  'complete',
                  onComplete,
                  t('Не удалось завершить визит.')
                )
              }
            >
              {pendingAction === 'complete' && (
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
              )}
              {t('Завершить')}
            </Button>
          )}

          {canComplete && (
            <Button
              size="sm"
              variant="ghost"
              className="h-11 text-destructive hover:text-destructive sm:h-8"
              disabled={pendingAction !== null}
              onClick={() => onOpenNoShow(booking)}
            >
              {t('Неявка')}
            </Button>
          )}

          {canReschedule && (
            <Button
              size="icon"
              variant="ghost"
              className="size-11 text-muted-foreground hover:text-foreground sm:size-8"
              disabled={pendingAction !== null}
              aria-label={t('Перенести запись {when}', { when })}
              title={t('Перенести запись')}
              onClick={() => onOpenReschedule(booking)}
            >
              <CalendarClock aria-hidden />
            </Button>
          )}

          {canCancel && (
            <Button
              ref={cancelRef}
              size="sm"
              variant="outline"
              className="h-11 sm:h-8"
              disabled={pendingAction !== null}
              onClick={() => onOpenCancel(booking)}
            >
              {t('Отменить')}
            </Button>
          )}

          <Button
            ref={noteRef}
            size="sm"
            variant="ghost"
            className="h-11 sm:h-8"
            disabled={pendingAction !== null}
            onClick={() => onOpenNote(booking)}
          >
            {t('Заметка')}
          </Button>
        </div>
      )}

      {failure && (
        <div
          role="alert"
          className="flex flex-wrap items-center gap-2 rounded-lg bg-destructive/10 px-3 py-2 text-sm text-destructive"
        >
          <p>{failure.message}</p>
          {failure.canReload && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              onClick={onReload}
            >
              {t('Обновить')}
            </Button>
          )}
        </div>
      )}
    </li>
  )
}
