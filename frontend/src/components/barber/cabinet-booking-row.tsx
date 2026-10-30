import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Loader2 } from 'lucide-react'

import { BookingStatusBadge } from '@/components/booking-status-badge'
import { Button } from '@/components/ui/button'
import type {
  BarberBooking,
  BarberBookingScope,
} from '@/lib/barber-bookings'
import {
  describeBarberBookingFailure,
  type BarberActionFailure,
} from '@/lib/errors'
import { formatDateTime, formatPrice } from '@/lib/format'

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
  onReload: () => void
  registerRow: (id: string, handle: RowHandle | null) => void
}) {
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
          <p className="text-sm">
            <span className="font-medium">{booking.customer.displayName}</span>
            {phone && (
              <>
                {' · '}
                <a
                  href={`tel:${phone.replace(/[^\d+]/g, '')}`}
                  className="break-words text-muted-foreground underline-offset-3 hover:text-foreground hover:underline focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
                >
                  {phone}
                </a>
              </>
            )}
          </p>
          <p className="break-words text-sm text-muted-foreground">
            {booking.service.name} · {booking.salon.name}
          </p>
          <p className="break-words text-xs text-muted-foreground">
            {booking.salon.city}, {booking.salon.address}
          </p>
        </div>

        <div className="flex shrink-0 items-center gap-2 sm:flex-col sm:items-end">
          <span className="text-sm tabular-nums">
            {formatPrice(booking.price)}
          </span>
          <BookingStatusBadge status={booking.status} />
        </div>
      </div>

      {booking.customerNote && (
        <p className="break-words text-sm">
          <span className="text-muted-foreground">Комментарий клиента: </span>
          {booking.customerNote}
        </p>
      )}

      {booking.barberNote && (
        <p className="break-words text-sm">
          <span className="text-muted-foreground">Ваша заметка: </span>
          {booking.barberNote}
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
                  'Не удалось подтвердить запись.'
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
              Подтвердить
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
                  'Не удалось завершить визит.'
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
              Завершить
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
              Неявка
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
              Отменить
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
            Заметка
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
              Обновить
            </Button>
          )}
        </div>
      )}
    </li>
  )
}
