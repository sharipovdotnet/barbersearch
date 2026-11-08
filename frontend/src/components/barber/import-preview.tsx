import { useCallback, useEffect, useRef, useState } from 'react'

import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { useMediaQuery } from '@/hooks/use-media-query'
import type { ImportRow, ImportSummary } from '@/hooks/use-barber-import'
import type { BarberBookingOptions } from '@/lib/barber-booking-options'
import {
  formatBookingsCount,
  formatPrice,
  formatShortDay,
} from '@/lib/format'
import {
  findOverlapPartner,
  type ImportDraft,
  type ImportIssue,
  type ImportRowPatch,
  type ImportSource,
} from '@/lib/import-parse'
import { salonDateFromInstant, salonTimeFromInstant } from '@/lib/salon-time'
import { cn } from '@/lib/utils'

/**
 * Шаг 2: проверка разобранного списка.
 *
 * Десктоп — настоящая таблица, телефон — карточки с теми же полями: горизонтального
 * скролла нет ни на одной ширине. Всё, что не понято, подсвечено и правится на месте;
 * сводка сверху называет проблемные строки и уводит фокус в первую из них.
 *
 * Строки календаря отличаются тремя вещами: у них есть галочка «Перенести», мягкие
 * подсказки разбора и кнопка «Изменить», раскрывающая поля даже у готовой строки —
 * телефон из описания может быть чужим, а формат при этом валидным.
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
  overlap: 'Пересекается с записью.',
  tooFar: 'Слишком далеко: запись можно создать не более чем на 90 дней вперёд.',
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

/** Название события для доступного имени чекбокса. */
function eventLabel(draft: ImportDraft): string {
  const summary = (draft.eventSummary ?? draft.raw).trim()

  return summary === '' ? 'Без названия' : summary
}

function overlapPartnerName(draft: ImportDraft): string {
  return draft.firstName ?? draft.eventSummary ?? draft.raw
}

function stateText(
  row: ImportRow,
  rows: readonly ImportRow[],
  includedLines: ReadonlySet<number>
): string {
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

  if (first === undefined) {
    return 'Готово'
  }

  if (first === 'overlap') {
    const partner = findOverlapPartner(
      rows.map((item) => item.draft),
      row.draft.line,
      includedLines
    )

    return partner === null
      ? ISSUE_TEXT.overlap
      : `Пересекается с записью «${overlapPartnerName(partner)}»`
  }

  return ISSUE_TEXT[first]
}

function dateLabel(date: string | null): string {
  return date === null ? '—' : formatShortDay(`${date}T00:00:00Z`, 'UTC')
}

function salonTimeZone(
  options: BarberBookingOptions,
  draft: ImportDraft
): string {
  const salon = options.salons.find((item) => item.salonId === draft.salonId)

  return salon?.timeZoneId ?? options.salons[0]?.timeZoneId ?? 'UTC'
}

/**
 * «12 ноя, 10:00–10:45»; конец на следующей календарной дате называется словами,
 * потому что «10:00–01:00» читалось бы как время назад.
 */
function icsWhenLabel(draft: ImportDraft, timeZoneId: string): string {
  const day = dateLabel(draft.date)
  const start = draft.time ?? '—'

  if (draft.endAt === null) {
    return `${day}, ${start}`
  }

  const end = salonTimeFromInstant(draft.endAt, timeZoneId)
  const endDate = salonDateFromInstant(draft.endAt, timeZoneId)

  return endDate === draft.date
    ? `${day}, ${start}–${end}`
    : `${day}, ${start} до ${end} следующего дня`
}

/**
 * Мягкие подсказки календаря: не ошибки, поэтому не красные и не блокируют строку.
 * Показываются рядом с состоянием — на телефоне это единственное место, где они
 * не отнимают ширину у полей.
 */
function hintLines(
  draft: ImportDraft,
  options: BarberBookingOptions
): string[] {
  const service = options.services.find(
    (item) => item.barberServiceId === draft.serviceId
  )
  const lines: string[] = []

  for (const hint of draft.hints) {
    switch (hint) {
      case 'personal':
        lines.push('Похоже на личное событие')
        break
      case 'free':
        lines.push('Событие отмечено как «свободно»')
        break
      case 'tentative':
        lines.push('Предварительная запись')
        break
      case 'noContact':
        lines.push('Нет телефона и услуги — проверьте')
        break
      case 'salonFallback':
        lines.push('Салон не найден — выбран основной')
        break
      case 'timeZoneConverted':
        if (draft.sourceTimeZone !== null) {
          lines.push(`Время пересчитано из ${draft.sourceTimeZone}`)
        }
        break
      case 'durationMismatch':
        if (draft.eventDurationMinutes !== null && service !== undefined) {
          lines.push(
            `Длительность ${draft.eventDurationMinutes} мин из календаря, услуга ${service.durationMinutes} мин`
          )
        }
        break
      case 'phoneFromDescription':
        lines.push('Телефон найден в описании — проверьте')
        break
      case 'nameTruncated':
        lines.push('Имя сокращено до 100 символов')
        break
      case 'phoneTruncated':
        lines.push('Телефон сокращён до 32 символов')
        break
    }
  }

  return lines
}

