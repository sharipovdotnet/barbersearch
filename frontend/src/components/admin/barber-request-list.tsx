import type { RefObject } from 'react'

import { ListPager } from '@/components/list-pager'
import { Badge } from '@/components/ui/badge'
import type {
  AdminBarberRequest,
  BarberRequestStatus,
} from '@/lib/admin-barber-requests'
import { ADMIN_BARBER_REQUEST_STATUS_LABELS } from '@/lib/admin-barber-requests'
import { barberRequestsPath, type AdminBarberRequestFilter } from '@/lib/admin-path'
import { formatDateTime } from '@/lib/format'
import { formatPageRange } from '@/lib/paging'
import { cn } from '@/lib/utils'

/**
 * Список заявок: одна строка — имя, город, дата подачи и статус.
 *
 * Счётчика «всего» нет: API отдаёт только страницу, поэтому в подписи «Показано N».
 * Ссылка в имени остаётся настоящей ссылкой — средний клик и «открыть в новой
 * вкладке» работают. Перелистывание — общий `ListPager`: диапазон без «из N».
 */
export function BarberRequestList({
  items,
  status,
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
  items: AdminBarberRequest[]
  /** Фильтр списка: уходит в ссылку строки, чтобы карточка открылась в своей выборке. */
  status: AdminBarberRequestFilter
  selectedId: string | null
  offset: number
  pageIndex: number
  hasPrev: boolean
  hasNext: boolean
  isLoadingPage: boolean
  pageError: string | null
  focusTargetRef: RefObject<HTMLElement | null>
  onSelect: (requestId: string) => void
  onPrev: () => void
  onNext: () => void
}) {
  return (
    <section aria-label="Заявки" className="space-y-3">
      <div className="flex items-center justify-between gap-2 px-1 text-xs text-muted-foreground">
        <span>Показано {items.length}</span>
        {/* Очередь сервер отдаёт старыми сверху, все заявки — новыми. */}
        <span>{status === 'pending' ? 'Сначала старые' : 'Сначала новые'}</span>
      </div>

      <ul aria-label="Список заявок" className="divide-y rounded-xl bg-card ring-1 ring-foreground/10">
        {items.map((request) => {
          const isSelected = request.id === selectedId

          return (
            <li key={request.id}>
              <a
                href={barberRequestsPath(request.id, status)}
                aria-current={isSelected ? 'true' : undefined}
                className={cn(
                  'block space-y-2 p-3 transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 lg:grid lg:grid-cols-[minmax(0,1fr)_8rem_10rem_8rem] lg:items-center lg:gap-3 lg:space-y-0',
                  isSelected ? 'bg-muted/60' : 'hover:bg-muted/30'
                )}
                onClick={(event) => {
                  event.preventDefault()
                  onSelect(request.id)
                }}
              >
                <span className="block min-w-0">
                  <span className="block truncate font-medium">
                    {request.displayName}
                  </span>
                  <span className="block truncate text-xs text-muted-foreground">
                    {request.applicant.fullName}
                  </span>
                </span>

                <span className="block text-sm break-words text-muted-foreground">
                  {request.city}
                </span>

                <span className="block text-xs text-muted-foreground tabular-nums">
                  <time dateTime={request.createdAt}>
                    {formatDateTime(request.createdAt)}
                  </time>
                </span>

                <BarberRequestStatusBadge status={request.status} />
              </a>
            </li>
          )
        })}
      </ul>

      <ListPager
        label="Страницы заявок"
        rangeLabel={items.length === 0 ? '' : formatPageRange(offset, items.length)}
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

/**
 * Статус заявки. Цвет не единственный сигнал: у каждого состояния свой текст,
 * а «Отклонена» ещё и объясняет, что причина видна заявителю.
 */
export function BarberRequestStatusBadge({
  status,
  className,
}: {
  status: BarberRequestStatus
  className?: string
}) {
  const variant =
    status === 'Approved'
      ? 'secondary'
      : status === 'Rejected'
        ? 'destructive'
        : 'outline'

  return (
    <Badge variant={variant} className={className}>
      {ADMIN_BARBER_REQUEST_STATUS_LABELS[status]}
    </Badge>
  )
}
