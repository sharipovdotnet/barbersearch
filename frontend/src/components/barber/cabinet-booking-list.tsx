import { useEffect, useRef, type KeyboardEvent, type RefObject } from 'react'
import { CalendarX, Loader2, TriangleAlert } from 'lucide-react'

import {
  CabinetBookingRow,
  type RowHandle,
} from '@/components/barber/cabinet-booking-row'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import type { BarberBookingsStatus } from '@/hooks/use-barber-bookings'
import type {
  BarberBooking,
  BarberBookingScope,
} from '@/lib/barber-bookings'
import { formatBookingsCount } from '@/lib/format'
import { cn } from '@/lib/utils'

const TABS = [
  { id: 'upcoming', label: 'Ближайшие' },
  { id: 'past', label: 'История' },
] as const

const SKELETON_KEYS = ['booking-skeleton-1', 'booking-skeleton-2', 'booking-skeleton-3']

/**
 * Список записей клиентов: две вкладки, строки, пагинация и все состояния.
 *
 * Счётчиков на вкладках нет: `nextCursor` не говорит, сколько всего записей. Пустые
 * состояния тоже не выдумывают итог — только то, что видно в этом скоупе.
 */
export function CabinetBookingList({
  scope,
  items,
  status,
  error,
  nextCursor,
  isLoadingMore,
  loadMoreError,
  now,
  hasPastItems,
  headingRef,
  onScopeChange,
  onRetry,
  onLoadMore,
  onReload,
  onConfirm,
  onComplete,
  onOpenCancel,
  onOpenNoShow,
  onOpenNote,
  registerRow,
}: {
  scope: BarberBookingScope
  items: BarberBooking[]
  status: BarberBookingsStatus
  error: string | null
  nextCursor: string | null
  isLoadingMore: boolean
  loadMoreError: string | null
  now: number
  /** История уже показала хотя бы одну строку — можно предложить её открыть. */
  hasPastItems: boolean
  headingRef: RefObject<HTMLHeadingElement | null>
  onScopeChange: (scope: BarberBookingScope) => void
  onRetry: () => void
  onLoadMore: () => void
  onReload: () => void
  onConfirm: (id: string) => Promise<void>
  onComplete: (id: string) => Promise<void>
  onOpenCancel: (booking: BarberBooking) => void
  onOpenNoShow: (booking: BarberBooking) => void
  onOpenNote: (booking: BarberBooking) => void
  registerRow: (id: string, handle: RowHandle | null) => void
}) {
  const tabRefs = useRef<Record<BarberBookingScope, HTMLButtonElement | null>>({
    upcoming: null,
    past: null,
  })
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

  function handleTabKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
      return
    }

    event.preventDefault()
    const next: BarberBookingScope = scope === 'upcoming' ? 'past' : 'upcoming'

    onScopeChange(next)
    tabRefs.current[next]?.focus()
  }

  const isLoading = status === 'loading' || status === 'idle'
  const isEmpty =
    status === 'ready' &&
    items.length === 0 &&
    !isLoadingMore &&
    loadMoreError === null
  const emptyTitle =
    scope === 'upcoming' ? 'Ближайших записей нет.' : 'История пуста.'

  const liveMessage = isLoading
    ? 'Загружаем записи…'
    : status === 'error'
      ? ''
      : isEmpty
        ? emptyTitle
        : `Загружено ${formatBookingsCount(items.length)}`

  return (
    <section aria-labelledby="barber-bookings-heading" className="space-y-3">
      <p aria-live="polite" className="sr-only">
        {liveMessage}
      </p>

      <h2
        id="barber-bookings-heading"
        ref={headingRef}
        tabIndex={-1}
        className="rounded-md font-heading text-lg font-semibold tracking-tight focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
      >
        {scope === 'upcoming' ? 'Ближайшие записи' : 'История записей'}
      </h2>

      <div
        role="tablist"
        aria-label="Какие записи показать"
        className="inline-flex items-center gap-0.5 rounded-lg bg-muted p-0.5"
      >
        {TABS.map((item) => {
          const isSelected = item.id === scope

          return (
            <button
              key={item.id}
              ref={(element) => {
                tabRefs.current[item.id] = element
              }}
              type="button"
              role="tab"
              id={`barber-tab-${item.id}`}
              aria-selected={isSelected}
              aria-controls="barber-bookings-panel"
              tabIndex={isSelected ? 0 : -1}
              onClick={() => onScopeChange(item.id)}
              onKeyDown={handleTabKeyDown}
              className={cn(
                'inline-flex h-11 items-center rounded-md px-3 text-sm transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-7',
                isSelected
                  ? 'bg-background font-medium text-foreground shadow-sm ring-1 ring-foreground/10'
                  : 'text-muted-foreground hover:text-foreground'
              )}
            >
              {item.label}
            </button>
          )
        })}
      </div>

      <div
        id="barber-bookings-panel"
        role="tabpanel"
        aria-labelledby={`barber-tab-${scope}`}
        aria-busy={isLoading}
      >
        {isLoading && <BookingsSkeleton />}

        {status === 'error' && (
          <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
            <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
              <TriangleAlert
                className="size-4 text-destructive"
                aria-hidden
              />
            </div>
            <div className="space-y-1">
              <p className="font-medium">Не удалось загрузить записи.</p>
              <p
                role="alert"
                className="mx-auto max-w-sm text-sm text-muted-foreground"
              >
                {error ?? 'Не удалось загрузить записи.'}
              </p>
            </div>
            <Button
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              onClick={onRetry}
            >
              Повторить
            </Button>
          </div>
        )}

        {isEmpty && (
          <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
            <div className="flex size-10 items-center justify-center rounded-full bg-muted">
              <CalendarX
                className="size-4 text-muted-foreground"
                aria-hidden
              />
            </div>
            <div className="space-y-1">
              <p className="font-medium">{emptyTitle}</p>
              {scope === 'upcoming' && (
                <p className="mx-auto max-w-sm text-sm text-muted-foreground">
                  {hasPastItems
                    ? 'Прошлые визиты остались в истории — их можно открыть в соседней вкладке.'
                    : 'Новые заявки появятся здесь, как только клиент запишется.'}
                </p>
              )}
            </div>
            {scope === 'upcoming' && hasPastItems && (
              <Button
                variant="outline"
                size="sm"
                className="h-11 sm:h-8"
                onClick={() => onScopeChange('past')}
              >
                Открыть историю
              </Button>
            )}
          </div>
        )}

        {status === 'ready' && items.length > 0 && (
          <>
            <ul className="divide-y rounded-xl border bg-card">
              {items.map((booking) => (
                <CabinetBookingRow
                  key={booking.id}
                  booking={booking}
                  scope={scope}
                  now={now}
                  onConfirm={onConfirm}
                  onComplete={onComplete}
                  onOpenCancel={onOpenCancel}
                  onOpenNoShow={onOpenNoShow}
                  onOpenNote={onOpenNote}
                  onReload={onReload}
                  registerRow={registerRow}
                />
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
      </div>
    </section>
  )
}

function BookingsSkeleton() {
  return (
    <ul aria-hidden="true" className="divide-y rounded-xl border bg-card">
      {SKELETON_KEYS.map((key) => (
        <li key={key} className="space-y-2 p-4">
          <Skeleton className="h-4 w-40" />
          <Skeleton className="h-4 w-56" />
          <Skeleton className="h-4 w-full max-w-sm" />
        </li>
      ))}
    </ul>
  )
}
