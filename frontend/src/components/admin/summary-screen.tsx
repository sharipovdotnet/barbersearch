import { Loader2, RefreshCw, TriangleAlert } from 'lucide-react'
import { useRef } from 'react'

import { AdminSignInGate } from '@/components/admin/admin-gate'
import { useAdminQueues } from '@/hooks/use-admin-queues'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useNow } from '@/hooks/use-now'
import { useI18n } from '@/i18n'
import type { AdminSummaryOldestReview } from '@/lib/admin-summary'
import { cataloguePath, photosPath, reviewsPath } from '@/lib/admin-path'
import {
  formatClockTime,
  formatProfilesCount,
  formatReviewsCount,
  formatSalonsCount,
  formatWaitingSince,
} from '@/lib/format'
import { navigate } from '@/lib/router'

/**
 * `/admin/summary` — «Что требует внимания»: блоки очередей одной страницей.
 *
 * Числа считает сервер (`GET api/admin/summary`) теми же предикатами, что фильтры
 * разделов, поэтому сводка не расходится с очередями. «Самый старый» отзыв —
 * первая строка FIFO-очереди модерации; ожидание показывается словами, а не датой:
 * это не визит, и пояс салона здесь не применяется.
 *
 * Данные и опрос берутся из `AdminQueuesProvider`: тот же снимок рисует счётчики
 * рейла, поэтому своего таймера у экрана нет — иначе на `/admin/summary` работали
 * бы два одинаковых опроса и числа могли разойтись.
 *
 * Экран только читает. Кнопка одна — «Обновить»; действий, графиков, периодов
 * и трендов нет: API не хранит историю агрегатов, а выдуманные числа запрещены.
 * Ссылки ведут в свои разделы настоящими `href`, поэтому работают средний клик
 * и «Открыть в новой вкладке».
 */
export function SummaryScreen() {
  const summary = useAdminQueues()
  const now = useNow()
  const headingRef = useRef<HTMLHeadingElement | null>(null)
  const { t } = useI18n()

  if (summary.requiresSignIn) {
    return (
      <div className="mx-auto w-full max-w-md py-6">
        <AdminSignInGate returnTo="/admin/summary" onSignedIn={summary.reload} />
      </div>
    )
  }

  const isFirstLoad = summary.status === 'loading'

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1
            ref={headingRef}
            tabIndex={-1}
            className="scroll-mt-20 font-heading text-2xl font-semibold tracking-tight sm:text-3xl"
          >
            {t('Сводка')}
          </h1>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
            {t(
              'Что требует внимания. Счётчики считает сервер, разделы открываются ссылками.'
            )}
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {summary.updatedAt !== null && (
            <span className="text-xs text-muted-foreground tabular-nums">
              {t('Обновлено в {time}', {
                time: formatClockTime(new Date(summary.updatedAt)),
              })}
            </span>
          )}
          <Button
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            disabled={isFirstLoad || summary.isRefreshing}
            onClick={summary.refresh}
          >
            {summary.isRefreshing ? (
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
            ) : (
              <RefreshCw data-icon="inline-start" aria-hidden />
            )}
            {summary.isRefreshing ? t('Обновляем…') : t('Обновить')}
          </Button>
        </div>
      </header>

      {summary.refreshError && (
        <p role="alert" className="text-sm text-destructive">
          {summary.refreshError}
        </p>
      )}

      <div aria-busy={isFirstLoad || summary.isRefreshing}>
        {isFirstLoad && <SummarySkeleton />}

        {summary.status === 'error' && summary.error !== null && (
          <SummaryError message={summary.error} onRetry={summary.reload} />
        )}

        {summary.status === 'ready' && summary.summary !== null && (
          <div className="divide-y divide-border border-t border-border">
            <ReviewsBlock reviews={summary.summary.reviews} now={now} />
            <BarbersBlock count={summary.summary.barbers.unverifiedCount} />
            <SalonsBlock count={summary.summary.salons.inactiveCount} />
            <PhotosBlock
              pendingAvatarsCount={summary.summary.photos.pendingAvatarsCount}
              pendingPortfolioCount={
                summary.summary.photos.pendingPortfolioCount
              }
            />
          </div>
        )}
      </div>
    </div>
  )
}

/**
 * Самый старый отзыв очереди: ожидание словами, оценка и имена.
 *
 * Дата не выводится: для отзыва важно «сколько ждёт», а не календарный день визита —
 * пояса салона здесь нет. Ожидание считается от попадания в очередь (`awaitingSince`),
 * поэтому отредактированный старый отзыв не показывает месяцы ожидания.
 */
