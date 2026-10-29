import { Loader2, Search, TriangleAlert } from 'lucide-react'

import { BarberCard } from '@/components/home/barber-card'
import { Button } from '@/components/ui/button'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import type { BarberSearchStatus } from '@/hooks/use-barber-search'
import type { BarberSearchFilters, BarberSearchItem } from '@/lib/barber-search'

type BarberResultsProps = {
  items: BarberSearchItem[]
  status: BarberSearchStatus
  error: string | null
  loadMoreError: string | null
  hasMore: boolean
  isLoadingMore: boolean
  /** Последние отправленные фильтры: по ним выбирается подсказка пустой выдачи. */
  filters: BarberSearchFilters
  onRetry: () => void
  onLoadMore: () => void
}

const SKELETON_KEYS = Array.from(
  { length: 6 },
  (_, index) => `skeleton-${index}`
)

/** «Показан 1 барбер», «Показаны 2 барбера», «Показаны 5 барберов». */
function shownLabel(count: number): string {
  const mod10 = count % 10
  const mod100 = count % 100

  if (mod10 === 1 && mod100 !== 11) {
    return `Показан ${count} барбер`
  }

  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) {
    return `Показаны ${count} барбера`
  }

  return `Показаны ${count} барберов`
}

/**
 * Секция результатов: заголовок, счётчик, сетка и состояния загрузки.
 *
 * Живой регион объявляет загрузку и число карточек; ошибка первой страницы —
 * `role="alert"`. Ошибка дозагрузки показывается у кнопки и не стирает список.
 */
export function BarberResults({
  items,
  status,
  error,
  loadMoreError,
  hasMore,
  isLoadingMore,
  filters,
  onRetry,
  onLoadMore,
}: BarberResultsProps) {
  const isEmpty = status === 'ready' && items.length === 0
  const liveMessage =
    status === 'loading'
      ? 'Загружаем барберов…'
      : status === 'error'
        ? ''
        : items.length === 0
          ? 'Барберы не найдены.'
          : shownLabel(items.length)

  return (
    <section
      id="barbers"
      aria-labelledby="barbers-heading"
      className="mx-auto max-w-5xl scroll-mt-14 px-4 py-14"
    >
      <h2
        id="barbers-heading"
        className="font-heading text-2xl font-semibold tracking-tight"
      >
        Барберы рядом
      </h2>
      <p className="mt-1 text-sm text-muted-foreground">
        Подтверждённые барберы с рейтингом, стажем и салоном.
      </p>

      <Separator className="my-6" />

      <p aria-live="polite" className="sr-only">
        {liveMessage}
      </p>

      {status === 'loading' && <BarberResultsSkeleton />}

      {status === 'error' && (
        <BarberResultsError
          message={error ?? 'Не удалось загрузить барберов.'}
          onRetry={onRetry}
        />
      )}

      {isEmpty && <BarberResultsEmpty filters={filters} />}

      {status === 'ready' && items.length > 0 && (
        <>
          <p className="mb-4 text-sm text-muted-foreground">
            {shownLabel(items.length)}
          </p>

          <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {items.map((barber) => (
              <li key={barber.id}>
                <BarberCard barber={barber} />
              </li>
            ))}
          </ul>

          {hasMore && (
            <div className="mt-8 flex flex-col items-center gap-3">
              {loadMoreError !== null && (
                <p role="alert" className="text-center text-sm text-destructive">
                  {loadMoreError}
                </p>
              )}
              <Button
                variant="outline"
                className="h-11 sm:h-10"
                aria-busy={isLoadingMore}
                onClick={onLoadMore}
              >
                {isLoadingMore && (
                  <Loader2
                    data-icon="inline-start"
                    aria-hidden
                    className="animate-spin"
                  />
                )}
                {isLoadingMore ? 'Загружаем…' : 'Показать ещё'}
              </Button>
            </div>
          )}
        </>
      )}
    </section>
  )
}

function BarberResultsSkeleton() {
  return (
    <div aria-busy="true">
      <ul
        aria-hidden="true"
        className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3"
      >
        {SKELETON_KEYS.map((key) => (
          <li key={key} className="rounded-xl p-4 ring-1 ring-foreground/10">
            <div className="flex items-start gap-3">
              <Skeleton className="size-10 shrink-0 rounded-full" />
              <div className="flex-1 space-y-2">
                <Skeleton className="h-4 w-3/4" />
                <Skeleton className="h-3 w-1/2" />
              </div>
            </div>
            <Skeleton className="mt-4 h-4 w-2/3" />
            <Skeleton className="mt-2 h-3 w-1/3" />
          </li>
        ))}
      </ul>
    </div>
  )
}

function BarberResultsError({
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
        <p className="font-medium">Не удалось загрузить барберов.</p>
        <p
          role="alert"
          className="mx-auto max-w-sm text-sm text-muted-foreground"
        >
          {message}
        </p>
      </div>
      <Button variant="outline" className="h-11 sm:h-10" onClick={onRetry}>
        Повторить
      </Button>
    </div>
  )
}

function BarberResultsEmpty({ filters }: { filters: BarberSearchFilters }) {
  const city = filters.city.trim()

  let title = 'Барберов пока нет'
  let text = 'Пока никто не подтверждён. Загляните позже.'

  if (city !== '') {
    title = 'В этом городе никого не нашли'
    text =
      'Проверьте написание — город учитывается точно, например «Москва».'
  } else if (filters.date !== '') {
    title = 'На выбранную дату никого не нашли'
    text =
      'Фильтр оставляет барберов с рабочим днём в этот день недели. Попробуйте другую дату.'
  }

  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <Search className="size-4 text-muted-foreground" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">{title}</p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">{text}</p>
      </div>
    </div>
  )
}
