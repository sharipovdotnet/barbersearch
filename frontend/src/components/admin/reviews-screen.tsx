import { useEffect, useRef, useState } from 'react'
import {
  ArrowLeft,
  CheckCircle2,
  Inbox,
  Loader2,
  RefreshCw,
  TriangleAlert,
  X,
} from 'lucide-react'

import { AdminSignInGate } from '@/components/admin/admin-gate'
import { ModerationRulesDialog } from '@/components/admin/moderation-rules-dialog'
import { ReviewDetail } from '@/components/admin/review-detail'
import { ReviewQueue } from '@/components/admin/review-queue'
import { ReviewTabs } from '@/components/admin/review-tabs'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useAdminReview } from '@/hooks/use-admin-review'
import { useAdminReviewList } from '@/hooks/use-admin-review-list'
import { useMediaQuery } from '@/hooks/use-media-query'
import { useNow } from '@/hooks/use-now'
import {
  describeAdminReviewActionError,
  hideReview,
  isReviewGone,
  isUnauthorized,
  publishReview,
  type AdminReview,
} from '@/lib/admin-reviews'
import type { AdminTab } from '@/lib/admin-path'
import { reviewsPath } from '@/lib/admin-path'
import { formatClockTime } from '@/lib/format'
import { navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

type Announcement = {
  text: string
  undo?: {
    label: string
    action: 'publish' | 'hide'
    reviewId: string
  }
}

type PendingAction = {
  kind: 'publish' | 'hide'
  reviewId: string
} | null

/**
 * Экран «Отзывы»: очередь, деталь и действия модерации.
 *
 * Сетка — split-view на широком экране; на телефоне список и деталь сменяют друг друга,
 * а выбранная карточка живёт в адресе, поэтому перезагрузка не теряет контекст.
 */
export function ReviewsScreen({
  tab,
  reviewId,
}: {
  tab: AdminTab
  reviewId: string | null
}) {
  const list = useAdminReviewList(tab)
  const listItem =
    reviewId === null
      ? null
      : (list.items.find((review) => review.id === reviewId) ?? null)
  const detail = useAdminReview(
    reviewId,
    list.status === 'ready' && !listItem
  )
  const review = listItem ?? detail.review
  const isWide = useMediaQuery('(min-width: 1024px)')
  const now = useNow()
  const headingRef = useRef<HTMLHeadingElement | null>(null)

  const [pendingAction, setPendingAction] = useState<PendingAction>(null)
  const [actionFailure, setActionFailure] = useState<{
    reviewId: string
    message: string
  } | null>(null)
  const [announcement, setAnnouncement] = useState<Announcement | null>(null)
  const [focusRequest, setFocusRequest] = useState<{ id: string | null } | null>(
    null
  )
  const [requiresSignIn, setRequiresSignIn] = useState(false)

  const actionError =
    review && actionFailure?.reviewId === review.id
      ? actionFailure.message
      : null

  // На широком экране очередь открывается с первой карточкой; на телефоне — со списка.
  useEffect(() => {
    if (
      !isWide ||
      reviewId ||
      list.status !== 'ready' ||
      list.items.length === 0
    ) {
      return
    }

    navigate(reviewsPath(tab, list.items[0].id), { replace: true })
  }, [isWide, reviewId, list.status, list.items, tab])

  // На телефоне деталь заменяет список — переход должен начинаться сверху.
  useEffect(() => {
    if (!reviewId || isWide) {
      return
    }

    window.scrollTo({ top: 0 })
  }, [reviewId, isWide])

  function nextIdFor(target: AdminReview): string | null {
    const index = list.items.findIndex((item) => item.id === target.id)

    if (index === -1) {
      return null
    }

    return list.items[index + 1]?.id ?? list.items[index - 1]?.id ?? null
  }

  function selectAfterRemoval(target: AdminReview) {
    const nextId = nextIdFor(target)

    list.removeItem(target.id)
    setFocusRequest({ id: nextId })
    navigate(
      nextId ? reviewsPath(tab, nextId) : reviewsPath(tab),
      { replace: true }
    )

    // Если очередь опустела, строки для фокуса нет: возвращаем его на заголовок,
    // а не отпускаем в <body>.
    if (!nextId) {
      window.requestAnimationFrame(() => {
        window.requestAnimationFrame(() => headingRef.current?.focus())
      })
    }
  }

  async function handlePublish(target: AdminReview) {
    if (pendingAction) {
      return
    }

    setPendingAction({ kind: 'publish', reviewId: target.id })
    setActionFailure(null)

    try {
      await publishReview(target.id)
      selectAfterRemoval(target)
      setAnnouncement({
        text: 'Отзыв опубликован. Рейтинг барбера пересчитан.',
        undo: {
          label: 'Скрыть отзыв',
          action: 'hide',
          reviewId: target.id,
        },
      })
      list.reload()
    } catch (cause) {
      if (isUnauthorized(cause)) {
        setRequiresSignIn(true)
        return
      }

      if (isReviewGone(cause)) {
        selectAfterRemoval(target)
        setAnnouncement({ text: 'Этот отзыв уже не в очереди.' })
        return
      }

      setActionFailure({
        reviewId: target.id,
        message: describeAdminReviewActionError(cause, 'publish'),
      })
    } finally {
      setPendingAction(null)
    }
  }

  async function handleHide(target: AdminReview) {
    if (pendingAction) {
      return
    }

    setPendingAction({ kind: 'hide', reviewId: target.id })
    setActionFailure(null)

    try {
      await hideReview(target.id)
      selectAfterRemoval(target)
      setAnnouncement({
        text: 'Отзыв скрыт. Он снова в очереди модерации.',
        undo: {
          label: 'Опубликовать снова',
          action: 'publish',
          reviewId: target.id,
        },
      })
      list.reload()
    } catch (cause) {
      if (isUnauthorized(cause)) {
        setRequiresSignIn(true)
        return
      }

      if (isReviewGone(cause)) {
        selectAfterRemoval(target)
        setAnnouncement({ text: 'Этот отзыв уже не в очереди.' })
        return
      }

      setActionFailure({
        reviewId: target.id,
        message: describeAdminReviewActionError(cause, 'hide'),
      })
    } finally {
      setPendingAction(null)
    }
  }

  async function handleUndo(entry: Announcement) {
    if (!entry.undo || pendingAction) {
      return
    }

    const { action, reviewId: targetId } = entry.undo
    setPendingAction({ kind: action, reviewId: targetId })
    setAnnouncement(null)
    setActionFailure(null)

    try {
      if (action === 'publish') {
        await publishReview(targetId)
      } else {
        await hideReview(targetId)
      }

      setAnnouncement({
        text:
          action === 'publish'
            ? 'Отзыв снова опубликован.'
            : 'Отзыв снова скрыт и вернулся в очередь.',
      })
      list.reload()
    } catch (cause) {
      if (isUnauthorized(cause)) {
        setRequiresSignIn(true)
        return
      }

      if (isReviewGone(cause)) {
        setAnnouncement({ text: 'Этот отзыв уже не в очереди.' })
        return
      }

      setActionFailure({
        reviewId: targetId,
        message: describeAdminReviewActionError(cause, action),
      })
    } finally {
      setPendingAction(null)
    }
  }

  function handleSkip() {
    if (!review) {
      return
    }

    setActionFailure(null)

    const index = list.items.findIndex((item) => item.id === review.id)
    const nextId =
      index >= 0 ? list.items[index + 1]?.id : list.items[0]?.id

    if (nextId) {
      setFocusRequest({ id: nextId })
      navigate(reviewsPath(tab, nextId))
      return
    }

    setAnnouncement({
      text: 'Это последний отзыв в загруженной очереди.',
    })
  }

  function handleBack() {
    if (reviewId) {
      setFocusRequest({ id: reviewId })
    }

    navigate(reviewsPath(tab))
  }

  function handleRefresh() {
    setActionFailure(null)
    list.reload()
  }

  const returnTo = reviewId ? reviewsPath(tab, reviewId) : reviewsPath(tab)

  if (list.requiresSignIn || requiresSignIn) {
    return (
      <div className="mx-auto w-full max-w-md py-6">
        <AdminSignInGate
          returnTo={returnTo}
          onSignedIn={() => {
            setRequiresSignIn(false)
            list.reload()
          }}
        />
      </div>
    )
  }

  const isEmpty =
    list.status === 'ready' && list.items.length === 0 && !reviewId
  const showGrid =
    list.status === 'ready' && (list.items.length > 0 || reviewId !== null)
  const isMobileDetail = !isWide && reviewId !== null

  return (
    <div className="space-y-5">
      {isMobileDetail && (
        <h1 ref={headingRef} tabIndex={-1} className="sr-only">
          Отзывы
        </h1>
      )}

      {!isMobileDetail && (
        <header className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h1
              ref={headingRef}
              tabIndex={-1}
              className="font-heading text-2xl font-semibold tracking-tight sm:text-3xl"
            >
              Отзывы
            </h1>
            <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
              {tab === 'pending'
                ? 'Скрытые отзывы ждут решения. Пока отзыв скрыт, он не влияет на рейтинг барбера.'
                : 'Опубликованные отзывы можно скрыть: он исчезнет с карточки барбера и вернётся на модерацию.'}
            </p>
          </div>

          <div className="flex items-center gap-2">
            {list.updatedAt !== null && (
              <span className="text-xs text-muted-foreground tabular-nums">
                Обновлено в {formatClockTime(new Date(list.updatedAt))}
              </span>
            )}
            <Button
              variant="outline"
              size="sm"
              className="h-11 sm:h-7"
              disabled={list.isRefreshing || list.status === 'loading'}
              onClick={handleRefresh}
            >
              {list.isRefreshing ? (
                <Loader2 data-icon="inline-start" aria-hidden className="animate-spin" />
              ) : (
                <RefreshCw data-icon="inline-start" aria-hidden />
              )}
              {list.isRefreshing ? 'Обновляем…' : 'Обновить'}
            </Button>
          </div>
        </header>
      )}

      {list.refreshError && (
        <p role="alert" className="text-sm text-destructive">
          {list.refreshError}
        </p>
      )}

      {announcement && (
        <div
          role="status"
          className="flex flex-wrap items-start justify-between gap-2 rounded-lg border bg-muted/50 px-3 py-2 text-sm"
        >
          <p className="min-w-0 flex-1">{announcement.text}</p>
          <div className="flex items-center gap-1">
            {announcement.undo && (
              <Button
                variant="outline"
                size="sm"
                disabled={pendingAction !== null}
                onClick={() => {
                  void handleUndo(announcement)
                }}
              >
                {announcement.undo.label}
              </Button>
            )}
            <button
              type="button"
              aria-label="Скрыть сообщение"
              className="rounded-md p-1 text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
              onClick={() => setAnnouncement(null)}
            >
              <X className="size-3.5" aria-hidden />
            </button>
          </div>
        </div>
      )}

      {!isMobileDetail && (
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
          <ReviewTabs
            tab={tab}
            onChange={(next) => {
              setAnnouncement(null)
              setActionFailure(null)
              navigate(reviewsPath(next))
            }}
          />
          <ModerationRulesDialog />
        </div>
      )}

      {list.status === 'loading' && (
        <ReviewsSkeleton detailOnly={isMobileDetail} />
      )}

      {list.status === 'error' && (
        <ReviewsError
          message={list.error ?? 'Не удалось загрузить отзывы.'}
          onRetry={list.reload}
        />
      )}

      {isEmpty && (
        <ReviewsEmpty
          tab={tab}
          onRefresh={list.reload}
          onGoPending={() => navigate(reviewsPath('pending'))}
        />
      )}

      {showGrid && (
        <div
          id="reviews-panel"
          role="tabpanel"
          aria-labelledby={`reviews-tab-${tab}`}
          className="grid gap-5 lg:grid-cols-[20rem_minmax(0,1fr)] lg:items-start"
        >
          <div className={cn('min-w-0', reviewId ? 'hidden lg:block' : 'block')}>
            {list.items.length > 0 ? (
              <ReviewQueue
                items={list.items}
                selectedId={reviewId}
                tab={tab}
                now={now}
                nextCursor={list.nextCursor}
                isLoadingMore={list.isLoadingMore}
                loadMoreError={list.loadMoreError}
                focusRequest={focusRequest}
                onSelect={(id) => {
                  setActionFailure(null)
                  navigate(reviewsPath(tab, id))
                }}
                onLoadMore={list.loadMore}
              />
            ) : (
              <p className="text-sm text-muted-foreground">
                В загруженной очереди нет отзывов.
              </p>
            )}
          </div>

          <div className={cn('min-w-0', reviewId ? 'block' : 'hidden lg:block')}>
            {review ? (
              <ReviewDetail
                review={review}
                tab={tab}
                pendingAction={
                  pendingAction?.reviewId === review.id
                    ? pendingAction.kind
                    : null
                }
                actionError={actionError}
                onPublish={() => {
                  void handlePublish(review)
                }}
                onSkip={handleSkip}
                onHide={() => {
                  void handleHide(review)
                }}
                onBack={handleBack}
              />
            ) : (
              <div className="space-y-3">
                {isMobileDetail && (
                  <Button
                    variant="ghost"
                    size="sm"
                    className="h-11 sm:h-7"
                    onClick={handleBack}
                  >
                    <ArrowLeft data-icon="inline-start" aria-hidden />
                    Назад
                  </Button>
                )}
                <DetailState
                  hasSelection={reviewId !== null}
                  status={detail.status}
                  error={detail.error}
                  onRetry={detail.reload}
                />
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function ReviewsSkeleton({ detailOnly = false }: { detailOnly?: boolean }) {
  return (
    <div
      className={cn(
        'grid gap-5',
        !detailOnly && 'lg:grid-cols-[20rem_minmax(0,1fr)]'
      )}
      aria-busy="true"
    >
      <p role="status" className="sr-only">
        Загружаем отзывы…
      </p>
      {!detailOnly && (
        <div className="space-y-2" aria-hidden>
          <Skeleton className="h-4 w-32" />
          <Skeleton className="h-96 rounded-xl" />
        </div>
      )}
      <div className="space-y-4" aria-hidden>
        <Skeleton className="h-12 w-40" />
        <Skeleton className="h-6 w-80" />
        <Skeleton className="h-64 rounded-xl" />
      </div>
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
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
        <TriangleAlert className="size-4 text-destructive" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">Не удалось загрузить отзывы</p>
        <p role="alert" className="mx-auto max-w-sm text-sm text-muted-foreground">
          {message}
        </p>
      </div>
      <Button size="sm" variant="outline" className="h-11 sm:h-7" onClick={onRetry}>
        Повторить
      </Button>
    </div>
  )
}

function ReviewsEmpty({
  tab,
  onRefresh,
  onGoPending,
}: {
  tab: AdminTab
  onRefresh: () => void
  onGoPending: () => void
}) {
  const isPending = tab === 'pending'

  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        {isPending ? (
          <CheckCircle2 className="size-4 text-success" aria-hidden />
        ) : (
          <Inbox className="size-4 text-muted-foreground" aria-hidden />
        )}
      </div>
      <div className="space-y-1">
        <p className="font-medium">
          {isPending ? 'Очередь пуста' : 'Опубликованных отзывов пока нет'}
        </p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          {isPending
            ? 'Все отзывы проверены. Новые появятся здесь, как только клиенты их оставят.'
            : 'Когда отзыв опубликуют, он появится здесь — и его можно будет скрыть.'}
        </p>
      </div>
      <div className="flex flex-wrap items-center justify-center gap-2">
        {isPending ? (
          <Button
            size="sm"
            variant="outline"
            className="h-11 sm:h-7"
            onClick={onRefresh}
          >
            Обновить
          </Button>
        ) : (
          <Button
            size="sm"
            variant="outline"
            className="h-11 sm:h-7"
            onClick={onGoPending}
          >
            К очереди
          </Button>
        )}
      </div>
    </div>
  )
}

function DetailState({
  hasSelection,
  status,
  error,
  onRetry,
}: {
  hasSelection: boolean
  status: 'idle' | 'loading' | 'ready' | 'error' | 'notFound'
  error: string | null
  onRetry: () => void
}) {
  if (!hasSelection) {
    return (
      <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Inbox className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">Выберите отзыв</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            Слева очередь: откройте строку, чтобы прочитать текст и контекст визита.
          </p>
        </div>
      </div>
    )
  }

  if (status === 'notFound') {
    return (
      <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <CheckCircle2 className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">Этот отзыв уже разобрали</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            Возможно, его опубликовал другой администратор. Вернитесь к очереди —
            она обновится.
          </p>
        </div>
        <Button
          size="sm"
          variant="outline"
          className="h-11 sm:h-7"
          onClick={() => {
            navigate(reviewsPath('pending'))
          }}
        >
          К очереди
        </Button>
      </div>
    )
  }

  if (status === 'error') {
    return (
      <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
          <TriangleAlert className="size-4 text-destructive" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">Не удалось загрузить отзыв</p>
          <p role="alert" className="mx-auto max-w-sm text-sm text-muted-foreground">
            {error ?? 'Попробуйте ещё раз.'}
          </p>
        </div>
        <Button size="sm" variant="outline" className="h-11 sm:h-7" onClick={onRetry}>
          Повторить
        </Button>
      </div>
    )
  }

  return (
    <div className="space-y-3" aria-busy="true">
      <p role="status" className="sr-only">
        Загружаем отзыв…
      </p>
      <Skeleton className="h-12 w-40" aria-hidden />
      <Skeleton className="h-6 w-80" aria-hidden />
      <Skeleton className="h-64 rounded-xl" aria-hidden />
    </div>
  )
}
