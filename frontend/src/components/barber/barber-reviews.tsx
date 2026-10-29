import { Loader2, Star, TriangleAlert } from 'lucide-react'
import { useEffect, useRef, type RefObject } from 'react'

import { BarberSectionHeading } from '@/components/barber/barber-section'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useBarberReviews } from '@/hooks/use-barber-reviews'
import type { BarberProfile } from '@/lib/catalog'
import {
  formatRating,
  formatReviewDate,
  formatReviewsCount,
} from '@/lib/format'
import type { Review } from '@/lib/reviews'

const SKELETON_KEYS = Array.from(
  { length: 6 },
  (_, index) => `review-skeleton-${index}`
)

const STAR_VALUES = [1, 2, 3, 4, 5] as const

/**
 * Секция «Отзывы» публичной карточки.
 *
 * Сводка берётся из профиля (`rating` и `reviewsCount`): среднее по загруженной
 * странице при пагинации было бы неверным. Список живёт в дочернем компоненте,
 * который монтируется только при `reviewsCount > 0`, поэтому у барбера без отзывов
 * нет запроса — и нет случайной ошибки сети.
 */
export function BarberReviews({ profile }: { profile: BarberProfile }) {
  const sectionRef = useRef<HTMLElement>(null)
  const primarySalon =
    profile.salons.find((salon) => salon.isPrimary) ?? profile.salons[0]

  return (
    <section
      ref={sectionRef}
      aria-labelledby="barber-reviews-heading"
      tabIndex={-1}
      className="scroll-mt-20 rounded-xl focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
    >
      <Card>
        <BarberSectionHeading id="barber-reviews-heading">
          Отзывы
        </BarberSectionHeading>

        {profile.reviewsCount === 0 ? (
          <CardContent>
            <p className="text-sm text-muted-foreground">Пока нет отзывов.</p>
          </CardContent>
        ) : (
          <BarberReviewsList
            key={profile.id}
            profileId={profile.id}
            rating={profile.rating}
            reviewsCount={profile.reviewsCount}
            timeZone={primarySalon?.timeZoneId}
            sectionRef={sectionRef}
          />
        )}
      </Card>
    </section>
  )
}

function BarberReviewsList({
  profileId,
  rating,
  reviewsCount,
  timeZone,
  sectionRef,
}: {
  profileId: string
  rating: number | null
  reviewsCount: number
  timeZone?: string
  sectionRef: RefObject<HTMLElement | null>
}) {
  const {
    items,
    status,
    error,
    nextCursor,
    isLoadingMore,
    loadMoreError,
    retry,
    loadMore,
  } = useBarberReviews(profileId)

  // Фокус уходит на секцию, когда «Показать ещё» исчезает вместе с последней страницей.
  const focusAfterLastPage = useRef(false)
  const previousCursor = useRef<string | null>(null)

  useEffect(() => {
    const hadMore = previousCursor.current !== null
    previousCursor.current = nextCursor

    if (
      !focusAfterLastPage.current ||
      !hadMore ||
      nextCursor !== null ||
      status !== 'ready'
    ) {
      return
    }

    focusAfterLastPage.current = false
    sectionRef.current?.focus()
  }, [nextCursor, status, sectionRef])

  const isEmpty = status === 'ready' && items.length === 0
  const liveMessage =
    status === 'loading'
      ? 'Загружаем отзывы…'
      : status === 'error'
        ? ''
        : isEmpty
          ? 'Пока нет отзывов.'
          : `Загружено ${formatReviewsCount(items.length)}`

  return (
    <CardContent className="space-y-4">
      <p className="text-sm">
        <span className="font-medium tabular-nums">{formatRating(rating)}</span>
        {' · '}
        <span className="text-muted-foreground">
          {formatReviewsCount(reviewsCount)}
        </span>
      </p>

      <p aria-live="polite" className="sr-only">
        {liveMessage}
      </p>

      {status === 'loading' && <ReviewsSkeleton />}

      {status === 'error' && (
        <ReviewsError
          message={error ?? 'Не удалось загрузить отзывы.'}
          onRetry={retry}
        />
      )}

      {isEmpty && (
        <p className="text-sm text-muted-foreground">Пока нет отзывов.</p>
      )}

      {status === 'ready' && items.length > 0 && (
        <>
          <ul className="divide-y">
            {items.map((review) => (
              <ReviewCard key={review.id} review={review} timeZone={timeZone} />
            ))}
          </ul>

          {nextCursor !== null && (
            <div className="flex flex-col items-center gap-2 pt-1">
              {loadMoreError !== null && (
                <p
                  role="alert"
                  className="text-center text-sm text-destructive"
                >
                  {loadMoreError}
                </p>
              )}
              <Button
                type="button"
                variant="outline"
                className="h-11 sm:h-10"
                aria-busy={isLoadingMore}
                onClick={() => {
                  focusAfterLastPage.current = true
                  loadMore()
                }}
              >
                {isLoadingMore && (
                  <Loader2
                    data-icon="inline-start"
                    aria-hidden
                    className="animate-spin"
                  />
                )}
                {isLoadingMore
                  ? 'Загружаем…'
                  : loadMoreError !== null
                    ? 'Повторить'
                    : 'Показать ещё'}
              </Button>
            </div>
          )}
        </>
      )}
    </CardContent>
  )
}

