import type { RefObject } from 'react'

import { ListPager } from '@/components/list-pager'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import type { AdminBarber } from '@/lib/admin-barbers'
import { BARBER_GRADE_LABELS } from '@/lib/admin-barbers'
import { barbersPath } from '@/lib/admin-path'
import { formatRating, formatReviewsCount, getInitials } from '@/lib/format'
import { formatPageRange } from '@/lib/paging'
import { cn } from '@/lib/utils'

/**
 * Список барберов: таблица на широком экране и карточки на телефоне.
 *
 * Счётчика «всего» нет: API отдаёт только страницу, поэтому в подписи «Показано N»,
 * а не выдуманное общее число. Выбранная строка подсвечивается, а ссылка в имени
 * остаётся настоящей ссылкой: средний клик и «открыть в новой вкладке» работают.
 * Перелистывание — общий `ListPager`: диапазон без выдуманного «из N».
 */
export function BarberList({
  items,
  selectedId,
  offset,
  pageIndex,
  hasPrev,
  hasNext,
  isLoadingPage,
  pageError,
  focusTargetRef,
  onSelect,
  onPrev,
  onNext,
}: {
  items: AdminBarber[]
  selectedId: string | null
  offset: number
  pageIndex: number
  hasPrev: boolean
  hasNext: boolean
  isLoadingPage: boolean
  pageError: string | null
  focusTargetRef: RefObject<HTMLElement | null>
  onSelect: (barberId: string) => void
  onPrev: () => void
  onNext: () => void
}) {
  return (
    <section aria-label="Барберы" className="space-y-3">
      <div className="flex items-center justify-between gap-2 px-1 text-xs text-muted-foreground">
        <span>Показано {items.length}</span>
        <span>Сначала новые</span>
      </div>

      <div className="overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10">
        <table className="hidden w-full text-left text-sm lg:table">
          <caption className="sr-only">
            Барберы: имя, email, грейд, рейтинг и статус профиля
          </caption>
          <thead className="border-b bg-muted/40 text-xs text-muted-foreground">
            <tr>
              <th scope="col" className="px-3 py-2 font-medium">
                Барбер
              </th>
              <th scope="col" className="px-3 py-2 font-medium">
                Грейд
              </th>
              <th scope="col" className="px-3 py-2 font-medium">
                Рейтинг
              </th>
              <th scope="col" className="px-3 py-2 font-medium">
                Статус
              </th>
            </tr>
          </thead>
          <tbody>
            {items.map((barber) => {
              const isSelected = barber.id === selectedId

              return (
                <tr
                  key={barber.id}
                  data-selected={isSelected ? 'true' : undefined}
                  className={cn(
                    'cursor-pointer border-b transition-colors last:border-b-0',
                    isSelected ? 'bg-muted/60' : 'hover:bg-muted/30'
                  )}
                  onClick={() => onSelect(barber.id)}
                >
                  <td className="px-3 py-3 align-middle">
                    <a
                      href={barbersPath(barber.id)}
                      aria-current={isSelected ? 'true' : undefined}
                      className="block rounded-md outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
                      onClick={(event) => {
                        event.preventDefault()
                        onSelect(barber.id)
                      }}
                    >
                      <BarberIdentity barber={barber} />
                    </a>
                  </td>
                  <td className="px-3 py-3 align-middle whitespace-nowrap">
                    {BARBER_GRADE_LABELS[barber.grade]}
                  </td>
                  <td className="px-3 py-3 align-middle whitespace-nowrap">
                    <BarberRating barber={barber} />
                  </td>
                  <td className="px-3 py-3 align-middle">
                    <BarberStatusBadge barber={barber} />
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>

        <ul className="divide-y lg:hidden">
          {items.map((barber) => (
            <li key={barber.id}>
              <a
                href={barbersPath(barber.id)}
                aria-current={barber.id === selectedId ? 'true' : undefined}
                className="block space-y-2 p-3 transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 hover:bg-muted/30"
                onClick={(event) => {
                  event.preventDefault()
                  onSelect(barber.id)
                }}
              >
                <BarberIdentity barber={barber} />
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
                  <span>{BARBER_GRADE_LABELS[barber.grade]}</span>
                  <BarberRating barber={barber} />
                </div>
                <BarberStatusBadge barber={barber} />
              </a>
            </li>
          ))}
        </ul>
      </div>

      <ListPager
        label="Страницы барберов"
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
        focusTargetRef={focusTargetRef}
      />
    </section>
  )
}

/** Аватар с инициалами, имя и email: одна и та же строка в таблице и карточке. */
export function BarberIdentity({ barber }: { barber: AdminBarber }) {
  return (
    <div className="flex min-w-0 items-center gap-2.5">
      <Avatar size="sm">
        <AvatarFallback className="text-xs">
          {getInitials(barber.displayName)}
        </AvatarFallback>
      </Avatar>
      <span className="min-w-0">
        <span className="block truncate font-medium">{barber.displayName}</span>
        <span className="block truncate text-xs text-muted-foreground">
          {barber.email}
        </span>
      </span>
    </div>
  )
}

function BarberRating({ barber }: { barber: AdminBarber }) {
  return (
    <span className="text-muted-foreground tabular-nums">
      {barber.rating === null ? (
        'Нет отзывов'
      ) : (
        <>
          {formatRating(barber.rating)} ·{' '}
          {formatReviewsCount(barber.reviewsCount)}
        </>
      )}
    </span>
  )
}

/**
 * Бейдж статуса. Цвет не единственный сигнал: у каждого состояния свой текст,
 * а «Скрыт из поиска» ещё и объясняет последствие.
 */
export function BarberStatusBadge({
  barber,
  className,
}: {
  barber: { isActive: boolean; isVerified: boolean }
  className?: string
}) {
  if (!barber.isActive) {
    return (
      <Badge variant="outline" className={className}>
        Скрыт из поиска
      </Badge>
    )
  }

  if (!barber.isVerified) {
    return (
      <Badge variant="outline" className={className}>
        На проверке
      </Badge>
    )
  }

  return (
    <Badge variant="secondary" className={className}>
      Подтверждён
    </Badge>
  )
}