function OldestReview({
  review,
  now,
}: {
  review: AdminSummaryOldestReview
  now: number
}) {
  const reviewPath = reviewsPath('pending', review.id)
  const { t } = useI18n()

  return (
    <div className="min-w-0 space-y-1 rounded-lg border p-3 text-sm">
      <p className="break-words">
        {t('Самый старый')}{' '}
        <span className="font-medium">
          {formatWaitingSince(review.awaitingSince, now)}
        </span>
      </p>
      <p>{t('Оценка: {value} из 5', { value: review.rating })}</p>
      <p className="break-words">
        {t('Клиент: {name}', { name: review.customerDisplayName })}
      </p>
      <p className="break-words">
        {t('Барбер: {name}', { name: review.barberDisplayName })}
      </p>
      <a
        href={reviewPath}
        className="inline-flex min-h-11 items-center rounded-md py-1.5 font-medium break-words underline-offset-4 hover:underline focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:min-h-8"
        onClick={(event) => {
          event.preventDefault()
          navigate(reviewPath)
        }}
      >
        {t('Открыть отзыв')}
      </a>
    </div>
  )
}

/** Блок очереди модерации: число, самый старый отзыв и ссылка в карточку. */
function ReviewsBlock({
  reviews,
  now,
}: {
  reviews: {
    pendingCount: number
    oldest: AdminSummaryOldestReview | null
  }
  now: number
}) {
  const { t } = useI18n()

  return (
    <section
      aria-labelledby="summary-reviews-heading"
      className="space-y-3 py-5 first:pt-4"
    >
      <div className="space-y-1">
        <h2
          id="summary-reviews-heading"
          className="font-heading text-lg font-semibold tracking-tight"
        >
          {t('Отзывы на модерации')}
        </h2>
        <p className="max-w-2xl text-sm text-muted-foreground">
          {t(
            'Скрытые отзывы ждут решения. Пока отзыв не опубликован, он не влияет на рейтинг барбера. В эту же очередь попадают и новые отзывы, и скрытые администратором — в домене это один флаг.'
          )}
        </p>
      </div>

      {reviews.pendingCount === 0 ? (
        <p className="text-sm font-medium">
          {t('Очередь пуста — все отзывы проверены.')}
        </p>
      ) : (
        <div className="space-y-3">
          <p className="text-sm">
            <span className="font-medium">
              {formatReviewsCount(reviews.pendingCount)}
            </span>{' '}
            {t('без публикации')}
          </p>

          {reviews.oldest !== null && (
            <OldestReview review={reviews.oldest} now={now} />
          )}

          <a
            href={reviewsPath('pending')}
            className="inline-flex min-h-11 items-center rounded-md text-sm font-medium break-words underline-offset-4 hover:underline focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:min-h-8"
            onClick={(event) => {
              event.preventDefault()
              navigate(reviewsPath('pending'))
            }}
          >
            {t('К очереди отзывов')}
          </a>
        </div>
      )}
    </section>
  )
}

/** Блок неподтверждённых барберов: ровно очередь «На проверке». */
function BarbersBlock({ count }: { count: number }) {
  const { t } = useI18n()

  return (
    <section
      aria-labelledby="summary-barbers-heading"
      className="space-y-3 py-5"
    >
      <div className="space-y-1">
        <h2
          id="summary-barbers-heading"
          className="font-heading text-lg font-semibold tracking-tight"
        >
          {t('Барберы без подтверждения')}
        </h2>
        <p className="max-w-2xl text-sm text-muted-foreground">
          {t('Профиль не появляется в выдаче, пока его не подтвердили.')}
        </p>
      </div>

      {count === 0 ? (
        <p className="text-sm font-medium">{t('Все профили подтверждены.')}</p>
      ) : (
        <div className="space-y-3">
          <p className="text-sm">
            <span className="font-medium">{formatProfilesCount(count)}</span>{' '}
            {t('без подтверждения')}
          </p>
          <a
            href="/admin/barbers"
            className="inline-flex min-h-11 items-center rounded-md text-sm font-medium underline-offset-4 hover:underline focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:min-h-8"
            onClick={(event) => {
              event.preventDefault()
              navigate('/admin/barbers')
            }}
          >
            {t('Открыть барберов')}
          </a>
        </div>
      )}
    </section>
  )
}

/** Блок выключенных салонов: ровно фильтр «Выключенные» каталога. */
function SalonsBlock({ count }: { count: number }) {
  const { t } = useI18n()

  return (
    <section
      aria-labelledby="summary-salons-heading"
      className="space-y-3 py-5"
    >
      <div className="space-y-1">
        <h2
          id="summary-salons-heading"
          className="font-heading text-lg font-semibold tracking-tight"
        >
          {t('Выключенные салоны')}
        </h2>
        <p className="max-w-2xl text-sm text-muted-foreground">
          {t(
            'Выключенный салон не участвует в поиске и не показывается клиентам; включается в каталоге.'
          )}
        </p>
      </div>

      {count === 0 ? (
        <p className="text-sm font-medium">{t('Выключенных салонов нет.')}</p>
      ) : (
        <div className="space-y-3">
          <p className="text-sm font-medium">{formatSalonsCount(count)}</p>
          <a
            href={cataloguePath('salons')}
            className="inline-flex min-h-11 items-center rounded-md text-sm font-medium underline-offset-4 hover:underline focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:min-h-8"
            onClick={(event) => {
              event.preventDefault()
              navigate(cataloguePath('salons'))
            }}
          >
            {t('Открыть каталог салонов')}
          </a>
        </div>
      )}
    </section>
  )
}

