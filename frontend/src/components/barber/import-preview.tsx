import { useCallback, useEffect, useRef } from 'react'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useMediaQuery } from '@/hooks/use-media-query'
import type { ImportRow, ImportSummary } from '@/hooks/use-barber-import'
import type { BarberBookingOptions } from '@/lib/barber-booking-options'
import {
  formatBookingsCount,
  formatPrice,
  formatShortDay,
} from '@/lib/format'
import type { ImportDraft, ImportIssue, ImportRowPatch } from '@/lib/import-parse'
import { cn } from '@/lib/utils'

/**
 * Шаг 2: проверка разобранного списка.
 *
 * Десктоп — настоящая таблица, телефон — карточки с теми же полями: горизонтального
 * скролла нет ни на одной ширине. Всё, что не понято, подсвечено и правится на месте;
 * сводка сверху называет проблемные строки и уводит фокус в первую из них.
 */

const ISSUE_TEXT: Record<ImportIssue, string> = {
  noDate: 'Не понял дату.',
  badDate: 'Не понял дату.',
  noTime: 'Не понял время.',
  badTime: 'Не понял время.',
  noName: 'Нужно имя.',
  noPhone: 'Нужен телефон.',
  badPhone: 'Телефон похож на неполный.',
  noService: 'Не нашёл услугу.',
  ambiguousService: 'Услуга неоднозначна — выберите.',
  past: 'Время уже прошло.',
  duplicate: 'Дубль в списке.',
}

const COLUMNS = [
  'Строка',
  'Когда',
  'Клиент',
  'Телефон',
  'Услуга',
  'Салон',
  'Цена',
  'Состояние',
] as const

function hasIssue(draft: ImportDraft, issue: ImportIssue): boolean {
  return draft.issues.includes(issue)
}

function stateText(row: ImportRow): string {
  if (row.status === 'creating') {
    return 'Создаём…'
  }

  if (row.status === 'created') {
    return 'Создано'
  }

  if (row.status === 'failed' && row.failure !== null) {
    return row.failure
  }

  const first = row.draft.issues[0]

  return first === undefined ? 'Готово' : ISSUE_TEXT[first]
}

function dateLabel(date: string | null): string {
  return date === null ? '—' : formatShortDay(`${date}T00:00:00Z`, 'UTC')
}

function summaryParts(summary: ImportSummary): string[] {
  const parts = [`Готово ${summary.ready} из ${summary.total}`]

  if (summary.missingService > 0) {
    parts.push(`${summary.missingService} без услуги`)
  }

  if (summary.past > 0) {
    parts.push(`${summary.past} в прошлом`)
  }

  return parts
}

/** Поле телефона: нормализация — на уходе из поля, иначе каретка прыгает. */
function PhoneField({
  draft,
  onUpdate,
  label,
  describedBy,
  invalid,
}: {
  draft: ImportDraft
  onUpdate: (line: number, patch: ImportRowPatch) => void
  label: string
  describedBy: string
  invalid: boolean
}) {
  return (
    <Input
      // Ключ — нормализованный номер: после правки поле перемонтируется с ним,
      // а во время набора остаётся тем, что напечатал барбер.
      key={draft.phone ?? ''}
      type="tel"
      inputMode="tel"
      autoComplete="off"
      aria-label={label}
      aria-invalid={invalid}
      aria-describedby={describedBy}
      defaultValue={draft.phone ?? ''}
      className="h-11 sm:h-8"
      onBlur={(event) => {
        onUpdate(draft.line, { phone: event.target.value })
      }}
    />
  )
}

function ServiceField({
  draft,
  options,
  label,
  describedBy,
  invalid,
  onUpdate,
}: {
  draft: ImportDraft
  options: BarberBookingOptions
  label: string
  describedBy: string
  invalid: boolean
  onUpdate: (line: number, patch: ImportRowPatch) => void
}) {
  const active = options.services.filter((service) => service.isActive)
  // Кандидаты при неоднозначности — первыми: барберу нужно выбрать из похожих.
  const ordered = [
    ...active.filter((service) =>
      draft.serviceCandidates.includes(service.barberServiceId)
    ),
    ...active.filter(
      (service) => !draft.serviceCandidates.includes(service.barberServiceId)
    ),
  ]

  return (
    <select
      aria-label={label}
      aria-invalid={invalid}
      aria-describedby={describedBy}
      value={draft.serviceId ?? ''}
      className="h-11 w-full min-w-0 rounded-md border border-input bg-transparent px-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-8"
      onChange={(event) => {
        onUpdate(draft.line, {
          serviceId: event.target.value === '' ? null : event.target.value,
        })
      }}
    >
      <option value="">Выберите услугу</option>
      {ordered.map((service) => (
        <option key={service.barberServiceId} value={service.barberServiceId}>
          {service.name}
        </option>
      ))}
    </select>
  )
}

