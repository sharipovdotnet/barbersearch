import { Eye, EyeOff, Loader2 } from 'lucide-react'
import type { ReactNode } from 'react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  ADMIN_CATALOGUE_ACTIVITY_LABELS,
  ADMIN_CATALOGUE_CAPTIONS,
  type AdminHairService,
  type AdminSalon,
  type AdminSpecialization,
  type CatalogueTab,
} from '@/lib/admin-catalogue'
import { SERVICE_CATEGORY_LABELS } from '@/lib/barber-profile-labels'
import { formatDuration } from '@/lib/format'

/**
 * Таблицы каталога: на `lg` и шире — `<table>` с `caption`, уже — карточки.
 *
 * Горизонтального скролла нет ни в одной ширине: карточки на телефоне перестраивают
 * те же колонки в список «подпись — значение». Статус всегда текстом, а не только цветом.
 */

type CatalogueRow = {
  id: string
  name: string
  isActive: boolean
  usageCount: number
}

type Column<T> = {
  id: string
  label: string
  render: (row: T) => ReactNode
}

type RowHandlers = {
  /** Строка, действие которой сейчас в полёте; остальные кнопки заблокированы. */
  pendingRowId: string | null
  /** Ошибка действия у конкретной строки: строки при этом не стираются. */
  actionError: { rowId: string; message: string } | null
  onEdit: (rowId: string) => void
  onToggle: (rowId: string) => void
}

export function HairServiceTable({
  rows,
  ...handlers
}: { rows: AdminHairService[] } & RowHandlers) {
  return (
    <CatalogueTable
      rows={rows}
      tab="services"
      columns={[
        { id: 'name', label: 'Название', render: (row) => row.name },
        {
          id: 'category',
          label: 'Категория',
          render: (row) => SERVICE_CATEGORY_LABELS[row.category],
        },
        {
          id: 'duration',
          label: 'Длительность',
          render: (row) => formatDuration(row.defaultDurationMinutes),
        },
      ]}
      {...handlers}
    />
  )
}

export function SpecializationTable({
  rows,
  ...handlers
}: { rows: AdminSpecialization[] } & RowHandlers) {
  return (
    <CatalogueTable
      rows={rows}
      tab="specializations"
      columns={[
        { id: 'name', label: 'Название', render: (row) => row.name },
        {
          id: 'description',
          label: 'Описание',
          render: (row) => (
            <span className="line-clamp-2 text-muted-foreground">
              {row.description ?? '—'}
            </span>
          ),
        },
      ]}
      {...handlers}
    />
  )
}

export function SalonTable({
  rows,
  ...handlers
}: { rows: AdminSalon[] } & RowHandlers) {
  return (
    <CatalogueTable
      rows={rows}
      tab="salons"
      columns={[
        { id: 'name', label: 'Название', render: (row) => row.name },
        { id: 'city', label: 'Город', render: (row) => row.city },
        {
          id: 'address',
          label: 'Адрес',
          // Телефон — контакт салона: отдельной колонки нет, но правится он в том же
          // диалоге, и строке нужен видимый признак, что правка применилась.
          render: (row) => (
            <span className="block">
              {row.address}
              {row.phoneNumber && (
                <span className="mt-1 block text-xs text-muted-foreground tabular-nums">
                  {row.phoneNumber}
                </span>
              )}
            </span>
          ),
        },
        {
          id: 'timeZoneId',
          label: 'Часовой пояс',
          render: (row) => <span className="tabular-nums">{row.timeZoneId}</span>,
        },
      ]}
      {...handlers}
    />
  )
}

