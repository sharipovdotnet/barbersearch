import { Loader2 } from 'lucide-react'

import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import type { AdminBarber } from '@/lib/admin-barbers'
import { BARBER_GRADE_LABELS } from '@/lib/admin-barbers'
import { formatRating, formatReviewsCount, getInitials } from '@/lib/format'

/**
 * Список барберов: таблица на широком экране и карточки на телефоне.
 *
 * Счётчика «всего» нет: API отдаёт только страницу, поэтому в подписи «Показано N»,
 * а не выдуманное общее число.
 */
export function BarberList({
  items,
  nextCursor,
  isLoadingMore,
  loadMoreError,
  onLoadMore,
}: {
  items: AdminBarber[]
  nextCursor: string | null
  isLoadingMore: boolean
  loadMoreError: string | null
  onLoadMore: () => void
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
            {items.map((barber) => (
              <tr key={barber.id} className="border-b last:border-b-0">
                <td className="px-3 py-3 align-middle">
                  <BarberIdentity barber={barber} />
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
            ))}
          </tbody>
        </table>

        <ul className="divide-y lg:hidden">
          {items.map((barber) => (
            <li key={barber.id} className="space-y-2 p-3">
              <BarberIdentity barber={barber} />
              <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
                <span>{BARBER_GRADE_LABELS[barber.grade]}</span>
                <BarberRating barber={barber} />
              </div>
              <BarberStatusBadge barber={barber} />
            </li>
          ))}
        </ul>
      </div>

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
            className="h-11 w-full sm:h-7"
            disabled={isLoadingMore}
            onClick={onLoadMore}
          >
            {isLoadingMore ? (
              <>
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
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
export function BarberStatusBadge({ barber }: { barber: AdminBarber }) {
  if (!barber.isActive) {
    return <Badge variant="outline">Скрыт из поиска</Badge>
  }

  if (!barber.isVerified) {
    return <Badge variant="outline">На проверке</Badge>
  }

  return <Badge variant="secondary">Подтверждён</Badge>
}
