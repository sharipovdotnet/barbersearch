import { useEffect, useRef, useState } from 'react'
import {
  Inbox,
  Loader2,
  RefreshCw,
  Search,
  TriangleAlert,
  X,
} from 'lucide-react'

import { AdminSignInGate } from '@/components/admin/admin-gate'
import {
  BookingDetail,
  BookingDetailPlaceholder,
  BookingDetailState,
} from '@/components/admin/booking-detail'
import { BookingStatusBadge } from '@/components/booking-status-badge'
import { ListPager } from '@/components/list-pager'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { useAdminBooking } from '@/hooks/use-admin-booking'
import { useAdminBookingsList } from '@/hooks/use-admin-bookings-list'
import { useMediaQuery } from '@/hooks/use-media-query'
import {
  ADMIN_BOOKING_STATUS_FILTERS,
  ADMIN_BOOKING_STATUS_FILTER_LABELS,
  ADMIN_BOOKINGS_SEARCH_MAX_LENGTH,
  formatAdminBookingTime,
  getAdminBookingCustomerName,
  type AdminBooking,
  type AdminBookingStatus,
  type AdminBookingStatusFilter,
} from '@/lib/admin-bookings'
import { bookingsPath } from '@/lib/admin-path'
import { bookingStatusLabel, bookingStatusStyle } from '@/lib/booking-status'
import { formatClockTime, formatPrice } from '@/lib/format'
import { formatPageRange } from '@/lib/paging'
import { navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * `/admin/bookings` — рабочий стол поддержки: список всех броней и карточка.
 *
 * Поиск (email клиента или точный id брони) и фильтр статуса живут в состоянии
 * React и не попадают в URL: email — PII, ему не место в истории браузера и логах
 * прокси. В адресе остаётся только id брони. На широком экране список и карточка
 * стоят рядом, на телефоне карточка заменяет список целиком.
 *
 * Счётчиков «всего» нет: API отдаёт курсорную страницу без общего числа.
 * Действий у строк нет — открыть карточку можно только ссылкой; отмена и неявка
 * живут в карточке и подтверждаются диалогом.
 */
export function BookingsScreen({ bookingId }: { bookingId: string | null }) {
  const [statusFilter, setStatusFilter] =
    useState<AdminBookingStatusFilter>('all')
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [requiresSignIn, setRequiresSignIn] = useState(false)

  // Задержка перед запросом: 300 мс хватает, чтобы не искать на каждую букву.
  useEffect(() => {
    const trimmed = searchInput.trim()

    if (trimmed === search) {
      return
    }

    const timer = window.setTimeout(() => setSearch(trimmed), 300)

    return () => window.clearTimeout(timer)
  }, [searchInput, search])

  const list = useAdminBookingsList({ status: statusFilter, search })
  const detail = useAdminBooking(bookingId)
  const isWide = useMediaQuery('(min-width: 1024px)')
  const headingRef = useRef<HTMLHeadingElement | null>(null)
  const detailHeadingRef = useRef<HTMLHeadingElement | null>(null)

  // На телефоне карточка заменяет список — переход должен начинаться сверху.
  useEffect(() => {
    if (!bookingId || isWide) {
      return
    }

    window.scrollTo({ top: 0 })
  }, [bookingId, isWide])

  const isFiltered = statusFilter !== 'all' || search !== ''
  const isFirstLoad = list.status === 'loading'
  const isEmpty = list.status === 'ready' && list.items.length === 0
  const isMobileDetail = !isWide && bookingId !== null
  const showGrid =
    list.status === 'ready' && (list.items.length > 0 || bookingId !== null)

  function resetFilters() {
    setStatusFilter('all')
    setSearchInput('')
    setSearch('')
  }

  function handleSelect(id: string) {
    navigate(bookingsPath(id))
  }

  function handleBack() {
    navigate(bookingsPath())

    // Фокус возвращается на заголовок, а не в <body>: на телефоне списка ещё нет,
    // пока страница не перерисовалась.
    window.requestAnimationFrame(() => {
      window.requestAnimationFrame(() => headingRef.current?.focus())
    })
  }

  function handleActionReload() {
    detail.reload()
    list.refresh()
  }

  if (list.requiresSignIn || requiresSignIn) {
    return (
      <div className="mx-auto w-full max-w-md py-6">
        <AdminSignInGate
          returnTo={bookingsPath(bookingId)}
          onSignedIn={() => {
            setRequiresSignIn(false)
            list.reload()
            detail.reload()
          }}
        />
      </div>
    )
  }

  return (
    <div className="space-y-5">
      {isMobileDetail && (
        <h1 ref={headingRef} tabIndex={-1} className="sr-only">
          Брони
        </h1>
      )}

      {!isMobileDetail && (
        <header className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h1
              ref={headingRef}
              tabIndex={-1}
              className="scroll-mt-20 font-heading text-2xl font-semibold tracking-tight sm:text-3xl"
            >
              Брони
            </h1>
            <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
              Все записи клиентов. Время показано в поясе салона.
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {list.updatedAt !== null && (
              <span className="text-xs text-muted-foreground tabular-nums">
                Обновлено в {formatClockTime(new Date(list.updatedAt))}
              </span>
            )}
            <Button
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              disabled={
                list.isRefreshing ||
                list.isLoadingPage ||
                list.status === 'loading'
              }
              onClick={list.refresh}
            >
              {list.isRefreshing ? (
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
              ) : (
                <RefreshCw data-icon="inline-start" aria-hidden />
              )}
              {list.isRefreshing ? 'Обновляем…' : 'Обновить'}
            </Button>
          </div>
        </header>
      )}

      {!isMobileDetail && (
        <div className="space-y-3 rounded-xl border p-3 sm:p-4">
          <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_12rem]">
            <div className="flex min-w-0 flex-col justify-end gap-1">
              <label htmlFor="bookings-search" className="text-sm font-medium">
                Клиент или номер брони
              </label>
              <div className="relative">
                <Search
                  className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
                  aria-hidden
                />
                <Input
                  id="bookings-search"
                  type="search"
                  value={searchInput}
                  maxLength={ADMIN_BOOKINGS_SEARCH_MAX_LENGTH}
                  placeholder="email клиента или id брони"
                  className="h-11 pr-11 pl-8"
                  onChange={(event) => setSearchInput(event.target.value)}
                />
                {searchInput !== '' && (
                  <button
                    type="button"
                    aria-label="Очистить поиск"
                    className="absolute top-1/2 right-1 inline-flex size-11 -translate-y-1/2 items-center justify-center rounded-md text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:right-1.5 sm:size-auto sm:p-1"
                    onClick={() => {
                      setSearchInput('')
                      setSearch('')
                    }}
                  >
                    <X className="size-3.5" aria-hidden />
                  </button>
                )}
              </div>
            </div>

            <div className="flex min-w-0 flex-col justify-end gap-1">
              <label htmlFor="bookings-status" className="text-sm font-medium">
                Статус
              </label>
              <select
                id="bookings-status"
                value={statusFilter}
                onChange={(event) => {
                  setStatusFilter(
                    event.target.value as AdminBookingStatusFilter
                  )
                }}
                className="h-11 w-full rounded-lg border border-input bg-transparent px-2.5 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50"
              >
                {ADMIN_BOOKING_STATUS_FILTERS.map((value) => (
                  <option key={value} value={value}>
                    {ADMIN_BOOKING_STATUS_FILTER_LABELS[value]}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="text-xs text-muted-foreground">
              Поиск по email клиента или точному номеру брони. Фильтры не
              попадают в адрес страницы.
            </p>
            {isFiltered && (
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-11 sm:h-8"
                onClick={resetFilters}
              >
                Сбросить фильтры
              </Button>
            )}
          </div>
        </div>
      )}

      {list.refreshError && (
        <p role="alert" className="text-sm text-destructive">
          {list.refreshError}
        </p>
      )}

      <div aria-busy={isFirstLoad || list.isLoadingPage}>
        {isFirstLoad && <BookingsSkeleton />}

        {list.status === 'error' && (
          <BookingsError
            message={list.error ?? 'Не удалось загрузить брони.'}
            onRetry={list.reload}
          />
        )}

        {showGrid && (
          <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_20rem] lg:items-start">
            <div className={cn('min-w-0', bookingId ? 'hidden lg:block' : 'block')}>
              {list.items.length > 0 ? (
                <div className="space-y-3">
                  {isWide ? (
                    <BookingsTable
                      items={list.items}
                      selectedId={bookingId}
                      onSelect={handleSelect}
                    />
                  ) : (
                    <BookingCards
                      items={list.items}
                      selectedId={bookingId}
                      onSelect={handleSelect}
                    />
                  )}

                  <ListPager
                    label="Страницы броней"
                    rangeLabel={formatPageRange(list.offset, list.items.length)}
                    hasPrev={list.hasPrev}
                    hasNext={list.hasNext}
                    isLoading={list.isLoadingPage}
                    error={list.pageError}
                    onPrev={list.goPrev}
                    onNext={list.goNext}
                    pageIndex={list.pageIndex}
                    focusTargetRef={headingRef}
                  />
                </div>
              ) : (
                <BookingsEmpty isFiltered={isFiltered} />
              )}
            </div>

            <div className={cn('min-w-0', bookingId ? 'block' : 'hidden lg:block')}>
              {detail.booking ? (
                <BookingDetail
                  booking={detail.booking}
                  headingRef={detailHeadingRef}
                  onReload={handleActionReload}
                  onBack={handleBack}
                  onRequiresSignIn={() => setRequiresSignIn(true)}
                />
              ) : bookingId ? (
                <BookingDetailState
                  status={detail.status}
                  error={detail.error}
                  onRetry={detail.reload}
                  onBack={handleBack}
                />
              ) : (
                <BookingDetailPlaceholder />
              )}
            </div>
          </div>
        )}

        {isEmpty && bookingId === null && (
          <BookingsEmpty isFiltered={isFiltered} />
        )}
      </div>
    </div>
  )
}

/**
 * Таблица для `lg`: время — ссылка на карточку, статус словом.
 *
 * `table-fixed` и переносы вместо горизонтального скролла: в split-view колонка
 * списка узкая, и длинный email не должен растягивать страницу.
 */
function BookingsTable({
  items,
  selectedId,
  onSelect,
}: {
  items: AdminBooking[]
  selectedId: string | null
  onSelect: (id: string) => void
}) {
  return (
    <table className="w-full table-fixed text-sm">
      <caption className="sr-only">Список броней</caption>
      <thead>
        <tr className="text-left">
          {TABLE_COLUMNS.map((column) => (
            <th
              key={column}
              scope="col"
              className="border-b px-2 py-2 text-xs font-medium tracking-wide text-muted-foreground uppercase"
            >
              {column}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {items.map((booking) => {
          const isSelected = booking.id === selectedId
          const customerName = getAdminBookingCustomerName(booking.customer)

          return (
            <tr
              key={booking.id}
              className={cn('align-top', isSelected && 'bg-muted/50')}
            >
              <td className="border-b px-2 py-2">
                <a
                  href={bookingsPath(booking.id)}
                  aria-current={isSelected ? 'page' : undefined}
                  className="inline-block rounded-md py-1.5 font-medium break-words underline-offset-4 hover:underline focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
                  onClick={(event) => {
                    event.preventDefault()
                    onSelect(booking.id)
                  }}
                >
                  <time dateTime={booking.startAt}>
                    {formatAdminBookingTime(booking)}
                  </time>
                  <span className="sr-only"> — {customerName}</span>
                </a>
              </td>
              <td className="border-b px-2 py-2">
                <span className="block font-medium break-words">
                  {customerName}
                </span>
                <span
                  className="block truncate text-xs text-muted-foreground"
                  title={booking.customer.email}
                >
                  {booking.customer.email}
                </span>
              </td>
              <td className="border-b px-2 py-2 break-words">
                {booking.barber.displayName}
              </td>
              <td className="border-b px-2 py-2 break-words">
                {booking.service.name}
              </td>
              <td className="border-b px-2 py-2">
                <span className="break-words">
                  <BookingStatusText status={booking.status} />
                </span>
              </td>
              <td className="border-b px-2 py-2 tabular-nums break-words">
                {formatPrice(booking.price)}
              </td>
            </tr>
          )
        })}
      </tbody>
    </table>
  )
}

/**
 * Карточки для узкого экрана: те же поля, что в таблице, без горизонтальной
 * прокрутки. Длинный email и id ломаются по символам.
 */
function BookingCards({
  items,
  selectedId,
  onSelect,
}: {
  items: AdminBooking[]
  selectedId: string | null
  onSelect: (id: string) => void
}) {
  return (
    <ul aria-label="Список броней" className="space-y-3">
      {items.map((booking) => {
        const isSelected = booking.id === selectedId
        const customerName = getAdminBookingCustomerName(booking.customer)

        return (
          <li key={booking.id}>
            <div
              className={cn(
                'rounded-xl border p-4',
                isSelected && 'bg-muted/50 ring-1 ring-foreground/10'
              )}
            >
              <div className="flex flex-wrap items-start justify-between gap-2">
                <a
                  href={bookingsPath(booking.id)}
                  aria-current={isSelected ? 'page' : undefined}
                  className="inline-flex min-h-11 items-center rounded-md font-medium break-words underline-offset-4 hover:underline focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
                  onClick={(event) => {
                    event.preventDefault()
                    onSelect(booking.id)
                  }}
                >
                  <time dateTime={booking.startAt}>
                    {formatAdminBookingTime(booking)}
                  </time>
                  <span className="sr-only"> — {customerName}</span>
                </a>
                <BookingStatusBadge status={booking.status} />
              </div>

              <dl className="mt-3 grid gap-2 text-sm">
                <div className="min-w-0">
                  <dt className="text-xs text-muted-foreground">Клиент</dt>
                  <dd>
                    <span className="block break-words font-medium">
                      {customerName}
                    </span>
                    <span className="block text-xs break-all text-muted-foreground">
                      {booking.customer.email}
                    </span>
                  </dd>
                </div>
                <div className="min-w-0">
                  <dt className="text-xs text-muted-foreground">Барбер</dt>
                  <dd className="break-words">{booking.barber.displayName}</dd>
                </div>
                <div className="min-w-0">
                  <dt className="text-xs text-muted-foreground">Услуга</dt>
                  <dd className="break-words">{booking.service.name}</dd>
                </div>
                <div className="min-w-0">
                  <dt className="text-xs text-muted-foreground">Цена</dt>
                  <dd className="tabular-nums">{formatPrice(booking.price)}</dd>
                </div>
              </dl>
            </div>
          </li>
        )
      })}
    </ul>
  )
}

/** Статус строки словом: цвет — только поддержка, поэтому перенос разрешён. */
function BookingStatusText({ status }: { status: AdminBookingStatus }) {
  return (
    <span
      className={cn(
        'inline-flex max-w-full items-center gap-1.5 rounded-full border px-2 py-0.5 text-xs font-medium break-words whitespace-normal',
        bookingStatusStyle(status)
      )}
    >
      <span aria-hidden className="size-1.5 shrink-0 rounded-full bg-current" />
      {bookingStatusLabel(status)}
    </span>
  )
}

const TABLE_COLUMNS = [
  'Время',
  'Клиент',
  'Барбер',
  'Услуга',
  'Статус',
  'Цена',
] as const

function BookingsSkeleton() {
  return (
    <div className="space-y-3" aria-busy="true">
      <p role="status" className="sr-only">
        Загружаем брони…
      </p>
      <Skeleton className="h-8 w-full" aria-hidden />
      <Skeleton className="h-14 w-full" aria-hidden />
      <Skeleton className="h-14 w-full" aria-hidden />
      <Skeleton className="h-14 w-full" aria-hidden />
    </div>
  )
}

function BookingsError({
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
        <p className="font-medium">Не удалось загрузить брони</p>
        <p
          role="alert"
          className="mx-auto max-w-sm text-sm text-muted-foreground"
        >
          {message}
        </p>
      </div>
      <Button size="sm" variant="outline" className="h-11 sm:h-8" onClick={onRetry}>
        Повторить
      </Button>
    </div>
  )
}

/**
 * Пустой список: без фильтров — «Броней нет», с фильтрами — «Ничего не найдено».
 *
 * Счётчика «всего» нет: сервер отдаёт страницу без общего числа, а выдуманное
 * число в пустом состоянии только путало бы.
 */
function BookingsEmpty({ isFiltered }: { isFiltered: boolean }) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        {isFiltered ? (
          <Search className="size-4 text-muted-foreground" aria-hidden />
        ) : (
          <Inbox className="size-4 text-muted-foreground" aria-hidden />
        )}
      </div>
      <div className="space-y-1">
        <p className="font-medium">
          {isFiltered ? 'Ничего не найдено' : 'Броней нет'}
        </p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          {isFiltered
            ? 'Под выбранные фильтры записей нет. Измените поиск или статус.'
            : 'Здесь появятся записи клиентов. Новые брони придут автоматически.'}
        </p>
      </div>
    </div>
  )
}
