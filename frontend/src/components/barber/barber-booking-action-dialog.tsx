import { useState } from 'react'
import { Loader2, TriangleAlert } from 'lucide-react'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import type { BarberBooking } from '@/lib/barber-bookings'
import {
  describeBarberBookingFailure,
  type BarberActionFailure,
} from '@/lib/errors'
import { formatDateTime } from '@/lib/format'

export type BarberActionKind = 'cancel' | 'noShow'

export type BarberActionTarget = {
  booking: BarberBooking
  kind: BarberActionKind
}

/** Предел сервера (`MAX_CANCELLATION_REASON_LENGTH`); клиент повторяет его для счётчика. */
const MAX_CANCELLATION_REASON_LENGTH = 500

/**
 * Подтверждения необратимых действий: отмена и неявка.
 *
 * Обе кнопки — `destructive`, а текст честно говорит, что вернуть запись нельзя.
 * Содержимое монтируется на каждое открытие, чтобы ошибка прошлой строки не
 * появлялась над следующей.
 */
export function BarberBookingActionDialog({
  target,
  onClose,
  onConfirm,
  onDone,
  onReload,
}: {
  target: BarberActionTarget | null
  onClose: () => void
  onConfirm: (
    kind: BarberActionKind,
    bookingId: string,
    reason?: string
  ) => Promise<void>
  onDone: (kind: BarberActionKind, booking: BarberBooking) => void
  onReload: () => void
}) {
  return (
    <Dialog
      open={target !== null}
      onOpenChange={(next) => {
        if (!next) {
          onClose()
        }
      }}
    >
      {target && (
        <DialogContent role="alertdialog">
          <ActionConfirmation
            target={target}
            onClose={onClose}
            onConfirm={onConfirm}
            onDone={onDone}
            onReload={onReload}
          />
        </DialogContent>
      )}
    </Dialog>
  )
}

function ActionConfirmation({
  target,
  onClose,
  onConfirm,
  onDone,
  onReload,
}: {
  target: BarberActionTarget
  onClose: () => void
  onConfirm: (
    kind: BarberActionKind,
    bookingId: string,
    reason?: string
  ) => Promise<void>
  onDone: (kind: BarberActionKind, booking: BarberBooking) => void
  onReload: () => void
}) {
  const { booking, kind } = target
  const isCancel = kind === 'cancel'
  const [isPending, setIsPending] = useState(false)
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [failure, setFailure] = useState<BarberActionFailure | null>(null)

  async function handleConfirm() {
    if (isPending) {
      return
    }

    const trimmedReason = reason.trim()

    if (isCancel && trimmedReason === '') {
      setReasonError('Укажите причину отмены.')
      return
    }

    setIsPending(true)
    setReasonError(null)
    setFailure(null)

    try {
      await onConfirm(kind, booking.id, isCancel ? trimmedReason : undefined)
      onDone(kind, booking)
      onClose()
    } catch (cause) {
      setFailure(
        describeBarberBookingFailure(
          cause,
          isCancel
            ? 'Не удалось отменить запись.'
            : 'Не удалось отметить неявку.'
        )
      )
      setIsPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>
          {isCancel ? 'Отменить запись?' : 'Отметить неявку?'}
        </DialogTitle>
        <DialogDescription>
          {formatDateTime(booking.startAt, booking.salon.timeZoneId)} ·{' '}
          {booking.customer.displayName}
        </DialogDescription>
      </DialogHeader>

      <div className="flex gap-2.5 rounded-lg bg-destructive/10 p-3">
        <TriangleAlert
          className="mt-0.5 size-4 shrink-0 text-destructive"
          aria-hidden
        />
        <div className="space-y-1">
          <p className="text-sm font-medium text-destructive">
            {isCancel ? 'Отмена необратима' : 'Неявка необратима'}
          </p>
          <p className="text-sm text-foreground/80">
            {isCancel
              ? 'Слот освободится — время сможет занять другой клиент. Вернуть запись нельзя, придётся записывать заново.'
              : 'Запись закроется со статусом «Неявка». Вернуть её или завершить визит после этого нельзя.'}
          </p>
        </div>
      </div>

      {isCancel && (
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="cancellation-reason">Причина отмены</Label>
          <Textarea
            id="cancellation-reason"
            name="cancellation-reason"
            value={reason}
            maxLength={MAX_CANCELLATION_REASON_LENGTH}
            disabled={isPending}
            required
            aria-invalid={reasonError !== null}
            aria-describedby={
              reasonError !== null ? 'cancellation-reason-error' : undefined
            }
            placeholder="Например: барбер заболел"
            onChange={(event) => {
              setReason(event.target.value)
              setReasonError(null)
              setFailure(null)
            }}
          />
          <p className="text-xs text-muted-foreground">
            Клиент увидит причину в истории посещений.
          </p>
          <p className="text-right text-xs text-muted-foreground tabular-nums">
            {reason.length} / {MAX_CANCELLATION_REASON_LENGTH}
          </p>
          {reasonError && (
            <p
              id="cancellation-reason-error"
              role="alert"
              className="text-sm text-destructive"
            >
              {reasonError}
            </p>
          )}
        </div>
      )}

      {failure && (
        <div className="flex flex-wrap items-center gap-2">
          <p role="alert" className="text-sm text-destructive">
            {failure.message}
          </p>
          {failure.canReload && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              onClick={() => {
                onReload()
                onClose()
              }}
            >
              Обновить
            </Button>
          )}
        </div>
      )}

      <DialogFooter className="flex-col sm:flex-row">
        <Button
          type="button"
          variant="outline"
          className="h-11 sm:h-8"
          onClick={onClose}
          disabled={isPending}
        >
          {isCancel ? 'Не отменять' : 'Не отмечать'}
        </Button>
        <Button
          type="button"
          variant="destructive"
          className="h-11 sm:h-8"
          onClick={() => void handleConfirm()}
          disabled={isPending}
          aria-busy={isPending}
        >
          {isPending && (
            <Loader2
              data-icon="inline-start"
              aria-hidden
              className="animate-spin"
            />
          )}
          {isPending
            ? 'Сохраняем…'
            : isCancel
              ? 'Отменить запись'
              : 'Отметить неявку'}
        </Button>
      </DialogFooter>
    </>
  )
}
