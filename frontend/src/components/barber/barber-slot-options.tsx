import { Check } from 'lucide-react'
import { useId } from 'react'

import { useI18n } from '@/i18n'
import type { BarberSlot, BarberSlots } from '@/lib/barber-slots'
import { formatTime } from '@/lib/format'

/**
 * Свободное время: чипы без выбора либо нативные радиокнопки.
 *
 * Компонент общий для публичной карточки и формы «Записать клиента». Пока услуга
 * не выбрана, слоты только показывают время; с выбранной услугой каждый слот —
 * радиокнопка, и снимок ответа `/slots` остаётся неизменным до отправки.
 * `name` разводит группы, если на странице когда-нибудь окажутся две формы.
 */
export function BarberSlotOptions({
  slots,
  selectable,
  selectedStartAt,
  onSelect,
  name = 'barber-slot',
  legend,
}: {
  slots: BarberSlots
  selectable: boolean
  selectedStartAt: string | null
  onSelect: (slot: BarberSlot) => void
  name?: string
  /** Готовая подпись группы; диалог переноса передаёт «Новое время». */
  legend?: string
}) {
  const { t } = useI18n()
  const groupId = useId().replace(/:/g, '')
  const legendText = legend ?? t('Время приёма')

  if (!slots.isWorkingDay) {
    return (
      <p className="text-sm text-muted-foreground">
        {t('В этот день барбер не работает.')}
      </p>
    )
  }

  if (slots.slots.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        {t('Свободного времени на этот день нет.')}
      </p>
    )
  }

  if (!selectable) {
    return (
      <ul className="flex flex-wrap gap-2">
        {slots.slots.map((slot) => (
          <li key={slot.startAt}>
            <span className="inline-flex h-9 items-center rounded-lg bg-muted px-3 text-sm text-foreground tabular-nums">
              <time dateTime={slot.startAt}>
                {formatTime(slot.startAt, slots.timeZoneId)}
              </time>
            </span>
          </li>
        ))}
      </ul>
    )
  }

  return (
    <fieldset>
      <legend className="sr-only">{legendText}</legend>
      <div className="flex flex-wrap gap-2">
        {slots.slots.map((slot, index) => {
          const inputId = `${groupId}-slot-${index}`
          const isSelected = selectedStartAt === slot.startAt

          return (
            <div key={slot.startAt} className="relative">
              <input
                id={inputId}
                type="radio"
                name={name}
                value={slot.startAt}
                checked={isSelected}
                className="peer sr-only"
                onChange={() => onSelect(slot)}
              />
              <label
                htmlFor={inputId}
                className="inline-flex h-11 cursor-pointer items-center gap-1.5 rounded-lg border border-transparent bg-muted px-3 text-sm text-foreground tabular-nums transition-colors select-none hover:bg-muted/70 peer-checked:border-primary peer-checked:bg-primary peer-checked:font-medium peer-checked:text-primary-foreground peer-checked:hover:bg-primary peer-focus-visible:ring-3 peer-focus-visible:ring-ring/50 sm:h-9"
              >
                <time dateTime={slot.startAt}>
                  {formatTime(slot.startAt, slots.timeZoneId)}
                </time>
                {isSelected && <Check className="size-3.5" aria-hidden />}
              </label>
            </div>
          )
        })}
      </div>
    </fieldset>
  )
}