function ReviewCard({ review, timeZone }: { review: Review; timeZone?: string }) {
  const hasText = review.text !== null && review.text.trim() !== ''

  return (
    <li className="py-4 first:pt-0 last:pb-0">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <ReviewStars rating={review.rating} />
        <span className="text-sm font-medium tabular-nums">
          {formatRating(review.rating)}
        </span>
        <time
          dateTime={review.createdAt}
          className="text-sm text-muted-foreground"
        >
          {formatReviewDate(review.createdAt, timeZone)}
        </time>
      </div>

      {hasText ? (
        <p className="mt-2 text-sm leading-relaxed break-words whitespace-pre-line">
          {review.text}
        </p>
      ) : (
        <p className="mt-2 text-sm text-muted-foreground">Без комментария.</p>
      )}
    </li>
  )
}

/** Пять звёзд с текстовой альтернативой: оценка не передаётся только цветом. */
function ReviewStars({ rating }: { rating: number }) {
  const filled = Math.min(5, Math.max(0, Math.round(rating)))

  return (
    <span
      role="img"
      aria-label={`Оценка ${filled} из 5`}
      className="inline-flex items-center gap-0.5"
    >
      {STAR_VALUES.map((value) => (
        <Star
          key={value}
          aria-hidden
          className={
            value <= filled
              ? 'size-3.5 fill-current text-warning'
              : 'size-3.5 text-muted-foreground/30'
          }
        />
      ))}
    </span>
  )
}

function ReviewsSkeleton() {
  return (
    <div aria-busy="true">
      <ul aria-hidden="true" className="divide-y">
        {SKELETON_KEYS.map((key) => (
          <li key={key} className="space-y-2 py-4 first:pt-0 last:pb-0">
            <div className="flex flex-wrap items-center gap-3">
              <Skeleton className="h-4 w-20" />
              <Skeleton className="h-4 w-28" />
            </div>
            <Skeleton className="h-4 w-full" />
            <Skeleton className="h-4 w-2/3" />
          </li>
        ))}
      </ul>
    </div>
  )
}

function ReviewsError({
  message,
  onRetry,
}: {
  message: string
  onRetry: () => void
}) {
  return (
    <div className="flex flex-col gap-3 rounded-lg border border-destructive/30 p-3 sm:flex-row sm:items-center sm:justify-between">
      <div
        role="alert"
        className="flex items-start gap-2 text-sm text-muted-foreground"
      >
        <TriangleAlert
          className="mt-0.5 size-4 shrink-0 text-destructive"
          aria-hidden
        />
        <div className="space-y-0.5">
          <p className="font-medium text-foreground">
            Не удалось загрузить отзывы.
          </p>
          <p>{message}</p>
        </div>
      </div>
      <Button
        type="button"
        variant="outline"
        className="h-11 w-full shrink-0 sm:h-9 sm:w-auto"
        onClick={onRetry}
      >
        Повторить
      </Button>
    </div>
  )
}
