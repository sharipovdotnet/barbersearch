import { Pencil, Trash2 } from 'lucide-react'

import { ReviewStatusBadge } from '@/components/account/review-status-badge'
import { ReviewStars } from '@/components/review-stars'
import { Button } from '@/components/ui/button'
import {
  formatDateTime,
  formatPrice,
  formatReviewDate,
} from '@/lib/format'
import type { MyReview } from '@/lib/my-reviews'
import { isPlainClick, navigate } from '@/lib/router'

/**
 * Строка «Моего отзыва».
 *
 * Контекст визита приходит в DTO `mine` целиком, поэтому строка не ходит в брони и
 * профиль барбера. GUID-ы остаются только в `href` ссылки на барбера: автора,
 * email и id брони в DOM нет.
 */
export function MyReviewRow({
  review,
  onRequestEdit,
  onRequestDelete,
  registerEditButton,
  registerDeleteButton,
}: {
  review: MyReview
  onRequestEdit: (review: MyReview) => void
  onRequestDelete: (review: MyReview) => void
  registerEditButton: (id: string, element: HTMLButtonElement | null) => void
  registerDeleteButton: (id: string, element: HTMLButtonElement | null) => void
}) {
  const { barber, visit } = review
  const hasText = review.text !== null && review.text.trim() !== ''
  const barberHref = `/barbers/${barber.id}`
  const visitLabel = formatDateTime(visit.startAt, visit.timeZoneId)

  return (
    <li className="px-4 py-4">
      <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
          <ReviewStars rating={review.rating} />
          <span className="text-sm font-medium tabular-nums">
            {review.rating} / 5
          </span>
          <ReviewStatusBadge isPublished={review.isPublished} />
        </div>

        <time
          dateTime={review.createdAt}
          className="text-xs text-muted-foreground"
        >
          {formatReviewDate(review.createdAt, visit.timeZoneId)}
        </time>
      </div>

      <p className="mt-2 text-sm">
        <a
          href={barberHref}
          className="font-medium underline-offset-4 transition-colors hover:text-muted-foreground hover:underline focus-visible:rounded-sm focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
          onClick={(event) => {
            // Средний клик и Ctrl-клик должны открыть новую вкладку штатно.
            if (isPlainClick(event)) {
              event.preventDefault()
              navigate(barberHref)
            }
          }}
        >
          {barber.displayName}
        </a>
      </p>

      <div className="mt-1 flex flex-wrap items-center gap-x-1.5 gap-y-1 text-sm text-muted-foreground">
        <time dateTime={visit.startAt}>{visitLabel}</time>
        <span aria-hidden>·</span>
        <span>{visit.serviceName}</span>
        <span aria-hidden>·</span>
        <span>{visit.salonName}</span>
        <span aria-hidden>·</span>
        <span className="tabular-nums">{formatPrice(visit.price)}</span>
      </div>

      {hasText ? (
        <p className="mt-3 text-sm leading-relaxed break-words whitespace-pre-line">
          {review.text}
        </p>
      ) : (
        <p className="mt-3 text-sm text-muted-foreground">Без комментария.</p>
      )}

      <div className="mt-3 flex flex-wrap gap-2">
        <Button
          ref={(element) => registerEditButton(review.id, element)}
          type="button"
          variant="outline"
          size="sm"
          className="h-11 sm:h-8"
          aria-label={`Изменить отзыв о визите ${visitLabel}`}
          onClick={() => onRequestEdit(review)}
        >
          <Pencil data-icon="inline-start" aria-hidden />
          Изменить
        </Button>

        <Button
          ref={(element) => registerDeleteButton(review.id, element)}
          type="button"
          variant="outline"
          size="sm"
          className="h-11 text-muted-foreground hover:text-destructive sm:h-8"
          aria-label={`Удалить отзыв о визите ${visitLabel}`}
          onClick={() => onRequestDelete(review)}
        >
          <Trash2 data-icon="inline-start" aria-hidden />
          Удалить
        </Button>
      </div>
    </li>
  )
}
