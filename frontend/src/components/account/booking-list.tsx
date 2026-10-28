import { useRef, type KeyboardEvent } from 'react'
import { Loader2, Scissors } from 'lucide-react'

import { BookingRow } from '@/components/account/booking-row'
import type { BookingView } from '@/components/account/booking-view'
import { Button } from '@/components/ui/button'
import { formatMonth } from '@/lib/format'
import { goToSection } from '@/lib/router'
import { cn } from '@/lib/utils'

export type BookingsTab = 'upcoming' | 'history'

const TABS = [
  { id: 'upcoming', label: 'Предстоящие' },
  { id: 'history', label: 'История' },
] as const

type MonthGroup = {
  key: string
  label: string
  items: BookingView[]
}

function groupByMonth(items: BookingView[]): MonthGroup[] {
  const groups = new Map<string, MonthGroup>()

  for (const view of items) {
    const label = formatMonth(view.booking.startAt, view.timeZoneId)
    const group = groups.get(label)

    if (group) {
      group.items.push(view)
    } else {
      groups.set(label, { key: label, label, items: [view] })
    }
  }

  return [...groups.values()]
}

/**
 * Список записей с переключателем «Предстоящие / История».
 *
 * Разделение клиентское: сервер отдаёт брони одним потоком по времени начала,
 * а кабинет показывает будущее отдельно от прошлого. Догрузка по курсору относится
 * к истории — предстоящие всегда попадают на первую страницу.
 */
export function BookingList({
  upcoming,
  history,
  tab,
  onTabChange,
  reviewedIds,
  nextCursor,
  isLoadingMore,
  loadMoreError,
  onLoadMore,
  onRequestCancel,
  onRequestReview,
}: {
  upcoming: BookingView[]
  history: BookingView[]
  tab: BookingsTab
  onTabChange: (tab: BookingsTab) => void
  reviewedIds: ReadonlySet<string>
  nextCursor: string | null
  isLoadingMore: boolean
  loadMoreError: string | null
  onLoadMore: () => void
  onRequestCancel: (view: BookingView) => void
  onRequestReview: (view: BookingView) => void
}) {
  const tabRefs = useRef<Record<BookingsTab, HTMLButtonElement | null>>({
    upcoming: null,
    history: null,
  })

  const items = tab === 'upcoming' ? upcoming : history
  const groups = groupByMonth(items)

  function handleTabKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
      return
    }

    event.preventDefault()
    const next: BookingsTab = tab === 'upcoming' ? 'history' : 'upcoming'
    onTabChange(next)
    tabRefs.current[next]?.focus()
  }

  return (
    <section className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div
          role="tablist"
          aria-label="Какие записи показать"
          className="inline-flex items-center gap-0.5 rounded-lg bg-muted p-0.5"
        >
          {TABS.map((item) => {
            const isSelected = item.id === tab
            const count = item.id === 'upcoming' ? upcoming.length : history.length

            return (
              <button
                key={item.id}
                ref={(element) => {
                  tabRefs.current[item.id] = element
                }}
                type="button"
                role="tab"
                id={`bookings-tab-${item.id}`}
                aria-selected={isSelected}
                aria-controls={isSelected ? 'bookings-panel' : undefined}
                tabIndex={isSelected ? 0 : -1}
                onClick={() => onTabChange(item.id)}
                onKeyDown={handleTabKeyDown}
                className={cn(
                  'inline-flex h-7 items-center gap-1.5 rounded-md px-2.5 text-sm transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50',
                  isSelected
                    ? 'bg-background font-medium text-foreground shadow-sm ring-1 ring-foreground/10'
                    : 'text-muted-foreground hover:text-foreground'
                )}
              >
                {item.label}
                <span className="text-xs tabular-nums text-muted-foreground">
                  {count}
                </span>
              </button>
            )
          })}
        </div>
      </div>

      <div
        role="tabpanel"
        id="bookings-panel"
        aria-labelledby={`bookings-tab-${tab}`}
        className="space-y-3"
      >
        {groups.length === 0 && !(tab === 'history' && nextCursor) ? (
          <TabEmptyState tab={tab} />
        ) : (
          groups.length > 0 && (
            <div className="overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10">
              {groups.map((group, index) => (
                <div key={group.key} className={index > 0 ? 'border-t' : undefined}>
                  <h3 className="border-b bg-muted/40 px-4 py-2 text-xs font-medium tracking-wide text-muted-foreground uppercase">
                    {group.label}
                  </h3>
                  <ul className="divide-y">
                    {group.items.map((view) => (
                      <BookingRow
                        key={view.booking.id}
                        view={view}
                        isReviewed={reviewedIds.has(view.booking.id)}
                        onRequestCancel={() => onRequestCancel(view)}
                        onRequestReview={() => onRequestReview(view)}
                      />
                    ))}
                  </ul>
                </div>
              ))}
            </div>
          )
        )}

        {tab === 'history' && nextCursor && (
          <div className="flex flex-col items-center gap-2 pt-1">
            {loadMoreError && (
              <p role="alert" className="text-sm text-destructive">
                {loadMoreError}
              </p>
            )}
            <p className="text-xs text-muted-foreground">
              {items.length === 0
                ? 'Ранние визиты ещё не загружены.'
                : `Показаны последние ${items.length} записей.`}
            </p>
            <Button
              variant="outline"
              size="sm"
              onClick={onLoadMore}
              disabled={isLoadingMore}
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
          </div>
        )}
      </div>
    </section>
  )
}

function TabEmptyState({ tab }: { tab: BookingsTab }) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <Scissors className="size-4 text-muted-foreground" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">
          {tab === 'upcoming' ? 'Предстоящих записей нет' : 'История пока пуста'}
        </p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          {tab === 'upcoming'
            ? 'Выберите барбера и свободное время — запись появится здесь.'
            : 'После визита здесь останется запись и можно будет оставить отзыв.'}
        </p>
      </div>
      {tab === 'upcoming' && (
        <Button size="sm" variant="outline" onClick={() => goToSection('barbers')}>
          Найти барбера
        </Button>
      )}
    </div>
  )
}
