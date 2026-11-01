import { useRef, type KeyboardEvent, type RefObject } from 'react'
import { Scissors } from 'lucide-react'

import { BookingRow } from '@/components/account/booking-row'
import type { BookingView } from '@/components/account/booking-view'
import { ListPager } from '@/components/list-pager'
import { Button } from '@/components/ui/button'
import { formatMonth } from '@/lib/format'
import { goToSection } from '@/lib/router'
import { cn } from '@/lib/utils'

export type BookingsTab = 'upcoming' | 'history'

const TABS = [
  { id: 'upcoming', label: 'Предстоящие' },
  { id: 'history', label: 'История' },
] as const

/** Подпись навигации пагинатора: у вкладок разные списки и разные курсоры. */
const PAGER_LABELS: Record<BookingsTab, string> = {
  upcoming: 'Страницы предстоящих записей',
  history: 'Страницы истории',
}

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
 * Вкладки — два независимых серверных списка, поэтому пагинатор живёт внутри
 * `tabpanel` и листает только свою выборку. Группировка по месяцам считается по
 * странице: месяц может разорваться между страницами — склеивать их на клиенте
 * значило бы грузить строки, которых на странице нет.
 *
 * Счётчиков на вкладках нет: общего числа сервер не отдаёт, а длина страницы не
 * равна числу записей. Подпись диапазона живёт в пагинаторе.
 */
export function BookingList({
  items,
  tab,
  onTabChange,
  reviewedIds,
  rangeLabel,
  hasPrev,
  hasNext,
  isLoadingPage,
  pageError,
  onPrev,
  onNext,
  pageIndex,
  focusTargetRef,
  onRequestCancel,
  onRequestReview,
}: {
  items: BookingView[]
  tab: BookingsTab
  onTabChange: (tab: BookingsTab) => void
  reviewedIds: ReadonlySet<string>
  rangeLabel: string
  hasPrev: boolean
  hasNext: boolean
  isLoadingPage: boolean
  pageError: string | null
  onPrev: () => void
  onNext: () => void
  pageIndex: number
  /** Цель фокуса и прокрутки после смены страницы — сама панель вкладки. */
  focusTargetRef: RefObject<HTMLDivElement | null>
  onRequestCancel: (view: BookingView) => void
  onRequestReview: (view: BookingView) => void
}) {
  const tabRefs = useRef<Record<BookingsTab, HTMLButtonElement | null>>({
    upcoming: null,
    history: null,
  })

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
                aria-controls="bookings-panel"
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
              </button>
            )
          })}
        </div>
      </div>

      <div
        role="tabpanel"
        id="bookings-panel"
        ref={focusTargetRef}
        tabIndex={-1}
        aria-labelledby={`bookings-tab-${tab}`}
        aria-busy={isLoadingPage}
        className="scroll-mt-20 space-y-3 rounded-md focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
      >
        {groups.length === 0 ? (
          <TabEmptyState tab={tab} />
        ) : (
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
        )}

        <ListPager
          label={PAGER_LABELS[tab]}
          rangeLabel={rangeLabel}
          hasPrev={hasPrev}
          hasNext={hasNext}
          isLoading={isLoadingPage}
          error={pageError}
          onPrev={onPrev}
          onNext={onNext}
          pageIndex={pageIndex}
          focusTargetRef={focusTargetRef}
        />
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
