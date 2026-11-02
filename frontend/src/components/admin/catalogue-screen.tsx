import { Loader2, Plus, RefreshCw, Search, TriangleAlert, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'

import { AdminSignInGate } from '@/components/admin/admin-gate'
import {
  CatalogueActionDialog,
  type CatalogueActionRow,
} from '@/components/admin/catalogue-action-dialog'
import {
  HairServiceTable,
  SalonTable,
  SpecializationTable,
} from '@/components/admin/catalogue-table'
import { CatalogueTabs } from '@/components/admin/catalogue-tabs'
import { HairServiceDialog } from '@/components/admin/hair-service-dialog'
import { SalonDialog } from '@/components/admin/salon-dialog'
import { SpecializationDialog } from '@/components/admin/specialization-dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { useAdminCatalogueList } from '@/hooks/use-admin-catalogue'
import {
  ADMIN_CATALOGUE_EMPTY,
  ADMIN_CATALOGUE_ENTITY_LABELS,
  ADMIN_CATALOGUE_SEARCH_LABELS,
  ADMIN_CATALOGUE_SEARCH_MAX_LENGTH,
  ADMIN_CATALOGUE_STATUSES,
  ADMIN_CATALOGUE_STATUS_LABELS,
  ADMIN_CATALOGUE_TAB_LABELS,
  describeAdminCatalogueError,
  setHairServiceActive,
  setSalonActive,
  setSpecializationActive,
  type AdminCatalogueStatus,
  type AdminHairService,
  type AdminSalon,
  type AdminSpecialization,
  type CatalogueTab,
} from '@/lib/admin-catalogue'
import { cataloguePath } from '@/lib/admin-path'
import { ApiError } from '@/lib/api'
import { formatClockTime } from '@/lib/format'
import { navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * Каталог: услуги, специализации и салоны.
 *
 * Три вкладки одного экрана делят фильтр статуса и отличаются поиском. Справочник
 * отдаётся целиком, поэтому здесь нет пейджера и счётчиков: видимая строка — это строка,
 * которую администратор может включить обратно, а не «первая из двадцати».
 */
export function CatalogueScreen({ tab }: { tab: CatalogueTab }) {
  const [statusFilter, setStatusFilter] = useState<AdminCatalogueStatus>('all')
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [requiresSignIn, setRequiresSignIn] = useState(false)
  const [announcement, setAnnouncement] = useState<string | null>(null)

  const [createOpen, setCreateOpen] = useState(false)
  const [editId, setEditId] = useState<string | null>(null)
  const [toggleId, setToggleId] = useState<string | null>(null)
  const [pendingRowId, setPendingRowId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<{
    rowId: string
    message: string
  } | null>(null)
  const [actionDialogError, setActionDialogError] = useState<string | null>(null)

  // Задержка перед запросом: 300 мс хватает, чтобы не искать на каждую букву.
  useEffect(() => {
    const trimmed = searchInput.trim()

    if (trimmed === search) {
      return
    }

    const timer = window.setTimeout(() => setSearch(trimmed), 300)

    return () => window.clearTimeout(timer)
  }, [searchInput, search])

  const list = useAdminCatalogueList(tab, {
    status: statusFilter,
    search,
  })

  const headingRef = useRef<HTMLHeadingElement | null>(null)

  const editingService =
    list.tab === 'services' && editId !== null
      ? (list.rows.find((row) => row.id === editId) ?? null)
      : null
  const editingSpecialization =
    list.tab === 'specializations' && editId !== null
      ? (list.rows.find((row) => row.id === editId) ?? null)
      : null
  const editingSalon =
    list.tab === 'salons' && editId !== null
      ? (list.rows.find((row) => row.id === editId) ?? null)
      : null
  const toggleRow = findToggleRow(list.tab, toggleId, list.rows)

  function resetSearch() {
    setSearchInput('')
    setSearch('')
  }

  function focusRowAction(rowId: string | null) {
    // Диалог закрылся; фокус возвращаем на кнопку той же строки, иначе на заголовок.
    window.requestAnimationFrame(() => {
      const target = rowId
        ? document.getElementById(`catalogue-${tab}-${rowId}-edit`)
        : null

      if (target) {
        target.focus()
        return
      }

      headingRef.current?.focus()
    })
  }

  function handleTabChange(next: CatalogueTab) {
    if (next === tab) {
      return
    }

    // Вкладка сбрасывает поиск, но не статус: статус общий для справочников.
    resetSearch()
    setAnnouncement(null)
    navigate(cataloguePath(next))
  }

  function handleStatusChange(next: AdminCatalogueStatus) {
    if (next === statusFilter) {
      return
    }

    setStatusFilter(next)
    resetSearch()
  }

  function handleSaved(message: string, rowId: string | null) {
    setCreateOpen(false)
    setEditId(null)
    setAnnouncement(message)
    list.refresh()
    focusRowAction(rowId)
  }

  async function confirmToggle() {
    if (!toggleRow || pendingRowId !== null) {
      return
    }

    const nextActive = !toggleRow.isActive

    setPendingRowId(toggleRow.id)
    setActionDialogError(null)

    try {
      switch (tab) {
        case 'services':
          await setHairServiceActive(toggleRow.id, nextActive)
          break
        case 'specializations':
          await setSpecializationActive(toggleRow.id, nextActive)
          break
        case 'salons':
          await setSalonActive(toggleRow.id, nextActive)
          break
      }

      setToggleId(null)
      setActionError(null)
      setAnnouncement(toggleAnnouncement(tab, nextActive))
      list.refresh()
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 401) {
        setRequiresSignIn(true)
        return
      }

      setActionDialogError(
        describeAdminCatalogueError(
          cause,
          `Не удалось ${nextActive ? 'включить' : 'выключить'} строку. Попробуйте ещё раз.`
        )
      )

      // Строка устарела: список нужно перечитать, иначе повтор снова упадёт.
      if (cause instanceof ApiError && (cause.status === 409 || cause.status === 404)) {
        list.refresh()
      }
    } finally {
      setPendingRowId(null)
    }
  }

  if (list.requiresSignIn || requiresSignIn) {
    return (
      <div className="mx-auto w-full max-w-md py-6">
        <AdminSignInGate
          returnTo={cataloguePath(tab)}
          onSignedIn={() => {
            setRequiresSignIn(false)
            list.reload()
          }}
        />
      </div>
    )
  }

  const isFirstLoad = list.status === 'loading'
  const isEmpty = list.status === 'ready' && list.rows.length === 0

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1
            ref={headingRef}
            tabIndex={-1}
            className="scroll-mt-20 font-heading text-2xl font-semibold tracking-tight sm:text-3xl"
          >
            Каталог
          </h1>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
            Справочники услуг, специализаций и салонов.
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
            disabled={list.isRefreshing || list.status === 'loading'}
            onClick={list.refresh}
          >
            {list.isRefreshing ? (
              <Loader2 data-icon="inline-start" aria-hidden className="animate-spin" />
            ) : (
              <RefreshCw data-icon="inline-start" aria-hidden />
            )}
            {list.isRefreshing ? 'Обновляем…' : 'Обновить'}
          </Button>
          <Button
            size="sm"
            className="h-11 sm:h-9"
            onClick={() => {
              setAnnouncement(null)
              setCreateOpen(true)
            }}
          >
            <Plus data-icon="inline-start" aria-hidden />
            Создать {ADMIN_CATALOGUE_ENTITY_LABELS[tab].toLowerCase()}
          </Button>
        </div>
      </header>

      {announcement && (
        <div
          role="status"
          className="flex flex-wrap items-start justify-between gap-2 rounded-lg border bg-muted/50 px-3 py-2 text-sm"
        >
          <p className="min-w-0 flex-1">{announcement}</p>
          <button
            type="button"
            aria-label="Скрыть сообщение"
            className="inline-flex size-11 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:size-auto sm:p-1"
            onClick={() => setAnnouncement(null)}
          >
            <X className="size-3.5" aria-hidden />
          </button>
        </div>
      )}

      <CatalogueTabs tab={tab} onChange={handleTabChange} />

      <div className="flex flex-wrap items-center gap-2">
        <SegmentedStatusFilter
          value={statusFilter}
          onChange={handleStatusChange}
        />

        <div className="relative w-full sm:ml-auto sm:w-64">
          <label htmlFor="catalogue-search" className="sr-only">
            {ADMIN_CATALOGUE_SEARCH_LABELS[tab]}
          </label>
          <Search
            className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden
          />
          <Input
            id="catalogue-search"
            type="search"
            value={searchInput}
            maxLength={ADMIN_CATALOGUE_SEARCH_MAX_LENGTH}
            placeholder={ADMIN_CATALOGUE_SEARCH_LABELS[tab]}
            className="h-11 pr-11 pl-8 sm:h-8 sm:pr-9"
            onChange={(event) => setSearchInput(event.target.value)}
          />
          {searchInput !== '' && (
            <button
              type="button"
              aria-label="Очистить поиск"
              className="absolute top-1/2 right-1 inline-flex size-11 -translate-y-1/2 items-center justify-center rounded-md text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:right-1.5 sm:size-auto sm:p-1"
              onClick={resetSearch}
            >
              <X className="size-3.5" aria-hidden />
            </button>
          )}
        </div>
      </div>

      {list.refreshError && (
        <p role="alert" className="text-sm text-destructive">
          {list.refreshError}
        </p>
      )}

      <div
        role="tabpanel"
        id="catalogue-panel"
        aria-labelledby={`catalogue-tab-${tab}`}
        aria-busy={isFirstLoad}
      >
        {isFirstLoad && <CatalogueSkeleton />}

        {list.status === 'error' && (
          <CatalogueError
            message={list.error ?? 'Не удалось загрузить справочник.'}
            onRetry={list.reload}
          />
        )}

        {isEmpty && (
          <CatalogueEmpty
            tab={tab}
            isSearching={search !== ''}
            onRefresh={list.refresh}
          />
        )}

        {list.status === 'ready' && list.rows.length > 0 && (
          <CatalogueRows
            list={list}
            pendingRowId={pendingRowId}
            actionError={actionError}
            onEdit={(rowId) => {
              setActionError(null)
              setEditId(rowId)
            }}
            onToggle={(rowId) => {
              setActionError(null)
              setActionDialogError(null)
              setToggleId(rowId)
            }}
          />
        )}

        {/* Счётчиков «всего строк» нет: справочник отдаётся целиком, а число в колонке
            «Барберы» — это `usageCount` из API, а не длина массива. */}
      </div>

      {createOpen && tab === 'services' && (
        <HairServiceDialog
          mode="create"
          service={null}
          onClose={() => {
            setCreateOpen(false)
            focusRowAction(null)
          }}
          onSaved={(message) => handleSaved(message, null)}
        />
      )}

      {createOpen && tab === 'specializations' && (
        <SpecializationDialog
          mode="create"
          specialization={null}
          onClose={() => {
            setCreateOpen(false)
            focusRowAction(null)
          }}
          onSaved={(message) => handleSaved(message, null)}
        />
      )}

      {createOpen && tab === 'salons' && (
        <SalonDialog
          mode="create"
          salon={null}
          onClose={() => {
            setCreateOpen(false)
            focusRowAction(null)
          }}
          onSaved={(message) => handleSaved(message, null)}
          onContactsFailed={list.refresh}
        />
      )}

      {editingService && (
        <HairServiceDialog
          mode="edit"
          service={editingService}
          onClose={() => {
            const rowId = editingService.id
            setEditId(null)
            focusRowAction(rowId)
          }}
          onSaved={(message) => handleSaved(message, editingService.id)}
        />
      )}

      {editingSpecialization && (
        <SpecializationDialog
          mode="edit"
          specialization={editingSpecialization}
          onClose={() => {
            const rowId = editingSpecialization.id
            setEditId(null)
            focusRowAction(rowId)
          }}
          onSaved={(message) => handleSaved(message, editingSpecialization.id)}
        />
      )}

      {editingSalon && (
        <SalonDialog
          mode="edit"
          salon={editingSalon}
          onClose={() => {
            const rowId = editingSalon.id
            setEditId(null)
            focusRowAction(rowId)
          }}
          onSaved={(message) => handleSaved(message, editingSalon.id)}
          onContactsFailed={list.refresh}
        />
      )}

      {toggleRow && (
        <CatalogueActionDialog
          tab={tab}
          row={toggleRow}
          isPending={pendingRowId === toggleRow.id}
          error={actionDialogError}
          onClose={() => {
            const rowId = toggleRow.id
            setToggleId(null)
            setActionDialogError(null)
            focusRowAction(rowId)
          }}
          onConfirm={() => {
            void confirmToggle()
          }}
        />
      )}
    </div>
  )
}

/** Строка, для которой открыто подтверждение: сужается по типу вкладки. */
function findToggleRow(
  tab: CatalogueTab,
  rowId: string | null,
  rows: AdminHairService[] | AdminSpecialization[] | AdminSalon[]
): CatalogueActionRow | null {
  if (rowId === null) {
    return null
  }

  switch (tab) {
    case 'services':
      return (rows as AdminHairService[]).find((row) => row.id === rowId) ?? null
    case 'specializations':
      return (
        (rows as AdminSpecialization[]).find((row) => row.id === rowId) ?? null
      )
    case 'salons':
      return (rows as AdminSalon[]).find((row) => row.id === rowId) ?? null
  }
}

function toggleAnnouncement(tab: CatalogueTab, isActive: boolean): string {
  switch (tab) {
    case 'services':
      return isActive ? 'Услуга включена' : 'Услуга выключена'
    case 'specializations':
      return isActive ? 'Специализация включена' : 'Специализация выключена'
    case 'salons':
      return isActive ? 'Салон включён' : 'Салон выключен'
  }
}

type List = ReturnType<typeof useAdminCatalogueList>

/** Таблица активной вкладки: союз `list` сужается по `tab`, типы строк сходятся. */
function CatalogueRows({
  list,
  pendingRowId,
  actionError,
  onEdit,
  onToggle,
}: {
  list: List
  pendingRowId: string | null
  actionError: { rowId: string; message: string } | null
  onEdit: (rowId: string) => void
  onToggle: (rowId: string) => void
}) {
  const handlers = { pendingRowId, actionError, onEdit, onToggle }

  if (list.tab === 'services') {
    return <HairServiceTable rows={list.rows} {...handlers} />
  }

  if (list.tab === 'specializations') {
    return <SpecializationTable rows={list.rows} {...handlers} />
  }

  return <SalonTable rows={list.rows} {...handlers} />
}

/** Сегментированный фильтр: одна группа кнопок с `aria-pressed`, как у остальных списков. */
function SegmentedStatusFilter({
  value,
  onChange,
}: {
  value: AdminCatalogueStatus
  onChange: (value: AdminCatalogueStatus) => void
}) {
  return (
    <div
      role="group"
      aria-label="Статус строк каталога"
      className="flex max-w-full flex-wrap items-center gap-0.5 rounded-lg bg-muted p-0.5"
    >
      {ADMIN_CATALOGUE_STATUSES.map((status) => {
        const isSelected = status === value

        return (
          <button
            key={status}
            type="button"
            aria-pressed={isSelected}
            onClick={() => onChange(status)}
            className={cn(
              'inline-flex h-11 items-center rounded-md px-3 text-sm whitespace-nowrap transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-7',
              isSelected
                ? 'bg-background font-medium text-foreground shadow-sm ring-1 ring-foreground/10'
                : 'text-muted-foreground hover:text-foreground'
            )}
          >
            {ADMIN_CATALOGUE_STATUS_LABELS[status]}
          </button>
        )
      })}
    </div>
  )
}

function CatalogueSkeleton() {
  return (
    <div className="space-y-3" aria-busy="true">
      <p role="status" className="sr-only">
        Загружаем справочник…
      </p>
      <Skeleton className="h-8 w-full" aria-hidden />
      <Skeleton className="h-14 w-full" aria-hidden />
      <Skeleton className="h-14 w-full" aria-hidden />
      <Skeleton className="h-14 w-full" aria-hidden />
    </div>
  )
}

function CatalogueError({
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
        <p className="font-medium">Не удалось загрузить справочник</p>
        <p role="alert" className="mx-auto max-w-sm text-sm text-muted-foreground">
          {message}
        </p>
      </div>
      <Button size="sm" variant="outline" className="h-11 sm:h-8" onClick={onRetry}>
        Повторить
      </Button>
    </div>
  )
}

function CatalogueEmpty({
  tab,
  isSearching,
  onRefresh,
}: {
  tab: CatalogueTab
  isSearching: boolean
  onRefresh: () => void
}) {
  if (isSearching) {
    return (
      <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Search className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">Ничего не найдено</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            В справочнике «{ADMIN_CATALOGUE_TAB_LABELS[tab].toLowerCase()}» нет строки
            с таким названием. Попробуйте другую строку.
          </p>
        </div>
      </div>
    )
  }

  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <Search className="size-4 text-muted-foreground" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">Справочник пуст</p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          {ADMIN_CATALOGUE_EMPTY[tab]}
        </p>
      </div>
      <Button size="sm" variant="outline" className="h-11 sm:h-8" onClick={onRefresh}>
        Обновить
      </Button>
    </div>
  )
}