type RowParts = {
  draft: ImportDraft
  options: BarberBookingOptions
  onUpdate: (line: number, patch: ImportRowPatch) => void
  describedBy: string
}

function WhenFields({ draft, onUpdate, describedBy }: RowParts) {
  const needsDate = hasIssue(draft, 'noDate') || hasIssue(draft, 'badDate')
  const needsTime =
    hasIssue(draft, 'noTime')
    || hasIssue(draft, 'badTime')
    || hasIssue(draft, 'past')
    || hasIssue(draft, 'duplicate')

  if (!needsDate && !needsTime) {
    return (
      <span className="whitespace-nowrap">
        {dateLabel(draft.date)}, {draft.time ?? '—'}
      </span>
    )
  }

  return (
    <div className="flex flex-col gap-1">
      {needsDate && (
        <Input
          type="date"
          aria-label={`Дата, строка ${draft.line}`}
          aria-invalid
          aria-describedby={describedBy}
          value={draft.date ?? ''}
          className="h-11 sm:h-8"
          onChange={(event) => {
            onUpdate(draft.line, { date: event.target.value || null })
          }}
        />
      )}
      {needsTime && (
        <Input
          type="time"
          aria-label={`Время, строка ${draft.line}`}
          aria-invalid
          aria-describedby={describedBy}
          value={draft.time ?? ''}
          className="h-11 w-28 sm:h-8"
          onChange={(event) => {
            onUpdate(draft.line, { time: event.target.value || null })
          }}
        />
      )}
      {draft.date !== null && draft.time !== null && (
        <span className="text-xs text-muted-foreground">
          {dateLabel(draft.date)}, {draft.time}
        </span>
      )}
    </div>
  )
}

function NameField({ draft, onUpdate, describedBy }: RowParts) {
  if (!hasIssue(draft, 'noName')) {
    return <span>{draft.firstName ?? '—'}</span>
  }

  return (
    <Input
      aria-label={`Имя, строка ${draft.line}`}
      aria-invalid
      aria-describedby={describedBy}
      value={draft.firstName ?? ''}
      className="h-11 sm:h-8"
      onChange={(event) => {
        onUpdate(draft.line, { firstName: event.target.value || null })
      }}
    />
  )
}

function PhoneCell({ draft, onUpdate, describedBy }: RowParts) {
  const invalid = hasIssue(draft, 'noPhone') || hasIssue(draft, 'badPhone')

  if (!invalid) {
    return <span className="whitespace-nowrap">{draft.phone ?? '—'}</span>
  }

  return (
    <PhoneField
      draft={draft}
      onUpdate={onUpdate}
      label={`Телефон, строка ${draft.line}`}
      describedBy={describedBy}
      invalid
    />
  )
}

function ServiceCell({ draft, options, onUpdate, describedBy }: RowParts) {
  const invalid =
    hasIssue(draft, 'noService') || hasIssue(draft, 'ambiguousService')

  if (invalid) {
    return (
      <ServiceField
        draft={draft}
        options={options}
        label={`Услуга, строка ${draft.line}`}
        describedBy={describedBy}
        invalid
        onUpdate={onUpdate}
      />
    )
  }

  const service = options.services.find(
    (item) => item.barberServiceId === draft.serviceId
  )

  return <span>{service?.name ?? '—'}</span>
}

