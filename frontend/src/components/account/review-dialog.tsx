import { useState, type FormEvent } from 'react'
import { Loader2, Star } from 'lucide-react'

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
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { ApiError } from '@/lib/api'
import { createReview } from '@/lib/bookings'
import { describeApiFailure } from '@/lib/errors'
import { formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'

const MAX_TEXT_LENGTH = 2000
const RATINGS = [1, 2, 3, 4, 5] as const

/** Отзыв уходит на модерацию: рейтинг барбера меняется только после публикации. */
function describeReviewError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 409) {
      return 'По этой записи уже есть отзыв — второй оставить нельзя.'
    }

    if (error.status === 403) {
      return 'Оставить отзыв может только клиент этой записи.'
    }

    if (error.hasCode('review.create.bookingNotCompleted')) {
      return 'Отзыв можно оставить только после завершённого визита.'
    }

    if (error.hasCode('review.rating')) {
      return 'Оценка должна быть от 1 до 5.'
    }

    if (error.hasCode('review.text.tooLong')) {
      return `Слишком длинный текст: максимум ${MAX_TEXT_LENGTH} символов.`
    }
  }

  return describeApiFailure(error, 'Не удалось отправить отзыв.')
}

export function ReviewDialog({
  view,
  onClose,
  onSubmitted,
}: {
  view: BookingView | null
  onClose: () => void
  onSubmitted: (view: BookingView) => void
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
      {view && (
        <DialogContent className="sm:max-w-md">
          <ReviewForm view={view} onClose={onClose} onSubmitted={onSubmitted} />
        </DialogContent>
      )}
    </Dialog>
  )
}

function ReviewForm({
  view,
  onClose,
  onSubmitted,
}: {
  view: BookingView
  onClose: () => void
  onSubmitted: (view: BookingView) => void
}) {
  const [rating, setRating] = useState(0)
  const [text, setText] = useState('')
  const [isPending, setIsPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (isPending) {
      return
    }

    if (rating === 0) {
      setError('Поставьте оценку от 1 до 5.')
      return
    }

    setIsPending(true)
    setError(null)

    try {
      await createReview({
        bookingId: view.booking.id,
        rating,
        text: text.trim() === '' ? null : text.trim(),
      })
      onSubmitted(view)
      onClose()
    } catch (cause) {
      setError(describeReviewError(cause))
      setIsPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Отзыв о визите</DialogTitle>
        <DialogDescription>
          {formatDateTime(view.booking.startAt, view.timeZoneId)} ·{' '}
          {view.barberName}
        </DialogDescription>
      </DialogHeader>

      <form className="flex flex-col gap-4" onSubmit={handleSubmit} noValidate>
        <fieldset className="flex flex-col gap-2">
          <legend className="text-sm font-medium">Оценка</legend>
          <div className="flex items-center gap-0.5">
            {RATINGS.map((value) => {
              const isActive = value <= rating

              return (
                <button
                  key={value}
                  type="button"
                  aria-pressed={rating === value}
                  aria-label={`${value} из 5`}
                  disabled={isPending}
                  onClick={() => {
                    setRating(value)
                    setError(null)
                  }}
                  className="rounded-md p-1 outline-none transition-colors focus-visible:ring-3 focus-visible:ring-ring/50 disabled:opacity-50"
                >
                  <Star
                    className={cn(
                      'size-7 transition-colors',
                      isActive
                        ? 'fill-current text-foreground'
                        : 'text-muted-foreground/40 hover:text-muted-foreground'
                    )}
                    aria-hidden
                  />
                </button>
              )
            })}
            <span className="ml-2 text-sm text-muted-foreground tabular-nums">
              {rating > 0 ? `${rating} / 5` : 'не выбрана'}
            </span>
          </div>
        </fieldset>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="review-text">
            Комментарий{' '}
            <span className="font-normal text-muted-foreground">
              — необязательно
            </span>
          </Label>
          <Textarea
            id="review-text"
            name="text"
            value={text}
            maxLength={MAX_TEXT_LENGTH}
            disabled={isPending}
            placeholder="Что понравилось, что можно улучшить"
            onChange={(event) => {
              setText(event.target.value)
              setError(null)
            }}
          />
          <p className="text-right text-xs text-muted-foreground tabular-nums">
            {text.length} / {MAX_TEXT_LENGTH}
          </p>
        </div>

        <p className="text-xs text-muted-foreground">
          Отзыв появится на карточке барбера после проверки модератором.
        </p>

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
            Отмена
          </Button>
          <Button type="submit" disabled={isPending}>
            {isPending && (
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
            )}
            {isPending ? 'Отправляем…' : 'Отправить отзыв'}
          </Button>
        </DialogFooter>
      </form>
    </>
  )
}
