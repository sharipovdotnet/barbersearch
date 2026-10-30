import { useEffect, useRef, type RefObject } from 'react'
import { Loader2, Star, TriangleAlert } from 'lucide-react'

import { MyReviewRow } from '@/components/account/my-review-row'
import { Button, buttonVariants } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import type { MyReviewsStatus } from '@/hooks/use-my-reviews'
import { formatReviewsCount } from '@/lib/format'
import type { MyReview } from '@/lib/my-reviews'
import { isPlainClick, navigate } from '@/lib/router'

const SKELETON_KEYS = Array.from(
  { length: 3 },
  (_, index) => `my-review-skeleton-${index}`
)

/**
 * Список своих отзывов: пустое состояние, ошибки и догрузка.
 *
 * Ошибка дозагрузки живёт отдельно от первой страницы: уже показанные строки
 * остаются, а кнопка предлагает повтор. После последней страницы фокус уходит на
 * заголовок списка, иначе он терялся бы вместе с исчезнувшей кнопкой.
 */
export function MyReviewsList({
  items,
  status,
  error,
  nextCursor,
  isLoadingMore,
  loadMoreError,
  headingRef,
  onRetry,
  onLoadMore,
  onRequestEdit,
  onRequestDelete,
  registerEditButton,
  registerDeleteButton,
}: {
  items: MyReview[]
  status: MyReviewsStatus
  error: string | null
  nextCursor: string | null
  isLoadingMore: boolean
  loadMoreError: string | null
  headingRef: RefObject<HTMLHeadingElement | null>
  onRetry: () => void
  onLoadMore: () => void
  onRequestEdit: (review: MyReview) => void
  onRequestDelete: (review: MyReview) => void
  registerEditButton: (id: string, element: HTMLButtonElement | null) => void
  registerDeleteButton: (id: string, element: HTMLButtonElement | null) => void
}) {
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
    headingRef.current?.focus()
  }, [nextCursor, status, headingRef])

  const isLoading = status === 'loading' || status === 'idle'
  const isEmpty =
    status === 'ready' &&
    items.length === 0 &&
    !isLoadingMore &&
    loadMoreError === null

  const liveMessage = isLoading
    ? 'Загружаем отзывы…'
    : status === 'error'
      ? ''
      : isEmpty
        ? 'Пока нет отзывов.'
        : `Загружено ${formatReviewsCount(items.length)}`

  return (
    <section aria-labelledby="my-reviews-heading" className="space-y-3">
      <p aria-live="polite" className="sr-only">
        {liveMessage}
      </p>

      <h2
        id="my-reviews-heading"
        ref={headingRef}
        tabIndex={-1}
        className="rounded-md font-heading text-lg font-semibold tracking-tight focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
      >
        Отзывы
      </h2>

      {isLoading && <ReviewsSkeleton />}

      {status === 'error' && (
        <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
          <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
            <TriangleAlert
              className="size-4 text-destructive"
              aria-hidden
            />
          </div>
          <div className="space-y-1">
            <p className="font-medium">Не удалось загрузить отзывы</p>
            <p
              role="alert"
              className="mx-auto max-w-sm text-sm text-muted-foreground"
            >
              {error ?? 'Не удалось загрузить отзывы.'}
            </p>
          </div>
          <Button variant="outline" size="sm" onClick={onRetry}>
            Повторить
          </Button>
        </div>
      )}

      {isEmpty && (
        <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
          <div className="flex size-10 items-center justify-center rounded-full bg-muted">
            <Star className="size-4 text-muted-foreground" aria-hidden />
          </div>
          <div className="space-y-1">
            <p className="font-medium">Пока нет отзывов.</p>
            <p className="mx-auto max-w-sm text-sm text-muted-foreground">
              Отзыв можно оставить в «Моих записях» после завершённого визита.
            </p>
          </div>
          <a
            href="/account"
            className={buttonVariants({ size: 'sm' })}
            onClick={(event) => {
              if (isPlainClick(event)) {
                event.preventDefault()
                navigate('/account')
              }
            }}
          >
            Перейти к записям
          </a>
        </div>
      )}

      {status === 'ready' && items.length > 0 && (
        <>
          <ul className="divide-y rounded-xl border bg-card">
            {items.map((review) => (
              <MyReviewRow
                key={review.id}
                review={review}
                onRequestEdit={onRequestEdit}
                onRequestDelete={onRequestDelete}
                registerEditButton={registerEditButton}
                registerDeleteButton={registerDeleteButton}
              />
            ))}
          </ul>

          {nextCursor !== null && (
            <div className="flex flex-col items-center gap-2 pt-1">
              {loadMoreError !== null && (
                <p role="alert" className="text-center text-sm text-destructive">
                  {loadMoreError}
                </p>
              )}
              <Button
                type="button"
                variant="outline"
                aria-busy={isLoadingMore}
                className="h-11 sm:h-9"
                onClick={() => {
                  focusAfterLastPage.current = true
                  onLoadMore()
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
    </section>
  )
}

function ReviewsSkeleton() {
  return (
    <div aria-busy="true">
      <ul aria-hidden="true" className="divide-y rounded-xl border bg-card">
        {SKELETON_KEYS.map((key) => (
          <li key={key} className="space-y-2 px-4 py-4">
            <div className="flex flex-wrap items-center gap-3">
              <Skeleton className="h-4 w-24" />
              <Skeleton className="h-5 w-24 rounded-4xl" />
            </div>
            <Skeleton className="h-4 w-40" />
            <Skeleton className="h-4 w-full" />
            <Skeleton className="h-4 w-2/3" />
            <div className="flex gap-2">
              <Skeleton className="h-8 w-24" />
              <Skeleton className="h-8 w-24" />
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}