function CatalogueTable<T extends CatalogueRow>({
  rows,
  tab,
  columns,
  pendingRowId,
  actionError,
  onEdit,
  onToggle,
}: {
  rows: T[]
  tab: CatalogueTab
  columns: Column<T>[]
} & RowHandlers) {
  const handlers = { tab, pendingRowId, actionError, onEdit, onToggle }

  return (
    <>
      <table className="hidden w-full text-sm lg:table">
        <caption className="sr-only">{ADMIN_CATALOGUE_CAPTIONS[tab]}</caption>
        <thead>
          <tr className="text-left">
            {columns.map((column) => (
              <th
                key={column.id}
                scope="col"
                className="border-b px-3 py-2 text-xs font-medium tracking-wide text-muted-foreground uppercase"
              >
                {column.label}
              </th>
            ))}
            <th
              scope="col"
              className="border-b px-3 py-2 text-xs font-medium tracking-wide text-muted-foreground uppercase"
            >
              Барберы
            </th>
            <th
              scope="col"
              className="border-b px-3 py-2 text-xs font-medium tracking-wide text-muted-foreground uppercase"
            >
              Статус
            </th>
            <th scope="col" className="border-b px-3 py-2">
              <span className="sr-only">Действия</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.id} className="align-top">
              {columns.map((column) => (
                <td key={column.id} className="border-b px-3 py-3">
                  {column.render(row)}
                </td>
              ))}
              <td className="border-b px-3 py-3 tabular-nums">
                {row.usageCount}
              </td>
              <td className="border-b px-3 py-3">
                <StatusBadge tab={tab} isActive={row.isActive} />
              </td>
              <td className="border-b px-3 py-3">
                <RowActions row={row} {...handlers} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <ul className="flex flex-col gap-3 lg:hidden">
        {rows.map((row) => (
          <li key={row.id} className="min-w-0 rounded-xl border p-4">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <p className="min-w-0 font-medium break-words">{row.name}</p>
              <StatusBadge tab={tab} isActive={row.isActive} />
            </div>

            <dl className="mt-3 grid gap-2 text-sm sm:grid-cols-2">
              {columns.map((column) => (
                <div key={column.id} className="min-w-0">
                  <dt className="text-xs text-muted-foreground">{column.label}</dt>
                  <dd className="break-words">{column.render(row)}</dd>
                </div>
              ))}
              <div>
                <dt className="text-xs text-muted-foreground">Барберы</dt>
                <dd className="tabular-nums">{row.usageCount}</dd>
              </div>
            </dl>

            <div className="mt-3">
              <RowActions row={row} {...handlers} />
            </div>
          </li>
        ))}
      </ul>
    </>
  )
}

/** Статус строки: подпись словом, бейдж — только оформление. */
function StatusBadge({
  tab,
  isActive,
}: {
  tab: CatalogueTab
  isActive: boolean
}) {
  const labels = ADMIN_CATALOGUE_ACTIVITY_LABELS[tab]

  return (
    <Badge variant={isActive ? 'secondary' : 'outline'}>
      {isActive ? labels.active : labels.hidden}
    </Badge>
  )
}

function RowActions({
  tab,
  row,
  pendingRowId,
  actionError,
  onEdit,
  onToggle,
}: RowActionsProps & { row: CatalogueRow; tab: CatalogueTab }) {
  const isPending = pendingRowId === row.id
  // Ровно одно действие в полёте: остальные кнопки таблицы ждут, чтобы не запускать
  // второй запрос по той же строке.
  const isBusy = pendingRowId !== null

  return (
    <div className="flex flex-wrap items-start gap-2">
      <Button
        id={`catalogue-${tab}-${row.id}-edit`}
        type="button"
        variant="outline"
        size="sm"
        className="h-11 sm:h-8"
        disabled={isBusy}
        onClick={() => onEdit(row.id)}
      >
        Изменить
      </Button>
      <Button
        id={`catalogue-${tab}-${row.id}-toggle`}
        type="button"
        variant={row.isActive ? 'outline' : 'default'}
        size="sm"
        className="h-11 sm:h-8"
        disabled={isBusy}
        aria-busy={isPending}
        onClick={() => onToggle(row.id)}
      >
        {isPending ? (
          <Loader2 data-icon="inline-start" aria-hidden className="animate-spin" />
        ) : row.isActive ? (
          <EyeOff data-icon="inline-start" aria-hidden />
        ) : (
          <Eye data-icon="inline-start" aria-hidden />
        )}
        {row.isActive ? 'Выключить' : 'Включить'}
      </Button>

      {actionError?.rowId === row.id && (
        <p role="alert" className="w-full text-sm text-destructive">
          {actionError.message}
        </p>
      )}
    </div>
  )
}

type RowActionsProps = Omit<RowHandlers, 'tab'>
