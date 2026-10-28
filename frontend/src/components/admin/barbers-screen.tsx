import { useEffect, useRef, useState } from 'react'
import {
  Inbox,
  Loader2,
  RefreshCw,
  Search,
  TriangleAlert,
  Users,
  X,
} from 'lucide-react'

import { AdminSignInGate } from '@/components/admin/admin-gate'
import {
  BarberDetail,
  BarberDetailPlaceholder,
  BarberDetailState,
} from '@/components/admin/barber-detail'
import { BarberList } from '@/components/admin/barber-list'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { useAdminBarber } from '@/hooks/use-admin-barber'
import { useAdminBarberList } from '@/hooks/use-admin-barber-list'
import { useMediaQuery } from '@/hooks/use-media-query'
import {
  ADMIN_BARBER_SEARCH_MAX_LENGTH,
  ADMIN_BARBER_STATUS_LABELS,
  type AdminBarberStatusFilter,
} from '@/lib/admin-barbers'
import { barbersPath } from '@/lib/admin-path'
import { formatClockTime } from '@/lib/format'
import { navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

const FILTERS: readonly AdminBarberStatusFilter[] = [
  'pending',
  'all',
  'hidden',
]

/**
 * Экран «Барберы»: очередь проверки, поиск по имени и email, карточка профиля.
 *
 * Сетка — split-view на широком экране; на телефоне список и карточка сменяют
 * друг друга, а выбранный профиль живёт в адресе, поэтому перезагрузка не теряет
 * контекст.
 *
 * Поиск отправляется с задержкой: сервер ищет по подстроке, и запрос на каждую
 * букву только грел бы лимит. Счётчиков на фильтрах нет — API не отдаёт общее число.
 */
export function BarbersScreen({ barberId }: { barberId: string | null }) {
  const [filter, setFilter] = useState<AdminBarberStatusFilter>('pending')
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')

  // Задержка перед запросом: 300 мс хватает, чтобы не искать на каждую букву.
  useEffect(() => {
    const trimmed = searchInput.trim()

    if (trimmed === search) {
      return
    }

    const timer = window.setTimeout(() => setSearch(trimmed), 300)

    return () => window.clearTimeout(timer)
  }, [searchInput, search])

  const list = useAdminBarberList({ status: filter, search })
  const detail = useAdminBarber(barberId)
  const isWide = useMediaQuery('(min-width: 1024px)')
  const headingRef = useRef<HTMLHeadingElement | null>(null)

  // На широком экране первая строка открывается сразу: пустая правая колонка
  // выглядит как ошибка загрузки.
  useEffect(() => {
    if (
      !isWide ||
      barberId ||
      list.status !== 'ready' ||
      list.items.length === 0
    ) {
      return
    }

    navigate(barbersPath(list.items[0].id), { replace: true })
  }, [isWide, barberId, list.status, list.items])

  // На телефоне карточка заменяет список — переход должен начинаться сверху.
  useEffect(() => {
    if (!barberId || isWide) {
      return
    }

    window.scrollTo({ top: 0 })
  }, [barberId, isWide])

  function handleFilterChange(next: AdminBarberStatusFilter) {
    if (next === filter) {
      return
    }

    setFilter(next)
    // Поиск сбрасывается вместе с фильтром: иначе «На проверке» с прошлым запросом
    // выглядит как пустая очередь, хотя дело в поиске.
    setSearchInput('')
    setSearch('')
  }

  function handleSelect(id: string) {
    navigate(barbersPath(id))
  }

  function handleBack() {
    navigate(barbersPath())

    // Фокус возвращается на заголовок, а не в <body>: на телефоне списка ещё нет,
    // пока страница не перерисовалась.
    window.requestAnimationFrame(() => {
      window.requestAnimationFrame(() => headingRef.current?.focus())
    })
  }

  const trimmedSearch = searchInput.trim()
  const isEmpty = list.status === 'ready' && list.items.length === 0
  const isMobileDetail = !isWide && barberId !== null
  const showGrid =
    list.status === 'ready' && (list.items.length > 0 || barberId !== null)

  if (list.requiresSignIn) {
    return (
      <div className="mx-auto w-full max-w-md py-6">
        <AdminSignInGate returnTo={barbersPath()} onSignedIn={list.reload} />
      </div>
    )
  }

  return (
    <div className="space-y-5">
      {isMobileDetail && (
        <h1 ref={headingRef} tabIndex={-1} className="sr-only">
          Барберы
        </h1>
      )}

      {!isMobileDetail && (
        <header className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h1
              ref={headingRef}
              tabIndex={-1}
              className="font-heading text-2xl font-semibold tracking-tight sm:text-3xl"
            >
              Барберы
            </h1>
            <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
              Подтвердите профиль, присвойте грейд или уберите барбера из поиска.
              Пока профиль не подтверждён, он не появляется в выдаче.
            </p>
          </div>

          <div className="flex items-center gap-2">
            {list.updatedAt !== null && (
              <span className="text-xs text-muted-foreground tabular-nums">
                Обновлено в {formatClockTime(new Date(list.updatedAt))}
              </span>
            )}
            <Button
              variant="outline"
              size="sm"
              className="h-11 sm:h-7"
              disabled={list.isRefreshing || list.status === 'loading'}
              onClick={list.reload}
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

      {list.refreshError && (
        <p role="alert" className="text-sm text-destructive">
          {list.refreshError}
        </p>
      )}

      {!isMobileDetail && (
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div
            role="group"
            aria-label="Каких барберов показать"
            className="inline-flex items-center gap-0.5 self-start rounded-lg bg-muted p-0.5"
          >
            {FILTERS.map((item) => {
              const isSelected = item === filter

              return (
                <button
                  key={item}
                  type="button"
                  aria-pressed={isSelected}
                  onClick={() => handleFilterChange(item)}
                  className={cn(
                    'inline-flex h-11 items-center rounded-md px-3 text-sm transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-7',
                    isSelected
                      ? 'bg-background font-medium text-foreground shadow-sm ring-1 ring-foreground/10'
                      : 'text-muted-foreground hover:text-foreground'
                  )}
                >
                  {ADMIN_BARBER_STATUS_LABELS[item]}
                </button>
              )
            })}
          </div>

          <div className="relative w-full sm:max-w-xs">
            <label htmlFor="barbers-search" className="sr-only">
              Поиск по имени или email
            </label>
            <Search
              className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
              aria-hidden
            />
            <Input
              id="barbers-search"
              type="search"
              value={searchInput}
              maxLength={ADMIN_BARBER_SEARCH_MAX_LENGTH}
              placeholder="Имя или email"
              className="h-11 pr-9 pl-8 sm:h-8"
              onChange={(event) => setSearchInput(event.target.value)}
            />
            {searchInput !== '' && (
              <button
                type="button"
                aria-label="Очистить поиск"
                className="absolute top-1/2 right-1.5 -translate-y-1/2 rounded-md p-1 text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
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
      )}

      <p role="status" className="sr-only">
        {list.status === 'ready' ? `Показано ${list.items.length}` : ''}
      </p>

      {list.status === 'loading' && <BarbersSkeleton />}

      {list.status === 'error' && (
        <BarbersError
          message={list.error ?? 'Не удалось загрузить барберов.'}
          onRetry={list.reload}
        />
      )}

      {isEmpty && (
        <BarbersEmpty
          filter={filter}
          search={trimmedSearch}
          onRefresh={list.reload}
        />
      )}

      {showGrid && (
        <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start">
          <div
            className={cn('min-w-0', barberId ? 'hidden lg:block' : 'block')}
          >
            {list.items.length > 0 ? (
              <BarberList
                items={list.items}
                selectedId={barberId}
                nextCursor={list.nextCursor}
                isLoadingMore={list.isLoadingMore}
                loadMoreError={list.loadMoreError}
                onSelect={handleSelect}
                onLoadMore={list.loadMore}
              />
            ) : (
              <p className="text-sm text-muted-foreground">
                В загруженной странице нет барберов.
              </p>
            )}
          </div>

          <div
            className={cn('min-w-0', barberId ? 'block' : 'hidden lg:block')}
          >
            {detail.barber ? (
              <BarberDetail barber={detail.barber} onBack={handleBack} />
            ) : barberId ? (
              <BarberDetailState
                status={detail.status}
                error={detail.error}
                onRetry={detail.reload}
                onBack={handleBack}
              />
            ) : (
              <BarberDetailPlaceholder />
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function BarbersSkeleton() {
  return (
    <div className="space-y-3" aria-busy="true">
      <p role="status" className="sr-only">
        Загружаем барберов…
      </p>
      <Skeleton className="h-4 w-32" aria-hidden />
      <Skeleton className="h-96 rounded-xl" aria-hidden />
    </div>
  )
}

function BarbersError({
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
        <p className="font-medium">Не удалось загрузить барберов</p>
        <p role="alert" className="mx-auto max-w-sm text-sm text-muted-foreground">
          {message}
        </p>
      </div>
      <Button
        size="sm"
        variant="outline"
        className="h-11 sm:h-7"
        onClick={onRetry}
      >
        Повторить
      </Button>
    </div>
  )
}

function BarbersEmpty({
  filter,
  search,
  onRefresh,
}: {
  filter: AdminBarberStatusFilter
  search: string
  onRefresh: () => void
}) {
  if (search !== '') {
    return (
      <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Search className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">Ничего не найдено</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            По запросу «{search}» нет ни имени, ни email. Попробуйте другую
            строку.
          </p>
        </div>
      </div>
    )
  }

  if (filter === 'pending') {
    return (
      <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Users className="size-4 text-success" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">Очередь проверки пуста</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            Все профили подтверждены. Новые появятся здесь, когда барберы
            зарегистрируются.
          </p>
        </div>
        <Button
          size="sm"
          variant="outline"
          className="h-11 sm:h-7"
          onClick={onRefresh}
        >
          Обновить
        </Button>
      </div>
    )
  }

  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <Inbox className="size-4 text-muted-foreground" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">
          {filter === 'hidden' ? 'Скрытых профилей нет' : 'Барберов пока нет'}
        </p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          {filter === 'hidden'
            ? 'Никто не убран из поиска: все профили либо подтверждены, либо ждут проверки.'
            : 'Профили появятся здесь, как только барберы их создадут.'}
        </p>
      </div>
    </div>
  )
}
