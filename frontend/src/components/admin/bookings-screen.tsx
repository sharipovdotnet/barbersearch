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
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { useAdminBooking } from '@/hooks/use-admin-booking'
import { useAdminBookingsList } from '@/hooks/use-admin-bookings-list'
import { useAutoRefresh } from '@/hooks/use-auto-refresh'
import { useElementWidth } from '@/hooks/use-element-width'
import { useMediaQuery } from '@/hooks/use-media-query'
import { useI18n } from '@/i18n'
import {
  ADMIN_BOOKING_STATUS_FILTERS,
  ADMIN_BOOKING_STATUS_FILTER_LABELS,
  ADMIN_BOOKINGS_SEARCH_MAX_LENGTH,
  formatAdminBookingTime,
  getAdminBookingCustomerContact,
  getAdminBookingCustomerName,
  type AdminBooking,
  type AdminBookingStatus,
  type AdminBookingStatusFilter,
} from '@/lib/admin-bookings'
import { bookingsPath } from '@/lib/admin-path'
import { bookingStatusLabel, bookingStatusStyle } from '@/lib/booking-status'
import { formatClockTime, formatPrice, getInitials } from '@/lib/format'
import { formatPageRange } from '@/lib/paging'
import { navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * Минимальная ширина колонки списка для таблицы.
 *
 * Сумма ширин её колонок — 596 px. В узкой колонке split-view шесть колонок не
 * помещаются: слова ломаются посередине, а плашка статуса наезжает на цену,
 * поэтому ниже порога список показывает карточки вместо таблицы. Клиент и
 * барбер шире остальных на 28 px: перед именем стоит аватар 24 px с отступом.
 */
const BOOKINGS_TABLE_MIN_WIDTH = 596

/**
 * `/admin/bookings` — рабочий стол поддержки: список всех броней и карточка.
 *
 * Поиск (email клиента или точный id брони) и фильтр статуса живут в состоянии
 * React и не попадают в URL: email — PII, ему не место в истории браузера и логах
 * прокси. В адресе остаётся только id брони. На широком экране список и карточка
 * стоят рядом, на телефоне карточка заменяет список целиком. Таблица показывается
 * только там, где колонка списка достаточно широка (`BOOKINGS_TABLE_MIN_WIDTH`),
 * иначе список идёт карточками — они переживают любую ширину.
 *
 * Счётчиков «всего» нет: API отдаёт курсорную страницу без общего числа.
 * Действий у строк нет — открыть карточку можно только ссылкой; отмена и неявка
 * живут в карточке и подтверждаются диалогом.
 */
export function BookingsScreen({ bookingId }: { bookingId: string | null }) {
  const { t } = useI18n()
  const [statusFilter, setStatusFilter] =
    useState<AdminBookingStatusFilter>('all')
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [requiresSignIn, setRequiresSignIn] = useState(false)
  const [isActing, setIsActing] = useState(false)

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

  // Новые и отменённые клиентами брони должны появляться без ручного обновления.
  useAutoRefresh({
    enabled:
      list.status === 'ready' &&
      !list.requiresSignIn &&
      !list.isLoadingPage &&
      !requiresSignIn &&
      !isActing,
    onRefresh: () => {
      list.refresh()

      if (bookingId !== null && detail.status !== 'notFound') {
        detail.reload()
      }
    },
  })

  const isWide = useMediaQuery('(min-width: 1024px)')
  // Таблица живёт по ширине колонки, а не окна: в split-view ей достаётся
  // разное место в зависимости от рейла, карточки справа и скроллбара.
  const { ref: listRef, width: listWidth } = useElementWidth<HTMLDivElement>()
  const showTable =
    listWidth !== null && listWidth >= BOOKINGS_TABLE_MIN_WIDTH
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
          {t('Брони')}
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
              {t('Брони')}
            </h1>
            <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
              {t('Все записи клиентов. Время показано в поясе салона.')}
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {list.updatedAt !== null && (
              <span className="text-xs text-muted-foreground tabular-nums">
                {t('Обновлено в {time}', {
                  time: formatClockTime(new Date(list.updatedAt)),
                })}
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
              {list.isRefreshing ? t('Обновляем…') : t('Обновить')}
            </Button>
          </div>
        </header>
      )}

      {!isMobileDetail && (
        <div className="space-y-3 rounded-xl border p-3 sm:p-4">
          <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_12rem]">
            <div className="flex min-w-0 flex-col justify-end gap-1">
              <label htmlFor="bookings-search" className="text-sm font-medium">
                {t('Клиент или номер брони')}
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
                  placeholder={t('email клиента или id брони')}
                  className="h-11 pr-11 pl-8"
                  onChange={(event) => setSearchInput(event.target.value)}
                />
                {searchInput !== '' && (
                  <button
                    type="button"
                    aria-label={t('Очистить поиск')}
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
                {t('Статус')}
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
                    {t(ADMIN_BOOKING_STATUS_FILTER_LABELS[value])}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="text-xs text-muted-foreground">
              {t(
                'Поиск по email клиента или точному номеру брони. Фильтры не попадают в адрес страницы.'
              )}
            </p>
            {isFiltered && (
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-11 sm:h-8"
                onClick={resetFilters}
              >
                {t('Сбросить фильтры')}
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
            message={list.error ?? t('Не удалось загрузить брони.')}
            onRetry={list.reload}
          />
        )}

        {showGrid && (
          <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_20rem] lg:items-start">
            <div
              ref={listRef}
              className={cn('min-w-0', bookingId ? 'hidden lg:block' : 'block')}
            >
              {list.items.length > 0 ? (
                <div className="space-y-3">
                  {showTable ? (
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
                    label={t('Страницы броней')}
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
                  onActingChange={setIsActing}
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
 * Таблица для широкой колонки списка: время — ссылка на карточку, статус словом.
 *
 * `table-fixed` и переносы вместо горизонтального скролла: в split-view колонка
 * списка узкая, и длинный email не должен растягивать страницу. Ширины колонок
 * заданы явно: поровну делить нельзя — плашка статуса не помещается в 90 px и
 * вылезает на цену. Время, статус и цена получают ровно нужное, остальное делят
 * клиент, барбер и услуга; цена прижата вправо и не переносится.
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
  const { t } = useI18n()

  return (
    <table className="w-full table-fixed text-sm">
      <caption className="sr-only">{t('Список броней')}</caption>
      <colgroup>
        <col className="w-19" />
        {/* Клиенту нужен запас шире остальных: «Абдуллаев» не должен ломаться
            по буквам, а аватару перед именем нужно ещё 30 px. */}
        <col className="w-30" />
        <col className="w-25" />
        <col className="w-22" />
        <col className="w-34" />
        <col className="w-19" />
      </colgroup>
      <thead>
        <tr className="text-left">
          {TABLE_COLUMNS.map((column) => (
            <th
              key={column.label}
              scope="col"
              className={cn(
                'border-b px-2 py-2 text-xs font-medium tracking-wide text-muted-foreground uppercase',
                column.align === 'right' && 'text-right'
              )}
            >
              {t(column.label)}
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
                  className="inline-block rounded-md py-1 font-medium break-words underline-offset-4 hover:underline focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
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
                <div className="flex min-w-0 items-start gap-1.5">
                  <Avatar size="sm" aria-hidden>
                    {booking.customer.avatarUrl && (
                      <AvatarImage src={booking.customer.avatarUrl} alt="" />
                    )}
                    <AvatarFallback className="text-xs">
                      {getInitials(customerName)}
                    </AvatarFallback>
                  </Avatar>
                  <div className="min-w-0">
                    <span className="block font-medium break-words">
                      {customerName}
                    </span>
                    <span
                      className="block truncate text-xs text-muted-foreground"
                      title={getAdminBookingCustomerContact(booking.customer)}
                    >
                      {getAdminBookingCustomerContact(booking.customer)}
                    </span>
                  </div>
                </div>
              </td>
              <td className="border-b px-2 py-2">
                <div className="flex min-w-0 items-center gap-1.5">
                  <Avatar size="sm" aria-hidden>
                    {booking.barber.avatarUrl && (
                      <AvatarImage src={booking.barber.avatarUrl} alt="" />
                    )}
                    <AvatarFallback className="text-xs">
                      {getInitials(booking.barber.displayName)}
                    </AvatarFallback>
                  </Avatar>
                  <span className="min-w-0 break-words">
                    {booking.barber.displayName}
                  </span>
                </div>
              </td>
              <td className="border-b px-2 py-2 break-words">
                {booking.service.name}
              </td>
              <td className="border-b px-2 py-2">
                <BookingStatusText status={booking.status} />
              </td>
              <td className="border-b px-2 py-2 text-right tabular-nums whitespace-nowrap">
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
  const { t } = useI18n()

  return (
    <ul aria-label={t('Список броней')} className="space-y-3">
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
                  <dt className="text-xs text-muted-foreground">
                    {t('Клиент')}
                  </dt>
                  <dd>
                    <div className="flex min-w-0 items-start gap-2">
                      <Avatar size="sm" aria-hidden>
                        {booking.customer.avatarUrl && (
                          <AvatarImage
                            src={booking.customer.avatarUrl}
                            alt=""
                          />
                        )}
                        <AvatarFallback className="text-xs">
                          {getInitials(customerName)}
                        </AvatarFallback>
                      </Avatar>
                      <div className="min-w-0">
                        <span className="block break-words font-medium">
                          {customerName}
                        </span>
                        <span className="block text-xs break-all text-muted-foreground">
                          {getAdminBookingCustomerContact(booking.customer)}
                        </span>
                      </div>
                    </div>
                  </dd>
                </div>
                <div className="min-w-0">
                  <dt className="text-xs text-muted-foreground">
                    {t('Барбер')}
                  </dt>
                  <dd>
                    <div className="flex min-w-0 items-center gap-2">
                      <Avatar size="sm" aria-hidden>
                        {booking.barber.avatarUrl && (
                          <AvatarImage src={booking.barber.avatarUrl} alt="" />
                        )}
                        <AvatarFallback className="text-xs">
                          {getInitials(booking.barber.displayName)}
                        </AvatarFallback>
                      </Avatar>
                      <span className="min-w-0 break-words">
                        {booking.barber.displayName}
                      </span>
                    </div>
                  </dd>
                </div>
                <div className="min-w-0">
                  <dt className="text-xs text-muted-foreground">
                    {t('Услуга')}
                  </dt>
                  <dd className="break-words">{booking.service.name}</dd>
                </div>
                <div className="min-w-0">
                  <dt className="text-xs text-muted-foreground">{t('Цена')}</dt>
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

/**
 * Статус строки: точка и слово, чтобы цвет не был единственным сигналом.
 *
 * `rounded-lg`, а не `rounded-full`: у «Ждёт подтверждения» две строки, и капсула
 * на такой высоте выглядит сплющенной. `px-1.5` — таблица плотнее карточек,
 * лишние 4 px уходят колонке услуги. Текст — отдельный элемент с `min-w-0`:
 * иначе flex-элемент не сжимается, длинное слово выходит за плашку и наезжает
 * на колонку цены.
 */
function BookingStatusText({ status }: { status: AdminBookingStatus }) {
  const { t } = useI18n()

  return (
    <span
      className={cn(
        'inline-flex max-w-full items-start gap-1.5 rounded-lg border px-1.5 py-0.5 text-xs font-medium',
        bookingStatusStyle(status)
      )}
    >
      <span
        aria-hidden
        className="mt-1 size-1.5 shrink-0 rounded-full bg-current"
      />
      <span className="min-w-0 break-words">
        {t(bookingStatusLabel(status))}
      </span>
    </span>
  )
}

const TABLE_COLUMNS = [
  { label: 'Время', align: 'left' },
  { label: 'Клиент', align: 'left' },
  { label: 'Барбер', align: 'left' },
  { label: 'Услуга', align: 'left' },
  { label: 'Статус', align: 'left' },
  { label: 'Цена', align: 'right' },
] as const

function BookingsSkeleton() {
  const { t } = useI18n()

  return (
    <div className="space-y-3" aria-busy="true">
      <p role="status" className="sr-only">
        {t('Загружаем брони…')}
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
  const { t } = useI18n()

  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
        <TriangleAlert className="size-4 text-destructive" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">{t('Не удалось загрузить брони')}</p>
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

/**
 * Пустой список: без фильтров — «Броней нет», с фильтрами — «Ничего не найдено».
 *
 * Счётчика «всего» нет: сервер отдаёт страницу без общего числа, а выдуманное
 * число в пустом состоянии только путало бы.
 */
function BookingsEmpty({ isFiltered }: { isFiltered: boolean }) {
  const { t } = useI18n()

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
          {isFiltered ? t('Ничего не найдено') : t('Броней нет')}
        </p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          {isFiltered
            ? t(
                'Под выбранные фильтры записей нет. Измените поиск или статус.'
              )
            : t(
                'Здесь появятся записи клиентов. Новые брони придут автоматически.'
              )}
        </p>
      </div>
    </div>
  )
}
