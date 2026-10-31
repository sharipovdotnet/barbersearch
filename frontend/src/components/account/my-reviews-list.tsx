import { type RefObject } from 'react'
import { Star, TriangleAlert } from 'lucide-react'

import { MyReviewRow } from '@/components/account/my-review-row'
import { ListPager } from '@/components/list-pager'
import { Button, buttonVariants } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import type { PagedListStatus } from '@/hooks/use-paged-list'
import type { MyReview } from '@/lib/my-reviews'
import { formatPageRange } from '@/lib/paging'
import { isPlainClick, navigate } from '@/lib/router'

const SKELETON_KEYS = Array.from(
  { length: 3 },
  (_, index) => `my-review-skeleton-${index}`
)

/**
 * Список своих отзывов: пустое состояние, ошибки и постраничный футер.
 *
 * Страницы, их кэш и пустая страница живут в `usePagedList`: здесь только вывод
 * текущей страницы. Диапазон объявляет `ListPager`, поэтому второй такой озвучки
 * на экране нет; фокус после «Назад»/«Дальше» тоже возвращает он.
 */
export function MyReviewsList({
  items,
  status,
  error,
  pageIndex,
  offset,
  hasPrev,
  hasNext,
  isLoadingPage,
  pageError,
  headingRef,
  onRetry,
  onPrev,
  onNext,
  onRequestEdit,
  onRequestDelete,
  registerEditButton,
  registerDeleteButton,
}: {
  items: MyReview[]
  status: PagedListStatus
  error: string | null
  pageIndex: number
  offset: number
  hasPrev: boolean
  hasNext: boolean
  isLoadingPage: boolean
  pageError: string | null
  headingRef: RefObject<HTMLHeadingElement | null>
  onRetry: () => void
  onPrev: () => void
  onNext: () => void
  onRequestEdit: (review: MyReview) => void
  onRequestDelete: (review: MyReview) => void
  registerEditButton: (id: string, element: HTMLButtonElement | null) => void
  registerDeleteButton: (id: string, element: HTMLButtonElement | null) => void
}) {
  const isLoading = status === 'loading' || status === 'idle'
  const isEmpty = status === 'ready' && items.length === 0 && pageIndex === 0

  // Диапазон объявляет пагинатор: дублировать его здесь нельзя, остальные
  // состояния списка остаются в этой живой области.
  const liveMessage = isLoading
    ? 'Загружаем отзывы…'
    : status === 'error'
      ? ''
      : isEmpty
        ? 'Пока нет отзывов.'
        : ''

  return (
    <section aria-labelledby="my-reviews-heading" className="space-y-3">
      <p aria-live="polite" className="sr-only">
        {liveMessage}
      </p>

      <h2
        id="my-reviews-heading"
        ref={headingRef}
        tabIndex={-1}
        className="scroll-mt-20 rounded-md font-heading text-lg font-semibold tracking-tight focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
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
      )}

      <ListPager
        label="Страницы отзывов"
        rangeLabel={
          items.length === 0 ? '' : formatPageRange(offset, items.length)
        }
        hasPrev={hasPrev}
        hasNext={hasNext}
        isLoading={isLoadingPage}
        error={pageError}
        onPrev={onPrev}
        onNext={onNext}
        pageIndex={pageIndex}
        focusTargetRef={headingRef}
      />
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
