import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import { Loader2, TriangleAlert } from 'lucide-react'

import { BookingStatusBadge } from '@/components/booking-status-badge'
import { LoginDialog } from '@/components/login-dialog'
import { Button, buttonVariants } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { useCreateBooking } from '@/hooks/use-create-booking'
import { useSession } from '@/hooks/use-session'
import type { BarberProfile } from '@/lib/catalog'
import type { BarberSlot } from '@/lib/barber-slots'
import { formatPrice, formatSalonDate, formatTime } from '@/lib/format'
import { isPlainClick, navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/** UI-лимит комментария: в БД `text`, серверного ограничения нет. */
const MAX_NOTE_LENGTH = 500

/** Снимок ответа `/slots` на момент выбора — перезапрос слотов его не меняет. */
export type BookingSlotSnapshot = {
  date: string
  timeZoneId: string
  salonId: string
  barberServiceId: string
}

type BarberBookingProps = {
  profile: BarberProfile
  slots: BookingSlotSnapshot
  slot: BarberSlot
  serviceName: string
  price: number | null
  /** Слот занят: перезапросить свободное время, панель не закрывать. */
  onBooked: () => void
  onConflict: () => void
  /** «Выбрать другое время» / «Записаться ещё раз»: сбросить выбор. */
  onClear: () => void
}

/**
 * Панель записи под выбранным слотом.
 *
 * Живёт внутри «Свободного времени» и монтируется на слот (`key={startAt}`), поэтому
 * ключ идемпотентности создаётся один раз на попытку и переживает повтор после ошибки.
 * Время показывается в поясе салона из снимка `/slots`, без пересчёта.
 */
export function BarberBooking({
  profile,
  slots,
  slot,
  serviceName,
  price,
  onBooked,
  onConflict,
  onClear,
}: BarberBookingProps) {
  const { status: sessionStatus } = useSession()
  const booking = useCreateBooking()
  const [note, setNote] = useState('')
  const [idempotencyKey, setIdempotencyKey] = useState(() =>
    crypto.randomUUID()
  )
  const headingId = useId()
  const noteId = useId()
  const noteHintId = useId()

  const resultRef = useRef<HTMLDivElement>(null)
  const errorRef = useRef<HTMLDivElement>(null)
  const bookedIdRef = useRef<string | null>(null)
  const handledFailureRef = useRef<string | null>(null)

  const venue =
    profile.salons.find((salon) => salon.salonId === slots.salonId)?.name ??
    'Салон'
  const isPending = booking.status === 'pending'
  const isSuccess = booking.status === 'success'
  const isError = booking.status === 'error'
  const isRetryable = isError && booking.failureKind === 'retry'
  const isKeyConflict =
    isError && booking.failureCode === 'booking.create.idempotencyKeyConflict'
  const isUnavailable = isError && booking.failureKind === 'unavailable'
  const needsSignIn =
    sessionStatus === 'authenticated' && booking.failureKind === 'signIn'

  useEffect(() => {
    if (!booking.createdId || bookedIdRef.current === booking.createdId) {
      return
    }

    bookedIdRef.current = booking.createdId
    onBooked()
    resultRef.current?.focus()
  }, [booking.createdId, onBooked])

  // Новая попытка начинается с чистого маркера: та же ошибка снова заберёт фокус.
  useEffect(() => {
    if (booking.status === 'idle' || booking.status === 'pending') {
      handledFailureRef.current = null
    }
  }, [booking.status])

  useEffect(() => {
    if (booking.failureKind === null) {
      return
    }

    const marker = `${booking.failureKind}|${booking.error ?? ''}`
    if (handledFailureRef.current === marker) {
      return
    }

    handledFailureRef.current = marker

    if (booking.failureKind === 'refresh') {
      onConflict()
    }

    errorRef.current?.focus()
  }, [booking.failureKind, booking.error, onConflict])

  function send(key = idempotencyKey) {
    booking.submit({
      barberProfileId: profile.id,
      barberServiceId: slots.barberServiceId,
      salonId: slots.salonId,
      startAt: slot.startAt,
      endAt: slot.endAt,
      idempotencyKey: key,
      customerNote: note.trim() === '' ? null : note.trim(),
    })
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (isPending) {
      return
    }

    send()
  }

  // Конфликт ключа не лечится повтором той же попытки: нужен новый ключ.
  function handleNewKeyRetry() {
    const nextKey = crypto.randomUUID()
    handledFailureRef.current = null
    setIdempotencyKey(nextKey)
    send(nextKey)
  }

  function handleSignedIn() {
    handledFailureRef.current = null
    booking.reset()
  }

  return (
    <section
      aria-labelledby={headingId}
      className="space-y-4 rounded-xl border border-border bg-muted/20 p-4"
    >
      <h3 id={headingId} className="text-sm font-medium">
        Запись на приём
      </h3>

      <BookingSummary
        slots={slots}
        slot={slot}
        serviceName={serviceName}
        venue={venue}
        price={price}
      />

      {sessionStatus === 'loading' && (
        <div className="space-y-2" aria-busy="true">
          <Skeleton className="h-4 w-40" />
          <Skeleton className="h-9 w-full rounded-lg sm:w-44" />
          <p role="status" className="sr-only">
            Проверяем вход…
          </p>
        </div>
      )}

      {sessionStatus !== 'loading' &&
        (sessionStatus === 'anonymous' || needsSignIn) && (
          <div className="space-y-3 border-t border-border pt-4">
            <p
              ref={needsSignIn ? errorRef : undefined}
              role={needsSignIn ? 'alert' : undefined}
              tabIndex={needsSignIn ? -1 : undefined}
              className="text-sm text-muted-foreground outline-none"
            >
              {needsSignIn
                ? (booking.error ?? 'Сессия истекла — войдите снова.')
                : 'Войдите, чтобы подтвердить запись'}
            </p>
            <LoginDialog
              variant="default"
              className="h-11 w-full sm:h-9 sm:w-auto"
              redirectTo={null}
              onSignedIn={handleSignedIn}
            />
          </div>
        )}

      {(isSuccess || (sessionStatus === 'authenticated' && !needsSignIn)) && (
        <form
          noValidate
          aria-busy={isPending}
          className="space-y-4 border-t border-border pt-4"
          onSubmit={handleSubmit}
        >
          {isError && (
            <div
              ref={errorRef}
              role="alert"
              tabIndex={-1}
              className="flex items-start gap-2 rounded-lg border border-destructive/30 p-3 text-sm outline-none"
            >
              <TriangleAlert
                className="mt-0.5 size-4 shrink-0 text-destructive"
                aria-hidden
              />
              <span>{booking.error ?? 'Не удалось записаться.'}</span>
            </div>
          )}

          {isSuccess ? (
            <div
              ref={resultRef}
              role="status"
              tabIndex={-1}
              className="space-y-3 outline-none"
            >
              <div className="flex flex-wrap items-center gap-2">
                <p className="text-sm font-medium">Заявка отправлена</p>
                <BookingStatusBadge status="Pending" />
              </div>
              <p className="text-sm text-muted-foreground">
                Барбер подтвердит запись — она появится в «Моих записях».
              </p>
              <div className="flex flex-col gap-2 sm:flex-row">
                <a
                  href="/account"
                  className={cn(
                    buttonVariants({ variant: 'default', size: 'default' }),
                    'h-11 w-full sm:h-9 sm:w-auto'
                  )}
                  onClick={(event) => {
                    if (!isPlainClick(event)) {
                      return
                    }

                    event.preventDefault()
                    navigate('/account')
                  }}
                >
                  Мои записи
                </a>
                <Button
                  type="button"
                  variant="outline"
                  className="h-11 w-full sm:h-9 sm:w-auto"
                  onClick={onClear}
                >
                  Записаться ещё раз
                </Button>
              </div>
            </div>
          ) : (
            <>
              <div className="space-y-1.5">
                <Label htmlFor={noteId}>Комментарий (необязательно)</Label>
                <Textarea
                  id={noteId}
                  name="customerNote"
                  value={note}
                  maxLength={MAX_NOTE_LENGTH}
                  aria-describedby={noteHintId}
                  onChange={(event) => setNote(event.target.value)}
                />
                <p
                  id={noteHintId}
                  className="text-xs text-muted-foreground tabular-nums"
                >
                  Осталось {MAX_NOTE_LENGTH - note.length}
                </p>
              </div>

              <div className="flex flex-col gap-2 sm:flex-row">
                {(!isError || isRetryable) && (
                  <Button
                    type="submit"
                    aria-busy={isPending}
                    className="h-11 w-full sm:h-9 sm:w-auto"
                  >
                    {isPending && (
                      <Loader2
                        data-icon="inline-start"
                        className="animate-spin"
                        aria-hidden
                      />
                    )}
                    {isPending
                      ? 'Отправляем…'
                      : isRetryable
                        ? 'Повторить'
                        : 'Записаться'}
                  </Button>
                )}

                {isKeyConflict && (
                  <Button
                    type="button"
                    className="h-11 w-full sm:h-9 sm:w-auto"
                    onClick={handleNewKeyRetry}
                  >
                    Попробовать снова
                  </Button>
                )}

                {isUnavailable ? (
                  <Button
                    type="button"
                    variant="outline"
                    className="h-11 w-full sm:h-9 sm:w-auto"
                    onClick={onClear}
                  >
                    Обновить свободное время
                  </Button>
                ) : (
                  <Button
                    type="button"
                    variant="outline"
                    className="h-11 w-full sm:h-9 sm:w-auto"
                    onClick={onClear}
                  >
                    Выбрать другое время
                  </Button>
                )}
              </div>
            </>
          )}
        </form>
      )}
    </section>
  )
}

/** День, время, услуга, салон и цена — один и тот же снимок в форме и подтверждении. */
function BookingSummary({
  slots,
  slot,
  serviceName,
  venue,
  price,
}: {
  slots: BookingSlotSnapshot
  slot: BarberSlot
  serviceName: string
  venue: string
  price: number | null
}) {
  return (
    <div className="space-y-1">
      <p className="text-sm font-medium">
        {formatSalonDate(slots.date)},{' '}
        <span className="tabular-nums">
          {formatTime(slot.startAt, slots.timeZoneId)}–
          {formatTime(slot.endAt, slots.timeZoneId)}
        </span>
      </p>
      <p className="text-sm text-muted-foreground">
        {serviceName} · {venue}
      </p>
      <p className="text-sm tabular-nums">
        {price === null ? 'Цена — уточняйте в салоне.' : formatPrice(price)}
      </p>
    </div>
  )
}
