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
import { t, useI18n } from '@/i18n'
import { ApiError } from '@/lib/api'
import { describeApiFailure } from '@/lib/errors'
import { formatDateTime } from '@/lib/format'
import type { MyReview, UpdateMyReviewInput } from '@/lib/my-reviews'
import { cn } from '@/lib/utils'

const MAX_TEXT_LENGTH = 2000
const RATINGS = [1, 2, 3, 4, 5] as const

/**
 * Правка меняет строку на месте: сброса пагинации нет.
 *
 * Правка текста опубликованного отзыва возвращает его на модерацию — статус приходит
 * в ответе `PUT`, поэтому строка кабинета не ждёт перезагрузки, чтобы показать бейдж.
 */
function describeEditError(error: unknown): string {
  if (error instanceof ApiError) {
    if (
      error.status === 409 ||
      error.hasCode('review.update.concurrencyConflict')
    ) {
      return t('Отзыв изменился в другой вкладке — обновите список.')
    }

    if (error.status === 404 || error.hasCode('review.notFound')) {
      return t('Отзыв больше не существует.')
    }

    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return t('Править отзыв может только его автор.')
    }

    if (error.hasCode('review.update.rating')) {
      return t('Оценка должна быть от 1 до 5.')
    }

    if (error.hasCode('review.update.text.tooLong')) {
      return t('Слишком длинный текст: максимум {count} символов.', {
        count: MAX_TEXT_LENGTH,
      })
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
  const { t } = useI18n()
  const [rating, setRating] = useState(review.rating)
  const [text, setText] = useState(review.text ?? '')
  const [isPending, setIsPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Правило сервера: текст опубликованного отзыва уходит на повторную проверку,
  // одна оценка — нет. Подсказка должна совпадать с тем, что произойдёт после «Сохранить».
  const textChanged = text.trim() !== (review.text ?? '').trim()

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (isPending) {
      return
    }

    if (rating < 1 || rating > 5) {
      setError(t('Оценка должна быть от 1 до 5.'))
      return
    }

    if (text.length > MAX_TEXT_LENGTH) {
      setError(
        t('Слишком длинный текст: максимум {count} символов.', {
          count: MAX_TEXT_LENGTH,
        })
      )
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
        <DialogTitle>{t('Изменить отзыв')}</DialogTitle>
        <DialogDescription>
          {formatDateTime(review.visit.startAt, review.visit.timeZoneId)} ·{' '}
          {review.barber.displayName}
        </DialogDescription>
      </DialogHeader>

      <form className="flex flex-col gap-4" onSubmit={handleSubmit} noValidate>
        <fieldset className="flex flex-col gap-2">
          <legend className="text-sm font-medium">{t('Оценка')}</legend>
          <div className="flex items-center gap-0.5">
            {RATINGS.map((value) => {
              const isActive = value <= rating

              return (
                <button
                  key={value}
                  type="button"
                  aria-pressed={rating === value}
                  aria-label={t('{rating} из 5', { rating: value })}
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
              {rating > 0 ? `${rating} / 5` : t('не выбрана')}
            </span>
          </div>
        </fieldset>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="edit-review-text">
            {t('Комментарий')}{' '}
            <span className="font-normal text-muted-foreground">
              {t('— необязательно')}
            </span>
          </Label>
          <Textarea
            id="edit-review-text"
            name="text"
            value={text}
            maxLength={MAX_TEXT_LENGTH}
            disabled={isPending}
            placeholder={t('Что понравилось, что можно улучшить')}
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
          {review.isPublished
            ? textChanged
              ? t(
                  'Изменение текста вернёт отзыв на проверку: до одобрения он не покажется на карточке барбера.'
                )
              : t(
                  'Оценку можно изменить сразу: повторная проверка нужна только для текста.'
                )
            : t(
                'Отзыв на проверке: изменения появятся на карточке после одобрения.'
              )}
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
            {t('Отмена')}
          </Button>
          <Button type="submit" disabled={isPending} aria-busy={isPending}>
            {isPending && (
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
            )}
            {isPending ? t('Сохраняем…') : t('Сохранить')}
          </Button>
        </DialogFooter>
      </form>
    </>
  )
}
