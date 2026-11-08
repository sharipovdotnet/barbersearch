import type { RefObject } from 'react'
import { CalendarX, TriangleAlert } from 'lucide-react'

import {
  CabinetBookingRow,
  type RowHandle,
} from '@/components/barber/cabinet-booking-row'
import { ListPager } from '@/components/list-pager'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import type { PagedListStatus } from '@/hooks/use-paged-list'
import { useI18n } from '@/i18n'
import type {
  BarberBooking,
  BarberBookingScope,
} from '@/lib/barber-bookings'
import { formatPageRange } from '@/lib/paging'

const SKELETON_KEYS = ['booking-skeleton-1', 'booking-skeleton-2', 'booking-skeleton-3']

/**
 * Список записей клиентов: строки, страницы и состояния одного скоупа.
 *
 * Вкладки живут на уровне страницы вместе с лентой дня: здесь только содержимое
 * панели, без второго `tablist` и без панели `role="tabpanel"`. Счётчиков на
 * вкладках нет: сервер не отдаёт общее число записей.
 */
export function CabinetBookingList({
  view,
  items,
  status,
  error,
  pageIndex,
  offset,
  hasPrev,
  hasNext,
  isLoadingPage,
  pageError,
  now,
  hasPastItems,
  headingRef,
  onRetry,
  onPrev,
  onNext,
  onReload,
  onOpenPast,
  onConfirm,
  onComplete,
  onOpenCancel,
  onOpenNoShow,
  onOpenNote,
  registerRow,
}: {
  view: BarberBookingScope
  items: BarberBooking[]
  status: PagedListStatus
  error: string | null
  pageIndex: number
  offset: number
  hasPrev: boolean
  hasNext: boolean
  isLoadingPage: boolean
  pageError: string | null
  now: number
  /** История уже отдала хотя бы одну строку — можно предложить её открыть. */
  hasPastItems: boolean
  headingRef: RefObject<HTMLHeadingElement | null>
  onRetry: () => void
  onPrev: () => void
  onNext: () => void
  onReload: () => void
  onOpenPast: () => void
  onConfirm: (id: string) => Promise<void>
  onComplete: (id: string) => Promise<void>
  onOpenCancel: (booking: BarberBooking) => void
  onOpenNoShow: (booking: BarberBooking) => void
  onOpenNote: (booking: BarberBooking) => void
  registerRow: (id: string, handle: RowHandle | null) => void
}) {
  const { t } = useI18n()
  const isLoading = status === 'loading' || status === 'idle'
  const isEmpty = status === 'ready' && items.length === 0
  const emptyTitle =
    view === 'upcoming'
      ? t('Ближайших записей нет.')
      : t('История пуста.')

  // Диапазон объявляет пагинатор: второй такой же озвучки на экране быть не должно.
  const liveMessage = isLoading
    ? t('Загружаем записи…')
    : isEmpty
      ? emptyTitle
      : ''

  return (
    <section aria-labelledby="barber-bookings-heading" className="space-y-3">
      <p aria-live="polite" className="sr-only">
        {liveMessage}
      </p>

      <h2
        id="barber-bookings-heading"
        ref={headingRef}
        tabIndex={-1}
        className="scroll-mt-20 rounded-md font-heading text-lg font-semibold tracking-tight focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
      >
        {view === 'upcoming' ? t('Ближайшие записи') : t('История записей')}
      </h2>

      {isLoading && <BookingsSkeleton />}

      {status === 'error' && (
        <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
          <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
            <TriangleAlert className="size-4 text-destructive" aria-hidden />
          </div>
          <div className="space-y-1">
            <p className="font-medium">{t('Не удалось загрузить записи.')}</p>
            <p
              role="alert"
              className="mx-auto max-w-sm text-sm text-muted-foreground"
            >
              {error ?? t('Не удалось загрузить записи.')}
            </p>
          </div>
          <Button
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            onClick={onRetry}
          >
            {t('Повторить')}
          </Button>
        </div>
      )}

      {isEmpty && (
        <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
          <div className="flex size-10 items-center justify-center rounded-full bg-muted">
            <CalendarX className="size-4 text-muted-foreground" aria-hidden />
          </div>
          <div className="space-y-1">
            <p className="font-medium">{emptyTitle}</p>
            {view === 'upcoming' && (
              <p className="mx-auto max-w-sm text-sm text-muted-foreground">
                {hasPastItems
                  ? t(
                      'Прошлые визиты остались в истории — их можно открыть в соседней вкладке.'
                    )
                  : t(
                      'Новые заявки появятся здесь, как только клиент запишется.'
                    )}
              </p>
            )}
          </div>
          {view === 'upcoming' && hasPastItems && (
            <Button
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              onClick={onOpenPast}
            >
              {t('Открыть историю')}
            </Button>
          )}
        </div>
      )}

      {status === 'ready' && items.length > 0 && (
        <div className="space-y-4">
          <ul className="divide-y rounded-xl border bg-card">
            {items.map((booking) => (
              <CabinetBookingRow
                key={booking.id}
                booking={booking}
                scope={view}
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

          <ListPager
            label={t('Страницы записей')}
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
        </div>
      )}
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
