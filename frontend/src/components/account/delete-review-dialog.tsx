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
import { ApiError } from '@/lib/api'
import { describeApiFailure } from '@/lib/errors'
import { formatDateTime } from '@/lib/format'
import type { MyReview } from '@/lib/my-reviews'

function describeDeleteError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return 'Удалить отзыв может только его автор.'
    }

    if (error.status === 404) {
      return 'Этот отзыв уже удалён.'
    }
  }

  return describeApiFailure(error, 'Не удалось удалить отзыв.')
}

/**
 * Подтверждение удаления отзыва.
 *
 * Удаление необратимо, поэтому это `alertdialog` с `destructive`-кнопкой. В тексте
 * честно сказано, что по этой записи можно будет оставить новый отзыв: ограничение
 * «один отзыв на бронь» снимается вместе со строкой.
 */
export function DeleteReviewDialog({
  review,
  onClose,
  onConfirm,
}: {
  review: MyReview | null
  onClose: () => void
  onConfirm: (review: MyReview) => Promise<void>
}) {
  return (
    <Dialog
      open={review !== null}
      onOpenChange={(next) => {
        if (!next) {
          onClose()
        }
      }}
    >
      {review && (
        <DialogContent role="alertdialog">
          <DeleteConfirmation
            review={review}
            onClose={onClose}
            onConfirm={onConfirm}
          />
        </DialogContent>
      )}
    </Dialog>
  )
}

function DeleteConfirmation({
  review,
  onClose,
  onConfirm,
}: {
  review: MyReview
  onClose: () => void
  onConfirm: (review: MyReview) => Promise<void>
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
      await onConfirm(review)
      onClose()
    } catch (cause) {
      setError(describeDeleteError(cause))
      setIsPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Удалить отзыв?</DialogTitle>
        <DialogDescription>
          {formatDateTime(review.visit.startAt, review.visit.timeZoneId)} ·{' '}
          {review.barber.displayName}
        </DialogDescription>
      </DialogHeader>

      <div className="flex gap-2.5 rounded-lg bg-destructive/10 p-3">
        <TriangleAlert
          className="mt-0.5 size-4 shrink-0 text-destructive"
          aria-hidden
        />
        <div className="space-y-1">
          <p className="text-sm font-medium text-destructive">
            Удаление необратимо
          </p>
          <p className="text-sm text-foreground/80">
            Отзыв исчезнет навсегда, восстановить его нельзя. По этой записи
            можно будет оставить новый отзыв.
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
          Не удалять
        </Button>
        <Button
          type="button"
          variant="destructive"
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
          {isPending ? 'Удаляем…' : 'Удалить отзыв'}
        </Button>
      </DialogFooter>
    </>
  )
}
