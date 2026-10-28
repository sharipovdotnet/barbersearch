import { useState } from 'react'
import { Loader2, TriangleAlert } from 'lucide-react'

import type { BookingView } from '@/components/account/booking-view'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { describeApiFailure } from '@/lib/errors'
import { formatDateTime } from '@/lib/format'

/**
 * Подтверждение отмены.
 *
 * Отмена необратима, поэтому это не просто «ОК»: в диалоге написано, что вернуть
 * запись нельзя, а кнопка подтверждения — `destructive`, чтобы её нельзя было
 * нажать по инерции.
 */
export function CancelBookingDialog({
  view,
  onClose,
  onConfirm,
  onCancelled,
}: {
  view: BookingView | null
  onClose: () => void
  onConfirm: (bookingId: string) => Promise<void>
  onCancelled: (view: BookingView) => void
}) {
  return (
    <Dialog
      open={view !== null}
      onOpenChange={(next) => {
        if (!next) {
          onClose()
        }
      }}
    >
      {/* Содержимое монтируется на каждое открытие: состояние формы не переносится
          с прошлой брони и не требует сброса через эффект. */}
      {view && (
        <DialogContent role="alertdialog">
          <CancelConfirmation
            view={view}
            onClose={onClose}
            onConfirm={onConfirm}
            onCancelled={onCancelled}
          />
        </DialogContent>
      )}
    </Dialog>
  )
}

function CancelConfirmation({
  view,
  onClose,
  onConfirm,
  onCancelled,
}: {
  view: BookingView
  onClose: () => void
  onConfirm: (bookingId: string) => Promise<void>
  onCancelled: (view: BookingView) => void
}) {
  const [isPending, setIsPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleConfirm() {
    if (isPending) {
      return
    }

    setIsPending(true)
    setError(null)

    try {
      await onConfirm(view.booking.id)
      onCancelled(view)
      onClose()
    } catch (cause) {
      setError(describeApiFailure(cause, 'Не удалось отменить запись.'))
      setIsPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Отменить запись?</DialogTitle>
        <DialogDescription>
          {formatDateTime(view.booking.startAt, view.timeZoneId)} ·{' '}
          {view.barberName}
        </DialogDescription>
      </DialogHeader>

      <div className="flex gap-2.5 rounded-lg bg-destructive/10 p-3">
        <TriangleAlert
          className="mt-0.5 size-4 shrink-0 text-destructive"
          aria-hidden
        />
        <div className="space-y-1">
          <p className="text-sm font-medium text-destructive">Отмена необратима</p>
          <p className="text-sm text-foreground/80">
            Вернуть запись нельзя — придётся записываться заново. Время может
            занять другой клиент.
          </p>
        </div>
      </div>

      {error && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}

      <DialogFooter className="flex-col sm:flex-row">
        <Button
          type="button"
          variant="outline"
          onClick={onClose}
          disabled={isPending}
        >
          Не отменять
        </Button>
        <Button
          type="button"
          variant="destructive"
          onClick={() => void handleConfirm()}
          disabled={isPending}
        >
          {isPending && (
            <Loader2 data-icon="inline-start" aria-hidden className="animate-spin" />
          )}
          {isPending ? 'Отменяем…' : 'Отменить запись'}
        </Button>
      </DialogFooter>
    </>
  )
}
