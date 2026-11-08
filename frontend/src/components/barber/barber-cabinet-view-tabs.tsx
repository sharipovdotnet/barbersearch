import { useRef, type KeyboardEvent } from 'react'

import {
  BARBER_CABINET_PANEL_ID,
  BARBER_CABINET_VIEWS,
  barberViewTabId,
  type BarberCabinetView,
} from '@/lib/barber-cabinet-view'
import { cn } from '@/lib/utils'
import { useI18n } from '@/i18n'

/**
 * Один `tablist` на три вкладки.
 *
 * Живёт на уровне страницы, а не внутри списка: иначе у дня и у списка было бы по
 * своему переключателю, и `aria-controls` указывал бы на разные панели. Стрелки,
 * Home/End, roving `tabIndex` — по паттерну APG; выбор переезжает вместе с фокусом.
 */
export function BarberCabinetViewTabs({
  view,
  onChange,
}: {
  view: BarberCabinetView
  onChange: (view: BarberCabinetView) => void
}) {
  const { t } = useI18n()
  const refs = useRef<Record<BarberCabinetView, HTMLButtonElement | null>>({
    day: null,
    upcoming: null,
    past: null,
  })

  function select(next: BarberCabinetView) {
    if (next !== view) {
      onChange(next)
    }

    refs.current[next]?.focus()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    const index = BARBER_CABINET_VIEWS.findIndex((item) => item.id === view)
    const last = BARBER_CABINET_VIEWS.length - 1
    let nextIndex: number

    if (event.key === 'ArrowLeft') {
      nextIndex = index === 0 ? last : index - 1
    } else if (event.key === 'ArrowRight') {
      nextIndex = index === last ? 0 : index + 1
    } else if (event.key === 'Home') {
      nextIndex = 0
    } else if (event.key === 'End') {
      nextIndex = last
    } else {
      return
    }

    event.preventDefault()
    select(BARBER_CABINET_VIEWS[nextIndex].id)
  }

  return (
    <div
      role="tablist"
      aria-label={t('Какие записи показать')}
      className="inline-flex items-center gap-0.5 rounded-lg bg-muted p-0.5"
    >
      {BARBER_CABINET_VIEWS.map((item) => {
        const isSelected = item.id === view

        return (
          <button
            key={item.id}
            ref={(element) => {
              refs.current[item.id] = element
            }}
            type="button"
            role="tab"
            id={barberViewTabId(item.id)}
            aria-selected={isSelected}
            aria-controls={BARBER_CABINET_PANEL_ID}
            tabIndex={isSelected ? 0 : -1}
            onClick={() => select(item.id)}
            onKeyDown={handleKeyDown}
            className={cn(
              'inline-flex h-11 items-center rounded-md px-2.5 text-sm whitespace-nowrap transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-7 sm:px-3',
              isSelected
                ? 'bg-background font-medium text-foreground shadow-sm ring-1 ring-foreground/10'
                : 'text-muted-foreground hover:text-foreground'
            )}
          >
            {t(item.label)}
          </button>
        )
      })}
    </div>
  )
}
