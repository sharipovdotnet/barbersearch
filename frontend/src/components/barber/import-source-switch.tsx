import { CalendarDays, List } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

import { FieldLegend, FieldSet } from '@/components/ui/field'
import { useI18n } from '@/i18n'
import type { ImportSource } from '@/lib/import-parse'
import { cn } from '@/lib/utils'

/**
 * Шаг 1: откуда переносить записи.
 *
 * Нативные радио в `fieldset`/`legend`: стрелки, Tab и озвучивание группы
 * работают от браузера, а карточка вокруг — только оформление. Никаких
 * `aria-checked` на div: состояние живёт в самом input.
 */

const OPTIONS: readonly {
  value: ImportSource
  label: string
  icon: LucideIcon
}[] = [
  { value: 'ics', label: 'Файл календаря', icon: CalendarDays },
  { value: 'text', label: 'Список текстом', icon: List },
]

export function ImportSourceSwitch({
  value,
  onChange,
}: {
  value: ImportSource
  onChange: (value: ImportSource) => void
}) {
  const { t } = useI18n()

  return (
    <FieldSet>
      <FieldLegend>{t('Откуда перенести')}</FieldLegend>
      <div className="grid gap-2 sm:grid-cols-2">
        {OPTIONS.map((option) => {
          const Icon = option.icon
          const checked = option.value === value

          return (
            <label
              key={option.value}
              className={cn(
                'flex min-h-11 cursor-pointer items-center gap-2.5 rounded-xl border bg-card px-3 py-2.5 transition-colors focus-within:border-ring focus-within:ring-3 focus-within:ring-ring/50 hover:bg-muted/40',
                checked && 'border-primary/40 bg-primary/5 hover:bg-primary/5'
              )}
            >
              <input
                type="radio"
                name="import-source"
                value={option.value}
                checked={checked}
                className="size-4 shrink-0 accent-primary"
                onChange={() => {
                  onChange(option.value)
                }}
              />
              <span
                className={cn(
                  'flex size-8 shrink-0 items-center justify-center rounded-full bg-muted text-muted-foreground',
                  checked && 'bg-primary/10 text-primary'
                )}
                aria-hidden
              >
                <Icon className="size-4" />
              </span>
              <span className="text-sm font-medium">
                {t(option.label)}
              </span>
            </label>
          )
        })}
      </div>
    </FieldSet>
  )
}
