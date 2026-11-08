import { useEffect, useRef } from 'react'

import { Button } from '@/components/ui/button'
import type { ImportRow, ImportSummary } from '@/hooks/use-barber-import'
import type { BarberBookingOptions } from '@/lib/barber-booking-options'
import type { BarberSlot } from '@/lib/barber-slots'
import { getLocale, t, tp } from '@/i18n'
import { formatShortDay, formatTime } from '@/lib/format'
import type { ImportSource } from '@/lib/import-parse'

/**
 * Шаг 3: отчёт о переносе.
 *
 * Числа — первое, что видит барбер; ниже только те строки, которые не создались,
 * с причиной и ближайшим свободным временем. Повтор идёт теми же ключами
 * идемпотентности, поэтому уже созданная запись дублем не станет.
 *
 * Для календаря отчёт честно говорит, сколько выбрано и сколько строк ещё требуют
 * внимания: иначе барбер решит, что невыбранные события потерялись. «Перенести ещё»
 * возвращает в предпросмотр без повторного чтения файла.
 */

function attentionText(count: number): string {
  const rows = tp('rows', count)
  const singular = new Intl.PluralRules(getLocale()).select(count) === 'one'

  return singular
    ? t('{rows} требует внимания.', { rows })
    : t('{rows} требуют внимания.', { rows })
}

function moment(date: string | null, time: string | null): string {
  const day =
    date === null
      ? t('без даты')
      : formatShortDay(`${date}T00:00:00Z`, 'UTC')

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
        {t('Ищем свободное время…')}
      </p>
    )
  }

  if (row.slotsStatus === 'error') {
    return (
      <p role="alert" className="text-sm text-destructive">
        {t('Не удалось загрузить свободное время.')}{' '}
        <button
          type="button"
          className="underline underline-offset-2"
          onClick={() => {
            onLoadSlots(row.draft.line)
          }}
        >
          {t('Повторить')}
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
        {t('Ближайшее время')}
      </Button>
    )
  }

  if (row.slots.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        {t('Свободного времени в этот день нет.')}
      </p>
    )
  }

  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="text-sm text-muted-foreground">
        {t('Ближайшее:')}
      </span>
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
  source,
  canContinue,
  isRunning,
  onRetryFailed,
  onContinue,
  onReview,
  onApplySlot,
  onLoadSlots,
  onOpenBookings,
}: {
  rows: ImportRow[]
  summary: ImportSummary
  options: BarberBookingOptions
  source: ImportSource
  /** Есть не созданные строки: отчёт предлагает «Перенести ещё». */
  canContinue: boolean
  isRunning: boolean
  onRetryFailed: () => void
  onContinue: () => void
  onReview: () => void
  onApplySlot: (line: number, slot: BarberSlot) => void
  onLoadSlots: (line: number) => void
  onOpenBookings: () => void
}) {
  const headingRef = useRef<HTMLHeadingElement>(null)
  const failed = rows.filter((row) => row.status === 'failed')
  const selected = rows.filter((row) => row.included).length
  // После «Перенести ещё» созданные строки прошлой пачки сняты с выбора: отчёт
  // календаря говорит о текущей пачке, а не о кумулятивном `summary.created`.
  const createdNow = rows.filter(
    (row) => row.included && row.status === 'created'
  ).length
  const attention = rows.filter(
    (row) =>
      row.included
      && row.status !== 'created'
      && row.draft.issues.length > 0
  ).length
  const report =
    source === 'ics'
      ? t('Перенесено {created} из {selected} выбранных записей.', {
          created: createdNow,
          selected,
        })
      : t('Перенесено {created} из {total} записей.', {
          created: summary.created,
          total: summary.total,
        })

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
        {t('Отчёт о переносе')}
      </h3>

      <p role="status" className="text-base">
        {report}
      </p>

      {source === 'ics' && (
        <p className="text-sm text-muted-foreground">
          {t('Повторная загрузка этого файла не создаст дублей.')}
        </p>
      )}

      {attention > 0 && (
        <div className="flex flex-wrap items-center gap-2">
          <p className="text-sm">{attentionText(attention)}</p>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            disabled={isRunning}
            onClick={onReview}
          >
            {t('Проверить строки')}
          </Button>
        </div>
      )}

      {failed.length > 0 && (
        <div className="rounded-xl border bg-card p-4">
          <h4 className="mb-2 text-sm font-medium">
            {t('Не удалось создать')}
          </h4>
          <ul className="space-y-3">
            {failed.map((row) => (
              <li
                key={row.draft.line}
                className="space-y-1 border-t pt-3 first:border-t-0 first:pt-0"
              >
                <p className="text-sm">
                  <span className="text-muted-foreground">
                    {t('Строка {line}', { line: row.draft.line })}
                  </span>
                  {' · '}
                  {row.draft.firstName ?? t('Без имени')}
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
            {t('Повторить неудачные')}
          </Button>
        )}
        {canContinue && (
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            disabled={isRunning}
            onClick={onContinue}
          >
            {t('Перенести ещё')}
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
          {t('Открыть записи')}
        </Button>
      </div>
    </section>
  )
}
