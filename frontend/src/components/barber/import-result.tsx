import { useEffect, useRef } from 'react'

import { Button } from '@/components/ui/button'
import type { ImportRow, ImportSummary } from '@/hooks/use-barber-import'
import type { BarberBookingOptions } from '@/lib/barber-booking-options'
import type { BarberSlot } from '@/lib/barber-slots'
import { formatShortDay, formatTime } from '@/lib/format'

/**
 * Шаг 3: отчёт о переносе.
 *
 * Числа — первое, что видит барбер; ниже только те строки, которые не создались,
 * с причиной и ближайшим свободным временем. Повтор идёт теми же ключами
 * идемпотентности, поэтому уже созданная запись дублем не станет.
 */

function moment(date: string | null, time: string | null): string {
  const day = date === null ? 'без даты' : formatShortDay(`${date}T00:00:00Z`, 'UTC')

  return time === null ? day : `${day}, ${time}`
}

function SlotChoices({
  row,
  options,
  onApplySlot,
  onLoadSlots,
}: {
  row: ImportRow
  options: BarberBookingOptions
  onApplySlot: (line: number, slot: BarberSlot) => void
  onLoadSlots: (line: number) => void
}) {
  const salon = options.salons.find((item) => item.salonId === row.draft.salonId)

  if (row.slotsStatus === 'loading') {
    return (
      <p role="status" className="text-sm text-muted-foreground">
        Ищем свободное время…
      </p>
    )
  }

  if (row.slotsStatus === 'error') {
    return (
      <p role="alert" className="text-sm text-destructive">
        Не удалось загрузить свободное время.{' '}
        <button
          type="button"
          className="underline underline-offset-2"
          onClick={() => {
            onLoadSlots(row.draft.line)
          }}
        >
          Повторить
        </button>
      </p>
    )
  }

  if (row.slots === null) {
    return (
      <Button
        type="button"
        variant="outline"
        size="sm"
        className="h-11 sm:h-8"
        onClick={() => {
          onLoadSlots(row.draft.line)
        }}
      >
        Ближайшее время
      </Button>
    )
  }

  if (row.slots.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        Свободного времени в этот день нет.
      </p>
    )
  }

  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="text-sm text-muted-foreground">Ближайшее:</span>
      {row.slots.map((slot) => (
        <Button
          key={slot.startAt}
          type="button"
          variant="outline"
          size="sm"
          className="h-11 sm:h-8"
          onClick={() => {
            onApplySlot(row.draft.line, slot)
          }}
        >
          {salon === undefined
            ? formatTime(slot.startAt)
            : formatTime(slot.startAt, salon.timeZoneId)}
        </Button>
      ))}
    </div>
  )
}

export function ImportResult({
  rows,
  summary,
  options,
  isRunning,
  onRetryFailed,
  onApplySlot,
  onLoadSlots,
  onOpenBookings,
}: {
  rows: ImportRow[]
  summary: ImportSummary
  options: BarberBookingOptions
  isRunning: boolean
  onRetryFailed: () => void
  onApplySlot: (line: number, slot: BarberSlot) => void
  onLoadSlots: (line: number) => void
  onOpenBookings: () => void
}) {
  const headingRef = useRef<HTMLHeadingElement>(null)
  const failed = rows.filter((row) => row.status === 'failed')

  useEffect(() => {
    headingRef.current?.focus({ preventScroll: true })
  }, [])

  return (
    <section aria-labelledby="import-result-title" className="space-y-4">
      <h3
        id="import-result-title"
        ref={headingRef}
        tabIndex={-1}
        className="text-lg font-medium outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
      >
        Отчёт о переносе
      </h3>

      <p role="status" className="text-base">
        {`Перенесено ${summary.created} из ${summary.total} записей.`}
      </p>

      {failed.length > 0 && (
        <div className="rounded-xl border bg-card p-4">
          <h4 className="mb-2 text-sm font-medium">Не удалось создать</h4>
          <ul className="space-y-3">
            {failed.map((row) => (
              <li
                key={row.draft.line}
                className="space-y-1 border-t pt-3 first:border-t-0 first:pt-0"
              >
                <p className="text-sm">
                  <span className="text-muted-foreground">
                    Строка {row.draft.line}
                  </span>
                  {' · '}
                  {row.draft.firstName ?? 'Без имени'}
                  {' · '}
                  {moment(row.draft.date, row.draft.time)}
                </p>
                <p className="text-sm text-destructive">{row.failure}</p>
                {row.overlap && (
                  <SlotChoices
                    row={row}
                    options={options}
                    onApplySlot={onApplySlot}
                    onLoadSlots={onLoadSlots}
                  />
                )}
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2">
        {failed.length > 0 && (
          <Button
            type="button"
            size="sm"
            className="h-11 sm:h-8"
            aria-busy={isRunning}
            disabled={isRunning}
            onClick={onRetryFailed}
          >
            Повторить неудачные
          </Button>
        )}
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-11 sm:h-8"
          disabled={isRunning}
          onClick={onOpenBookings}
        >
          Открыть записи
        </Button>
      </div>
    </section>
  )
}
