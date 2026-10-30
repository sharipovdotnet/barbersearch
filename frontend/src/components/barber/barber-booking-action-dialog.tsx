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
  onConfirm: (kind: BarberActionKind, bookingId: string) => Promise<void>
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
  onConfirm: (kind: BarberActionKind, bookingId: string) => Promise<void>
  onDone: (kind: BarberActionKind, booking: BarberBooking) => void
  onReload: () => void
}) {
  const { booking, kind } = target
  const isCancel = kind === 'cancel'
  const [isPending, setIsPending] = useState(false)
  const [failure, setFailure] = useState<BarberActionFailure | null>(null)

  async function handleConfirm() {
    if (isPending) {
      return
    }

    setIsPending(true)
    setFailure(null)

    try {
      await onConfirm(kind, booking.id)
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
