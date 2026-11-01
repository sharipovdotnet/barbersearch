import { useRef, type KeyboardEvent } from 'react'

import {
  ADMIN_CATALOGUE_TAB_LABELS,
  type CatalogueTab,
} from '@/lib/admin-catalogue'
import { cn } from '@/lib/utils'

const TABS: readonly { id: CatalogueTab; label: string }[] = [
  { id: 'services', label: ADMIN_CATALOGUE_TAB_LABELS.services },
  { id: 'specializations', label: ADMIN_CATALOGUE_TAB_LABELS.specializations },
  { id: 'salons', label: ADMIN_CATALOGUE_TAB_LABELS.salons },
]

/**
 * Вкладки каталога: какой справочник показать.
 *
 * Счётчиков нет: API не отдаёт общее число строк, а цифра на вкладке была бы выдумкой.
 * Паттерн — ручной roving tabindex, как у вкладок отзывов: ←/→ переключают вкладку,
 * Home/End прыгают к первой и последней.
 */
export function CatalogueTabs({
  tab,
  onChange,
}: {
  tab: CatalogueTab
  onChange: (tab: CatalogueTab) => void
}) {
  const tabRefs = useRef<Record<CatalogueTab, HTMLButtonElement | null>>({
    services: null,
    specializations: null,
    salons: null,
  })

  function moveTo(next: CatalogueTab) {
    onChange(next)
    tabRefs.current[next]?.focus()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    const index = TABS.findIndex((item) => item.id === tab)

    switch (event.key) {
      case 'ArrowRight':
        event.preventDefault()
        moveTo(TABS[(index + 1) % TABS.length].id)
        break
      case 'ArrowLeft':
        event.preventDefault()
        moveTo(TABS[(index - 1 + TABS.length) % TABS.length].id)
        break
      case 'Home':
        event.preventDefault()
        moveTo(TABS[0].id)
        break
      case 'End':
        event.preventDefault()
        moveTo(TABS[TABS.length - 1].id)
        break
      default:
        break
    }
  }

  return (
    <div
      role="tablist"
      aria-label="Какой справочник показать"
      className="flex max-w-full items-center gap-0.5 overflow-x-auto rounded-lg bg-muted p-0.5"
    >
      {TABS.map((item) => {
        const isSelected = item.id === tab

        return (
          <button
            key={item.id}
            ref={(element) => {
              tabRefs.current[item.id] = element
            }}
            type="button"
            role="tab"
            id={`catalogue-tab-${item.id}`}
            aria-selected={isSelected}
            aria-controls={isSelected ? 'catalogue-panel' : undefined}
            tabIndex={isSelected ? 0 : -1}
            onClick={() => onChange(item.id)}
            onKeyDown={handleKeyDown}
            className={cn(
              'inline-flex h-11 shrink-0 items-center rounded-md px-3 text-sm whitespace-nowrap transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-7',
              isSelected
                ? 'bg-background font-medium text-foreground shadow-sm ring-1 ring-foreground/10'
                : 'text-muted-foreground hover:text-foreground'
            )}
          >
            {item.label}
          </button>
        )
      })}
    </div>
  )
}