/**
 * Очереди премодерации фото: два числа и ссылки в обе вкладки `/admin/photos`.
 *
 * Счётчики приходят с сервера теми же предикатами, что чтения очередей; клиент
 * их не складывает и не пересчитывает. Ссылки — настоящие `href`, поэтому средний
 * клик открывает вкладку в новом окне. При нуле обеих очередей ссылок нет:
 * открывать нечего.
 */
function PhotosBlock({
  pendingAvatarsCount,
  pendingPortfolioCount,
}: {
  pendingAvatarsCount: number
  pendingPortfolioCount: number
}) {
  const { t } = useI18n()
  const isEmpty = pendingAvatarsCount === 0 && pendingPortfolioCount === 0
  const avatarsPath = photosPath('avatars')
  const portfolioPath = photosPath('portfolio')

  return (
    <section
      aria-labelledby="summary-photos-heading"
      className="space-y-3 py-5 last:pb-4"
    >
      <div className="space-y-1">
        <h2
          id="summary-photos-heading"
          className="font-heading text-lg font-semibold tracking-tight"
        >
          {t('Фотографии на проверке')}
        </h2>
        <p className="max-w-2xl text-sm text-muted-foreground">
          {t(
            'Новые аватары и работы не видны публично, пока администратор их не одобрит.'
          )}
        </p>
      </div>

      {isEmpty ? (
        <p className="text-sm font-medium">{t('Все фотографии проверены.')}</p>
      ) : (
        <div className="space-y-3">
          <dl className="grid gap-2 sm:grid-cols-2">
            <div className="rounded-lg border p-3">
              <dt className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
                {t('Аватары')}
              </dt>
              <dd className="mt-1 text-sm font-medium tabular-nums">
                {pendingAvatarsCount}
              </dd>
            </div>
            <div className="rounded-lg border p-3">
              <dt className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
                {t('Портфолио')}
              </dt>
              <dd className="mt-1 text-sm font-medium tabular-nums">
                {pendingPortfolioCount}
              </dd>
            </div>
          </dl>

          <div className="flex flex-wrap gap-x-4 gap-y-1">
            <a
              href={avatarsPath}
              className="inline-flex min-h-11 items-center rounded-md text-sm font-medium underline-offset-4 hover:underline focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:min-h-8"
              onClick={(event) => {
                event.preventDefault()
                navigate(avatarsPath)
              }}
            >
              {t('Открыть аватары')}
            </a>
            <a
              href={portfolioPath}
              className="inline-flex min-h-11 items-center rounded-md text-sm font-medium underline-offset-4 hover:underline focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:min-h-8"
              onClick={(event) => {
                event.preventDefault()
                navigate(portfolioPath)
              }}
            >
              {t('Открыть портфолио')}
            </a>
          </div>
        </div>
      )}
    </section>
  )
}

/** Скелетон-силуэт четырёх блоков: только первая загрузка. */
function SummarySkeleton() {
  const { t } = useI18n()

  return (
    <div className="space-y-5 border-t border-border pt-4" aria-busy="true">
      <p role="status" className="sr-only">
        {t('Загружаем сводку…')}
      </p>
      <div className="space-y-3" aria-hidden>
        <Skeleton className="h-5 w-48" />
        <Skeleton className="h-4 w-full max-w-md" />
        <Skeleton className="h-16 w-full rounded-lg" />
      </div>
      <div className="space-y-3" aria-hidden>
        <Skeleton className="h-5 w-56" />
        <Skeleton className="h-4 w-full max-w-sm" />
      </div>
      <div className="space-y-3" aria-hidden>
        <Skeleton className="h-5 w-44" />
        <Skeleton className="h-4 w-full max-w-sm" />
      </div>
      <div className="space-y-3" aria-hidden>
        <Skeleton className="h-5 w-52" />
        <Skeleton className="h-4 w-full max-w-sm" />
      </div>
    </div>
  )
}

/** Ошибка первой загрузки: причина и «Повторить», блоков нет. */
function SummaryError({
  message,
  onRetry,
}: {
  message: string
  onRetry: () => void
}) {
  const { t } = useI18n()

  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
        <TriangleAlert className="size-4 text-destructive" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">{t('Не удалось загрузить сводку')}</p>
        <p
          role="alert"
          className="mx-auto max-w-sm text-sm text-muted-foreground"
        >
          {message}
        </p>
      </div>
      <Button size="sm" variant="outline" className="h-11 sm:h-8" onClick={onRetry}>
        {t('Повторить')}
      </Button>
    </div>
  )
}
