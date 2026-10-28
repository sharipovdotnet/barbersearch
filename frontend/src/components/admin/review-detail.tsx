import { useState, type ReactNode } from 'react'
import { ArrowLeft, Loader2, Star } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import type { AdminReview } from '@/lib/admin-reviews'
import type { AdminTab } from '@/lib/admin-path'
import {
  formatDateTime,
  formatPrice,
  formatRating,
  formatReviewsCount,
} from '@/lib/format'
import { cn } from '@/lib/utils'

const STARS = [1, 2, 3, 4, 5] as const

/**
 * Деталь отзыва: место, где админ читает контекст и принимает решение.
 *
 * Текст клиента выводится как текст: React экранирует его, `dangerouslySetInnerHTML`
 * здесь не нужен и запрещён правилами проекта.
 */
export function ReviewDetail({
  review,
  tab,
  pendingAction,
  actionError,
  onPublish,
  onSkip,
  onHide,
  onBack,
}: {
  review: AdminReview
  tab: AdminTab
  pendingAction: 'publish' | 'hide' | null
  actionError: string | null
  onPublish: () => void
  onSkip: () => void
  onHide: () => void
  onBack: () => void
}) {
  const [isConfirmOpen, setIsConfirmOpen] = useState(false)
  const isPending = pendingAction !== null
  const summary =
    review.barber.rating === null
      ? 'У барбера пока нет опубликованных отзывов.'
      : `Сейчас у барбера ${formatRating(review.barber.rating)}, ${formatReviewsCount(review.barber.reviewsCount)}.`

  return (
    <section
      id="review-detail"
      aria-labelledby="review-detail-title"
      aria-busy={isPending}
      className="overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10"
    >
      <h2 id="review-detail-title" className="sr-only">
        Отзыв
      </h2>

      <div className="space-y-5 p-4 pb-32 sm:p-5 lg:pb-5">
        <div className="flex items-center justify-between gap-3">
          <Button
            variant="ghost"
            size="sm"
            className="h-11 sm:h-7 lg:hidden"
            onClick={onBack}
          >
            <ArrowLeft data-icon="inline-start" aria-hidden />
            Назад
          </Button>
          <span className="ml-auto text-xs text-muted-foreground">
            {tab === 'pending' ? 'На модерации' : 'Опубликован'}
          </span>
        </div>

        <div className="flex items-baseline gap-3">
          <span className="font-heading text-5xl leading-none font-medium tracking-tight tabular-nums sm:text-6xl">
            {review.rating}
          </span>
          <div className="space-y-1">
            <div className="flex items-center gap-0.5" aria-hidden>
              {STARS.map((value) => (
                <Star
                  key={value}
                  className={cn(
                    'size-3.5',
                    value <= review.rating
                      ? 'fill-current text-foreground'
                      : 'text-muted-foreground/30'
                  )}
                />
              ))}
            </div>
            <p className="text-xs text-muted-foreground tabular-nums">
              {review.rating} из 5
            </p>
          </div>
        </div>

        {review.text ? (
          <p className="max-w-prose text-lg leading-relaxed break-words whitespace-pre-line">
            {review.text}
          </p>
        ) : (
          <p className="text-sm text-muted-foreground">
            Клиент поставил оценку без комментария.
          </p>
        )}

        <dl className="grid gap-4 border-t pt-4 sm:grid-cols-2">
          <ContextItem label="Барбер">
            <span className="flex flex-wrap items-center gap-1.5">
              {review.barber.displayName}
              {!review.barber.isVerified && (
                <Badge variant="outline">Не подтверждён</Badge>
              )}
              {!review.barber.isActive && (
                <Badge variant="outline">Скрыт из поиска</Badge>
              )}
            </span>
          </ContextItem>

          <ContextItem label="Клиент">
            <span className="block">{review.customer.fullName}</span>
            <span className="block break-all text-xs text-muted-foreground">
              {review.customer.email}
            </span>
          </ContextItem>

          <ContextItem label="Визит">
            <span className="block">
              {review.visit.serviceName} · {review.visit.salonName}
            </span>
            <span className="block text-xs text-muted-foreground">
              {formatDateTime(review.visit.startAt, review.visit.timeZoneId)} ·{' '}
              {formatPrice(review.visit.price)}
            </span>
          </ContextItem>

          <ContextItem label="Отзыв оставлен">
            <span className="block">{formatDateTime(review.createdAt)}</span>
          </ContextItem>
        </dl>

        <div className="rounded-lg border bg-muted/40 p-3 text-sm text-muted-foreground">
          {summary}{' '}
          {tab === 'pending'
            ? 'После публикации рейтинг пересчитается.'
            : 'После скрытия рейтинг пересчитается.'}
        </div>
      </div>

      <div className="fixed inset-x-0 bottom-0 z-20 border-t bg-background/95 p-4 backdrop-blur lg:sticky lg:inset-auto lg:bottom-0 lg:z-auto lg:rounded-b-xl">
        {actionError && (
          <p role="alert" className="mb-3 text-sm text-destructive">
            {actionError}
          </p>
        )}

        {tab === 'pending' ? (
          <div className="flex flex-col gap-2 sm:flex-row">
            <Button
              className="h-11 flex-1 sm:h-9"
              disabled={isPending}
              onClick={onPublish}
            >
              {pendingAction === 'publish' ? (
                <>
                  <Loader2
                    data-icon="inline-start"
                    aria-hidden
                    className="animate-spin"
                  />
                  Публикуем…
                </>
              ) : (
                'Опубликовать'
              )}
            </Button>
            <Button
              variant="ghost"
              className="h-11 sm:h-9"
              disabled={isPending}
              onClick={onSkip}
            >
              Пропустить
            </Button>
          </div>
        ) : (
          <Button
            variant="destructive"
            className="h-11 w-full sm:h-9 sm:w-auto"
            disabled={isPending}
            onClick={() => setIsConfirmOpen(true)}
          >
            {pendingAction === 'hide' ? (
              <>
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
                Скрываем…
              </>
            ) : (
              'Скрыть отзыв'
            )}
          </Button>
        )}
      </div>

      <Dialog open={isConfirmOpen} onOpenChange={setIsConfirmOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Скрыть отзыв?</DialogTitle>
            <DialogDescription>
              Отзыв исчезнет с карточки барбера, рейтинг пересчитается. Вернуть
              отзыв можно в любой момент — он появится в очереди модерации.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setIsConfirmOpen(false)}
              disabled={isPending}
            >
              Отмена
            </Button>
            <Button
              variant="destructive"
              disabled={isPending}
              onClick={() => {
                setIsConfirmOpen(false)
                onHide()
              }}
            >
              Скрыть отзыв
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  )
}

function ContextItem({
  label,
  children,
}: {
  label: string
  children: ReactNode
}) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
        {label}
      </dt>
      <dd className="mt-1 text-sm">{children}</dd>
    </div>
  )
}