function SalonCell({ draft, options, onUpdate, describedBy }: RowParts) {
  const salon = options.salons.find((item) => item.salonId === draft.salonId)

  if (options.salons.length <= 1) {
    return <span>{salon?.name ?? '—'}</span>
  }

  return (
    <select
      aria-label={`Салон, строка ${draft.line}`}
      aria-describedby={describedBy}
      value={draft.salonId ?? ''}
      className="h-11 w-full min-w-0 rounded-md border border-input bg-transparent px-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-8"
      onChange={(event) => {
        onUpdate(draft.line, {
          salonId: event.target.value === '' ? null : event.target.value,
        })
      }}
    >
      {options.salons.map((item) => (
        <option key={item.salonId} value={item.salonId}>
          {item.name}
        </option>
      ))}
    </select>
  )
}

function PriceCell({ draft, options }: RowParts) {
  const service = options.services.find(
    (item) => item.barberServiceId === draft.serviceId
  )

  return <span>{service === undefined ? '—' : formatPrice(service.price)}</span>
}

export function ImportPreview({
  rows,
  summary,
  options,
  isRunning,
  progress,
  onUpdate,
  onCreate,
  onBack,
}: {
  rows: ImportRow[]
  summary: ImportSummary
  options: BarberBookingOptions
  isRunning: boolean
  progress: { done: number; total: number } | null
  onUpdate: (line: number, patch: ImportRowPatch) => void
  onCreate: () => void
  onBack: () => void
}) {
  const isWide = useMediaQuery('(min-width: 640px)')
  const summaryRef = useRef<HTMLParagraphElement>(null)
  const rowRefs = useRef(new Map<number, HTMLElement>())

  useEffect(() => {
    // Разбор — действие пользователя: фокус переезжает на сводку, чтобы
    // клавиатурный путь не начинался с середины старой формы.
    summaryRef.current?.focus({ preventScroll: true })
  }, [])

  const registerRow = useCallback((line: number) => {
    return (node: HTMLElement | null) => {
      if (node === null) {
        rowRefs.current.delete(line)
      } else {
        rowRefs.current.set(line, node)
      }
    }
  }, [])

  function focusFirst(issue: ImportIssue) {
    const row = rows.find((item) => item.draft.issues.includes(issue))

    if (row === undefined) {
      return
    }

    const node = rowRefs.current.get(row.draft.line)

    node?.focus({ preventScroll: true })
    node?.scrollIntoView({ block: 'start' })
  }

  const parts = summaryParts(summary)

  return (
    <section aria-labelledby="import-preview-title" className="space-y-4">
      <h3 id="import-preview-title" className="sr-only">
        Проверка записей
      </h3>

      <p
        ref={summaryRef}
        tabIndex={-1}
        className="flex flex-wrap items-center gap-x-1 text-sm outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
      >
        <span role="status">{parts[0]}</span>
        {summary.missingService > 0 && (
          <span>
            {' · '}
            <button
              type="button"
              className="underline underline-offset-2"
              onClick={() => {
                focusFirst('noService')
              }}
            >
              {parts[1]}
            </button>
          </span>
        )}
        {summary.past > 0 && (
          <span>
            {' · '}
            <button
              type="button"
              className="underline underline-offset-2"
              onClick={() => {
                focusFirst('past')
              }}
            >
              {summary.past} в прошлом
            </button>
          </span>
        )}
      </p>

      {isWide ? (
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-sm">
            <caption className="sr-only">Разобранные записи</caption>
            <thead>
              <tr className="border-b text-left text-muted-foreground">
                {COLUMNS.map((column) => (
                  <th
                    key={column}
                    scope="col"
                    className="py-2 pr-2 font-medium whitespace-nowrap"
                  >
                    {column}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => {
                const errorId = `import-row-${row.draft.line}-error`
                const invalid = row.draft.issues.length > 0

                return (
                  <tr
                    key={row.draft.line}
                    id={`import-row-${row.draft.line}`}
                    ref={registerRow(row.draft.line)}
                    tabIndex={-1}
                    className={cn(
                      'border-b align-top outline-none',
                      invalid && 'bg-destructive/5'
                    )}
                  >
                    <td className="py-2 pr-2 text-muted-foreground">
                      {row.draft.line}
                    </td>
                    <td className="py-2 pr-2">
                      <WhenFields
                        draft={row.draft}
                        options={options}
                        onUpdate={onUpdate}
                        describedBy={errorId}
                      />
                    </td>
                    <td className="py-2 pr-2">
                      <NameField
                        draft={row.draft}
                        options={options}
                        onUpdate={onUpdate}
                        describedBy={errorId}
                      />
                    </td>
                    <td className="py-2 pr-2">
                      <PhoneCell
                        draft={row.draft}
                        options={options}
                        onUpdate={onUpdate}
                        describedBy={errorId}
                      />
                    </td>
                    <td className="py-2 pr-2">
                      <ServiceCell
                        draft={row.draft}
                        options={options}
                        onUpdate={onUpdate}
                        describedBy={errorId}
                      />
                    </td>
                    <td className="py-2 pr-2">
                      <SalonCell
                        draft={row.draft}
                        options={options}
                        onUpdate={onUpdate}
                        describedBy={errorId}
                      />
                    </td>
                    <td className="py-2 pr-2 whitespace-nowrap">
                      <PriceCell
                        draft={row.draft}
                        options={options}
                        onUpdate={onUpdate}
                        describedBy={errorId}
                      />
                    </td>
                    <td className="py-2">
                      <span
                        id={errorId}
                        className={cn(
                          'block text-sm',
                          invalid ? 'text-destructive' : 'text-muted-foreground'
                        )}
                      >
                        {stateText(row)}
                      </span>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      ) : (
        <ul className="space-y-3">
          {rows.map((row) => {
            const errorId = `import-row-${row.draft.line}-error`
            const invalid = row.draft.issues.length > 0

            return (
              <li
                key={row.draft.line}
                id={`import-row-${row.draft.line}`}
                ref={registerRow(row.draft.line)}
                tabIndex={-1}
                className={cn(
                  'space-y-2 rounded-xl border bg-card p-3 outline-none',
                  invalid && 'border-destructive/40 bg-destructive/5'
                )}
              >
                <div className="flex items-start justify-between gap-2">
                  <p className="text-sm font-medium">Строка {row.draft.line}</p>
                  <span
                    id={errorId}
                    className={cn(
                      'text-right text-sm',
                      invalid ? 'text-destructive' : 'text-muted-foreground'
                    )}
                  >
                    {stateText(row)}
                  </span>
                </div>
                <dl className="grid grid-cols-[5.5rem_1fr] items-start gap-x-3 gap-y-2 text-sm">
                  <dt className="text-muted-foreground">Когда</dt>
                  <dd>
                    <WhenFields
                      draft={row.draft}
                      options={options}
                      onUpdate={onUpdate}
                      describedBy={errorId}
                    />
                  </dd>
                  <dt className="text-muted-foreground">Клиент</dt>
                  <dd>
                    <NameField
                      draft={row.draft}
                      options={options}
                      onUpdate={onUpdate}
                      describedBy={errorId}
                    />
                  </dd>
                  <dt className="text-muted-foreground">Телефон</dt>
                  <dd>
                    <PhoneCell
                      draft={row.draft}
                      options={options}
                      onUpdate={onUpdate}
                      describedBy={errorId}
                    />
                  </dd>
                  <dt className="text-muted-foreground">Услуга</dt>
                  <dd>
                    <ServiceCell
                      draft={row.draft}
                      options={options}
                      onUpdate={onUpdate}
                      describedBy={errorId}
                    />
                  </dd>
                  <dt className="text-muted-foreground">Салон</dt>
                  <dd>
                    <SalonCell
                      draft={row.draft}
                      options={options}
                      onUpdate={onUpdate}
                      describedBy={errorId}
                    />
                  </dd>
                  <dt className="text-muted-foreground">Цена</dt>
                  <dd>
                    <PriceCell
                      draft={row.draft}
                      options={options}
                      onUpdate={onUpdate}
                      describedBy={errorId}
                    />
                  </dd>
                </dl>
              </li>
            )
          })}
        </ul>
      )}

      {progress !== null && (
        <p role="status" className="text-sm text-muted-foreground">
          Обработано {progress.done} из {progress.total}…
        </p>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          size="sm"
          className="h-11 sm:h-8"
          aria-busy={isRunning}
          disabled={isRunning || summary.ready === 0}
          onClick={onCreate}
        >
          {`Создать ${formatBookingsCount(summary.ready)}`}
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-11 sm:h-8"
          disabled={isRunning}
          onClick={onBack}
        >
          Изменить список
        </Button>
      </div>
    </section>
  )
}
