import { useState, type FormEvent } from 'react'
import { Loader2, Star } from 'lucide-react'

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
import { describeApiFailure } from '@/lib/errors'
import { formatDateTime } from '@/lib/format'
import type { MyReview, UpdateMyReviewInput } from '@/lib/my-reviews'
import { cn } from '@/lib/utils'

const MAX_TEXT_LENGTH = 2000
const RATINGS = [1, 2, 3, 4, 5] as const

/** Правка меняет строку на месте: повторной модерации и сброса пагинации нет. */
function describeEditError(error: unknown): string {
  if (error instanceof ApiError) {
    if (
      error.status === 409 ||
      error.hasCode('review.update.concurrencyConflict')
    ) {
      return 'Отзыв изменился в другой вкладке — обновите список.'
    }

    if (error.status === 404 || error.hasCode('review.notFound')) {
      return 'Отзыв больше не существует.'
    }

    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return 'Править отзыв может только его автор.'
    }

    if (error.hasCode('review.update.rating')) {
      return 'Оценка должна быть от 1 до 5.'
    }

    if (error.hasCode('review.update.text.tooLong')) {
      return `Слишком длинный текст: максимум ${MAX_TEXT_LENGTH} символов.`
    }
  }

  return describeApiFailure(error, 'Не удалось сохранить отзыв.')
}

/**
 * Диалог правки отзыва.
 *
 * Содержимое монтируется на каждое открытие: оценка и текст не переезжают на другой
 * отзыв, сбрасывать их эффектом не нужно.
 */
export function EditReviewDialog({
  review,
  onClose,
  onSave,
}: {
  review: MyReview | null
  onClose: () => void
  onSave: (id: string, input: UpdateMyReviewInput) => Promise<void>
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
        <DialogContent className="sm:max-w-md">
          <EditReviewForm review={review} onClose={onClose} onSave={onSave} />
        </DialogContent>
      )}
    </Dialog>
  )
}

function EditReviewForm({
  review,
  onClose,
  onSave,
}: {
  review: MyReview
  onClose: () => void
  onSave: (id: string, input: UpdateMyReviewInput) => Promise<void>
}) {
  const [rating, setRating] = useState(review.rating)
  const [text, setText] = useState(review.text ?? '')
  const [isPending, setIsPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (isPending) {
      return
    }

    if (rating < 1 || rating > 5) {
      setError('Оценка должна быть от 1 до 5.')
      return
    }

    if (text.length > MAX_TEXT_LENGTH) {
      setError(`Слишком длинный текст: максимум ${MAX_TEXT_LENGTH} символов.`)
      return
    }

    setIsPending(true)
    setError(null)

    try {
      await onSave(review.id, {
        rating,
        text: text.trim() === '' ? null : text.trim(),
      })
      onClose()
    } catch (cause) {
      setError(describeEditError(cause))
    } finally {
      setIsPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Изменить отзыв</DialogTitle>
        <DialogDescription>
          {formatDateTime(review.visit.startAt, review.visit.timeZoneId)} ·{' '}
          {review.barber.displayName}
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
          <Label htmlFor="edit-review-text">
            Комментарий{' '}
            <span className="font-normal text-muted-foreground">
              — необязательно
            </span>
          </Label>
          <Textarea
            id="edit-review-text"
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
          Опубликованный отзыв изменится на карточке барбера сразу — на повторную
          проверку он не уйдёт.
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
          <Button type="submit" disabled={isPending} aria-busy={isPending}>
            {isPending && (
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
            )}
            {isPending ? 'Сохраняем…' : 'Сохранить'}
          </Button>
        </DialogFooter>
      </form>
    </>
  )
}
