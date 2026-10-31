import { TriangleAlert } from 'lucide-react'
import { useCallback, useRef, type RefObject } from 'react'

import { BarberSectionHeading } from '@/components/barber/barber-section'
import { ListPager } from '@/components/list-pager'
import { ReviewStars } from '@/components/review-stars'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { usePagedList } from '@/hooks/use-paged-list'
import type { BarberProfile } from '@/lib/catalog'
import {
  formatRating,
  formatReviewDate,
  formatReviewsCount,
} from '@/lib/format'
import { formatPageRange } from '@/lib/paging'
import { fetchBarberReviews, type Review } from '@/lib/reviews'

const SKELETON_KEYS = Array.from(
  { length: 6 },
  (_, index) => `review-skeleton-${index}`
)

const REVIEWS_FALLBACK = 'Не удалось загрузить отзывы.'
const PAGE_FALLBACK = 'Не удалось загрузить следующие отзывы.'

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
      className="scroll-mt-20"
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
  const listRef = useRef<HTMLUListElement>(null)

  const fetchPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      fetchBarberReviews(profileId, cursor, signal),
    [profileId]
  )

  const {
    items,
    status,
    error,
    pageIndex,
    offset,
    hasPrev,
    hasNext,
    isLoadingPage,
    pageError,
    reload,
    goPrev,
    goNext,
  } = usePagedList<Review>({
    getId: (review) => review.id,
    fetchPage,
    fallback: REVIEWS_FALLBACK,
    pageFallback: PAGE_FALLBACK,
  })

  const isEmpty = status === 'ready' && items.length === 0 && pageIndex === 0

  // Диапазон объявляет пагинатор: второй такой же озвучки на экране быть не должно.
  const liveMessage =
    status === 'loading'
      ? 'Загружаем отзывы…'
      : status === 'error'
        ? ''
        : items.length === 0
          ? isEmpty
            ? 'Пока нет отзывов.'
            : 'На этой странице отзывов нет.'
          : ''

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
        <ReviewsError message={error ?? REVIEWS_FALLBACK} onRetry={reload} />
      )}

      {isEmpty && (
        <p className="text-sm text-muted-foreground">Пока нет отзывов.</p>
      )}

      {status === 'ready' && items.length === 0 && pageIndex > 0 && (
        <p className="text-sm text-muted-foreground">
          На этой странице отзывов нет.
        </p>
      )}

      {status === 'ready' && items.length > 0 && (
        <ul
          key={pageIndex}
          ref={listRef}
          tabIndex={-1}
          aria-label="Отзывы барбера"
          className="scroll-mt-20 divide-y rounded-lg motion-safe:animate-in motion-safe:fade-in-0 motion-safe:duration-200 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
        >
          {items.map((review) => (
            <ReviewCard key={review.id} review={review} timeZone={timeZone} />
          ))}
        </ul>
      )}

      <ListPager
        label="Страницы отзывов"
        rangeLabel={
          items.length === 0
            ? ''
            : formatPageRange(offset, items.length, reviewsCount)
        }
        hasPrev={hasPrev}
        hasNext={hasNext}
        isLoading={isLoadingPage}
        error={pageError}
        onPrev={goPrev}
        onNext={goNext}
        pageIndex={pageIndex}
        focusTargetRef={listRef}
        scrollTargetRef={sectionRef}
      />
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