type SummaryPart = {
  key: string
  text: string
  /** Проблема, к первой строке которой ведёт число; `null` — просто счётчик. */
  issue: ImportIssue | null
}

function summaryParts(
  summary: ImportSummary,
  source: ImportSource
): SummaryPart[] {
  if (source === 'text') {
    const parts: SummaryPart[] = [
      {
        key: 'ready',
        text: `Готово ${summary.ready} из ${summary.total}`,
        issue: null,
      },
    ]

    if (summary.missingService > 0) {
      parts.push({
        key: 'service',
        text: `${summary.missingService} без услуги`,
        issue: 'noService',
      })
    }

    if (summary.past > 0) {
      parts.push({
        key: 'past',
        text: `${summary.past} в прошлом`,
        issue: 'past',
      })
    }

    return parts
  }

  const parts: SummaryPart[] = [
    {
      key: 'selected',
      text: `Выбрано ${summary.selected} из ${summary.total}`,
      issue: null,
    },
  ]

  if (summary.ready > 0) {
    parts.push({
      key: 'ready',
      text: `Готово ${summary.ready}`,
      issue: null,
    })
  }

  if (summary.missingService > 0) {
    parts.push({
      key: 'service',
      text: `${summary.missingService} без услуги`,
      issue: 'noService',
    })
  }

  if (summary.missingPhone > 0) {
    parts.push({
      key: 'phone',
      text: `${summary.missingPhone} без телефона`,
      issue: 'noPhone',
    })
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

type RowAppearance = {
  /** Календарь: строку раскрыли кнопкой «Изменить», даже если ошибок нет. */
  expanded: boolean
  source: ImportSource
  /** Пояс салона строки: конец события показывается в нём. */
  timeZoneId: string
}

function WhenFields({
  draft,
  onUpdate,
  describedBy,
  source,
  timeZoneId,
  expanded,
}: RowParts & RowAppearance) {
  const isIcs = source === 'ics'
  const dateInvalid = hasIssue(draft, 'noDate') || hasIssue(draft, 'badDate')
  const timeInvalid =
    hasIssue(draft, 'noTime')
    || hasIssue(draft, 'badTime')
    || hasIssue(draft, 'past')
    || hasIssue(draft, 'duplicate')
    // Конфликт снимается сдвигом времени, а не только снятием галочки.
    || (isIcs && (hasIssue(draft, 'overlap') || hasIssue(draft, 'tooFar')))
  const needsDate = expanded || dateInvalid
  const needsTime = expanded || timeInvalid

  if (!needsDate && !needsTime) {
    return (
      <span className={isIcs ? 'sm:whitespace-nowrap' : 'whitespace-nowrap'}>
        {isIcs
          ? icsWhenLabel(draft, timeZoneId)
          : `${dateLabel(draft.date)}, ${draft.time ?? '—'}`}
      </span>
    )
  }

  return (
    <div className="flex flex-col gap-1">
      {needsDate && (
        <Input
          type="date"
          aria-label={`Дата, строка ${draft.line}`}
          aria-invalid={dateInvalid}
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
          aria-invalid={timeInvalid}
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

function NameField({
  draft,
  onUpdate,
  describedBy,
  expanded,
}: RowParts & Pick<RowAppearance, 'expanded'>) {
  if (!expanded && !hasIssue(draft, 'noName')) {
    return <span>{draft.firstName ?? '—'}</span>
  }

  return (
    <Input
      aria-label={`Имя, строка ${draft.line}`}
      aria-invalid={hasIssue(draft, 'noName')}
      aria-describedby={describedBy}
      value={draft.firstName ?? ''}
      className="h-11 sm:h-8"
      onChange={(event) => {
        onUpdate(draft.line, { firstName: event.target.value || null })
      }}
    />
  )
}

function PhoneCell({
  draft,
  onUpdate,
  describedBy,
  expanded,
}: RowParts & Pick<RowAppearance, 'expanded'>) {
  const invalid = hasIssue(draft, 'noPhone') || hasIssue(draft, 'badPhone')

  if (!expanded && !invalid) {
    return <span className="whitespace-nowrap">{draft.phone ?? '—'}</span>
  }

  return (
    <PhoneField
      draft={draft}
      onUpdate={onUpdate}
      label={`Телефон, строка ${draft.line}`}
      describedBy={describedBy}
      invalid={invalid}
    />
  )
}

function ServiceCell({
  draft,
  options,
  onUpdate,
  describedBy,
  expanded,
}: RowParts & Pick<RowAppearance, 'expanded'>) {
  const invalid =
    hasIssue(draft, 'noService') || hasIssue(draft, 'ambiguousService')

  if (!expanded && !invalid) {
    const service = options.services.find(
      (item) => item.barberServiceId === draft.serviceId
    )

    return <span>{service?.name ?? '—'}</span>
  }

  return (
    <ServiceField
      draft={draft}
      options={options}
      label={`Услуга, строка ${draft.line}`}
      describedBy={describedBy}
      invalid={invalid}
      onUpdate={onUpdate}
    />
  )
}

function SalonCell({
  draft,
  options,
  onUpdate,
  describedBy,
  expanded,
}: RowParts & Pick<RowAppearance, 'expanded'>) {
  const salon = options.salons.find((item) => item.salonId === draft.salonId)

  if (!expanded && options.salons.length <= 1) {
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

/** Подсказки строки: маленький список под состоянием, на телефоне — в шапке карточки. */
function HintList({ lines }: { lines: string[] }) {
  if (lines.length === 0) {
    return null
  }

  return (
    <ul className="mt-1 space-y-0.5 text-xs text-muted-foreground">
      {lines.map((line) => (
        <li key={line}>{line}</li>
      ))}
    </ul>
  )
}

export function ImportPreview({
  rows,
  summary,
  options,
  source,
  selectionNotice,
  isRunning,
  progress,
  onUpdate,
  onToggle,
  onToggleAll,
  onCreate,
  onBack,
}: {
  rows: ImportRow[]
  summary: ImportSummary
  options: BarberBookingOptions
  source: ImportSource
  /** «За один раз — не больше 50 записей.» после 51-й галочки. */
  selectionNotice: string | null
  isRunning: boolean
  progress: { done: number; total: number } | null
  onUpdate: (line: number, patch: ImportRowPatch) => void
  onToggle: (line: number) => void
  onToggleAll: (included: boolean) => void
  onCreate: () => void
  onBack: () => void
}) {
  const isWide = useMediaQuery('(min-width: 640px)')
  const isIcs = source === 'ics'
  const summaryRef = useRef<HTMLParagraphElement>(null)
  const rowRefs = useRef(new Map<number, HTMLElement>())
  const [editingLine, setEditingLine] = useState<number | null>(null)

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

  function toggleEditing(line: number) {
    setEditingLine((current) => (current === line ? null : line))
  }

  const parts = summaryParts(summary, source)
  const includedLines = new Set(
    rows.filter((row) => row.included).map((row) => row.draft.line)
  )

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
        {parts.map((part, index) => {
          const issue = part.issue

          return (
            <span key={part.key} role={index === 0 ? 'status' : undefined}>
              {index > 0 && <span>{' · '}</span>}
              {issue === null ? (
                part.text
              ) : (
                <button
                  type="button"
                  className="underline underline-offset-2"
                  onClick={() => {
                    focusFirst(issue)
                  }}
                >
                  {part.text}
                </button>
              )}
            </span>
          )
        })}
      </p>

      {isIcs && (
        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            disabled={isRunning}
            onClick={() => {
              onToggleAll(true)
            }}
          >
            Выбрать все
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            disabled={isRunning}
            onClick={() => {
              onToggleAll(false)
            }}
          >
            Снять все
          </Button>
        </div>
      )}

      {isIcs && selectionNotice !== null && (
        <p role="status" className="text-sm text-muted-foreground">
          {selectionNotice}
        </p>
      )}

      {isWide ? (
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-sm">
            <caption className="sr-only">Разобранные записи</caption>
            <thead>
              <tr className="border-b text-left text-muted-foreground">
                {isIcs && (
                  <th scope="col" className="sr-only">
                    Перенести
                  </th>
                )}
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
                const expanded =
                  isIcs
                  && row.status !== 'created'
                  && editingLine === row.draft.line
                const hints = isIcs
                  ? hintLines(row.draft, options)
                  : []
                const timeZoneId = salonTimeZone(options, row.draft)

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
                    {isIcs && (
                      <td className="py-2 pr-2">
                        <Checkbox
                          checked={row.included}
                          disabled={row.status === 'created'}
                          aria-label={`Перенести событие «${eventLabel(row.draft)}»`}
                          onCheckedChange={() => {
                            onToggle(row.draft.line)
                          }}
                        />
                      </td>
                    )}
                    <td className="py-2 pr-2 text-muted-foreground">
                      {row.draft.line}
                    </td>
                    <td className="py-2 pr-2">
                      <WhenFields
                        draft={row.draft}
                        options={options}
                        onUpdate={onUpdate}
                        describedBy={errorId}
                        source={source}
                        timeZoneId={timeZoneId}
                        expanded={expanded}
                      />
                    </td>
                    <td className="py-2 pr-2">
                      <NameField
                        draft={row.draft}
                        options={options}
                        onUpdate={onUpdate}
                        describedBy={errorId}
                        expanded={expanded}
                      />
                    </td>
                    <td className="py-2 pr-2">
                      <PhoneCell
                        draft={row.draft}
                        options={options}
                        onUpdate={onUpdate}
                        describedBy={errorId}
                        expanded={expanded}
                      />
                    </td>
                    <td className="py-2 pr-2">
                      <ServiceCell
                        draft={row.draft}
                        options={options}
                        onUpdate={onUpdate}
                        describedBy={errorId}
                        expanded={expanded}
                      />
                    </td>
                    <td className="py-2 pr-2">
                      <SalonCell
                        draft={row.draft}
                        options={options}
                        onUpdate={onUpdate}
                        describedBy={errorId}
                        expanded={expanded}
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
                        {stateText(row, rows, includedLines)}
                      </span>
                      {isIcs && <HintList lines={hints} />}
                      {isIcs && row.status !== 'created' && (
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          className="mt-1 h-11 px-0 sm:h-8"
                          aria-expanded={expanded}
                          onClick={() => {
                            toggleEditing(row.draft.line)
                          }}
                        >
                          Изменить
                        </Button>
                      )}
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
            const expanded =
              isIcs
              && row.status !== 'created'
              && editingLine === row.draft.line
            const hints = isIcs ? hintLines(row.draft, options) : []
            const timeZoneId = salonTimeZone(options, row.draft)

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
                <div className="flex items-start gap-2">
                  {isIcs && (
                    <Checkbox
                      className="mt-0.5"
                      checked={row.included}
                      disabled={row.status === 'created'}
                      aria-label={`Перенести событие «${eventLabel(row.draft)}»`}
                      onCheckedChange={() => {
                        onToggle(row.draft.line)
                      }}
                    />
                  )}
                  <div className="flex min-w-0 flex-1 items-start justify-between gap-2">
                    <p className="text-sm font-medium">Строка {row.draft.line}</p>
                    <div className="text-right">
                      <span
                        id={errorId}
                        className={cn(
                          'block text-sm',
                          invalid
                            ? 'text-destructive'
                            : 'text-muted-foreground'
                        )}
                      >
                        {stateText(row, rows, includedLines)}
                      </span>
                      {isIcs && row.status !== 'created' && (
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          className="mt-1 h-11 px-0 sm:h-8"
                          aria-expanded={expanded}
                          onClick={() => {
                            toggleEditing(row.draft.line)
                          }}
                        >
                          Изменить
                        </Button>
                      )}
                    </div>
                  </div>
                </div>
                {isIcs && <HintList lines={hints} />}
                <dl className="grid grid-cols-[5.5rem_1fr] items-start gap-x-3 gap-y-2 text-sm">
                  <dt className="text-muted-foreground">Когда</dt>
                  <dd>
                    <WhenFields
                      draft={row.draft}
                      options={options}
                      onUpdate={onUpdate}
                      describedBy={errorId}
                      source={source}
                      timeZoneId={timeZoneId}
                      expanded={expanded}
                    />
                  </dd>
                  <dt className="text-muted-foreground">Клиент</dt>
                  <dd>
                    <NameField
                      draft={row.draft}
                      options={options}
                      onUpdate={onUpdate}
                      describedBy={errorId}
                      expanded={expanded}
                    />
                  </dd>
                  <dt className="text-muted-foreground">Телефон</dt>
                  <dd>
                    <PhoneCell
                      draft={row.draft}
                      options={options}
                      onUpdate={onUpdate}
                      describedBy={errorId}
                      expanded={expanded}
                    />
                  </dd>
                  <dt className="text-muted-foreground">Услуга</dt>
                  <dd>
                    <ServiceCell
                      draft={row.draft}
                      options={options}
                      onUpdate={onUpdate}
                      describedBy={errorId}
                      expanded={expanded}
                    />
                  </dd>
                  <dt className="text-muted-foreground">Салон</dt>
                  <dd>
                    <SalonCell
                      draft={row.draft}
                      options={options}
                      onUpdate={onUpdate}
                      describedBy={errorId}
                      expanded={expanded}
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
          {isIcs ? 'Изменить файл' : 'Изменить список'}
        </Button>
        {isIcs && (
          <p className="w-full text-xs text-muted-foreground sm:w-auto">
            Правки строк при возврате к файлу потеряются.
          </p>
        )}
      </div>
    </section>
  )
}
