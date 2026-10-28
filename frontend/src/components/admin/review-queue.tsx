import { useEffect, useRef, type KeyboardEvent } from 'react'
import { Loader2, Star } from 'lucide-react'

import { Button } from '@/components/ui/button'
import type { AdminReview } from '@/lib/admin-reviews'
import type { AdminTab } from '@/lib/admin-path'
import { formatShortDay, formatShortName, formatWaitingSince } from '@/lib/format'
import { cn } from '@/lib/utils'

const WARNING_AFTER_MS = 48 * 60 * 60 * 1000

/** Строка очереди: оценка, имя клиента и сколько отзыв ждёт решения. */
export function ReviewQueue({
  items,
  selectedId,
  tab,
  now,
  nextCursor,
  isLoadingMore,
  loadMoreError,
  focusRequest,
  onSelect,
  onLoadMore,
}: {
  items: AdminReview[]
  selectedId: string | null
  tab: AdminTab
  now: number
  nextCursor: string | null
  isLoadingMore: boolean
  loadMoreError: string | null
  focusRequest: { id: string | null } | null
  onSelect: (reviewId: string) => void
  onLoadMore: () => void
}) {
  const listRef = useRef<HTMLElement | null>(null)
  const rowRefs = useRef(new Map<string, HTMLButtonElement>())

  const selectedIndex = items.findIndex((review) => review.id === selectedId)
  const tabbableIndex = selectedIndex >= 0 ? selectedIndex : 0

  // После действия фокус уходит на следующую строку, а не в <body>.
  useEffect(() => {
    if (!focusRequest) {
      return
    }

    if (focusRequest.id) {
      rowRefs.current.get(focusRequest.id)?.focus()
    } else {
      listRef.current?.focus()
    }
  }, [focusRequest])

  function handleKeyDown(
    event: KeyboardEvent<HTMLButtonElement>,
    index: number
  ) {
    if (
      event.key !== 'ArrowDown' &&
      event.key !== 'ArrowUp' &&
      event.key !== 'Home' &&
      event.key !== 'End'
    ) {
      return
    }

    event.preventDefault()

    let nextIndex = index

    if (event.key === 'ArrowDown') {
      nextIndex = Math.min(items.length - 1, index + 1)
    } else if (event.key === 'ArrowUp') {
      nextIndex = Math.max(0, index - 1)
    } else if (event.key === 'Home') {
      nextIndex = 0
    } else {
      nextIndex = items.length - 1
    }

    const target = items[nextIndex]

    if (!target || target.id === selectedId) {
      return
    }

    onSelect(target.id)
    window.requestAnimationFrame(() => {
      rowRefs.current.get(target.id)?.focus()
    })
  }

  return (
    <section
      ref={listRef}
      id="reviews-list"
      aria-label="Очередь отзывов"
      tabIndex={-1}
      className="space-y-2 outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
    >
      <div className="flex items-center justify-between gap-2 px-1 text-xs text-muted-foreground">
        <span>{items.length > 0 ? `Показано ${items.length}` : 'Список пуст'}</span>
        <span>{tab === 'pending' ? 'Сначала старые' : 'Сначала новые'}</span>
      </div>

      <ul className="overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10">
        {items.map((review, index) => {
          const isSelected = review.id === selectedId
          const isOverdue =
            tab === 'pending' &&
            now - Date.parse(review.createdAt) > WARNING_AFTER_MS
          const secondaryText =
            tab === 'pending'
              ? formatWaitingSince(review.createdAt, now)
              : `опубликован ${formatShortDay(review.createdAt)}`

          return (
            <li key={review.id}>
              <button
                ref={(element) => {
                  if (element) {
                    rowRefs.current.set(review.id, element)
                  } else {
                    rowRefs.current.delete(review.id)
                  }
                }}
                type="button"
                aria-current={isSelected ? 'true' : undefined}
                aria-controls="review-detail"
                aria-label={`${review.customer.fullName}, оценка ${review.rating} из 5, ${secondaryText}`}
                tabIndex={index === tabbableIndex ? 0 : -1}
                onClick={() => onSelect(review.id)}
                onKeyDown={(event) => handleKeyDown(event, index)}
                className={cn(
                  'flex min-h-16 w-full items-center gap-3 border-l-2 px-3 py-2.5 text-left transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50',
                  isSelected
                    ? 'border-foreground bg-muted/50'
                    : 'border-transparent hover:bg-muted/30'
                )}
              >
                <span className="flex w-10 shrink-0 items-baseline gap-0.5 text-sm font-medium tabular-nums">
                  {review.rating}
                  <Star className="size-3 fill-current text-muted-foreground" aria-hidden />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-medium">
                    {formatShortName(review.customer.fullName)}
                  </span>
                  <span
                    className={cn(
                      'flex items-center gap-1.5 text-xs',
                      isOverdue ? 'text-foreground' : 'text-muted-foreground'
                    )}
                  >
                    {isOverdue && (
                      <span
                        className="size-1.5 shrink-0 rounded-full bg-warning"
                        aria-hidden
                      />
                    )}
                    {secondaryText}
                  </span>
                </span>
              </button>
            </li>
          )
        })}
      </ul>

      <div className="flex flex-col items-center gap-2 pt-1">
        {loadMoreError && (
          <p role="alert" className="text-sm text-destructive">
            {loadMoreError}
          </p>
        )}

        {nextCursor && (
          <Button
            variant="outline"
            size="sm"
            className="w-full"
            disabled={isLoadingMore}
            onClick={onLoadMore}
          >
            {isLoadingMore ? (
              <>
                <Loader2 data-icon="inline-start" aria-hidden className="animate-spin" />
                Загружаем…
              </>
            ) : (
              'Показать ещё'
            )}
          </Button>
        )}
      </div>
    </section>
  )
}
