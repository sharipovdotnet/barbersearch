import { useRef, type KeyboardEvent } from 'react'

import type { AdminTab } from '@/lib/admin-path'
import { cn } from '@/lib/utils'

const TABS = [
  { id: 'pending', label: 'На модерации' },
  { id: 'published', label: 'Опубликованные' },
] as const

/**
 * Вкладки отзывов.
 *
 * Счётчиков нет: API не отдаёт общее число, а «12» на вкладке было бы выдумкой.
 * Паттерн — ручной roving tabindex, как в списке записей кабинета.
 */
export function ReviewTabs({
  tab,
  onChange,
}: {
  tab: AdminTab
  onChange: (tab: AdminTab) => void
}) {
  const tabRefs = useRef<Record<AdminTab, HTMLButtonElement | null>>({
    pending: null,
    published: null,
  })

  function handleKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
      return
    }

    event.preventDefault()
    const next: AdminTab =
      tab === 'pending' ? 'published' : 'pending'

    onChange(next)
    tabRefs.current[next]?.focus()
  }

  return (
    <div
      role="tablist"
      aria-label="Какие отзывы показать"
      className="inline-flex items-center gap-0.5 rounded-lg bg-muted p-0.5"
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
            id={`reviews-tab-${item.id}`}
            aria-selected={isSelected}
            aria-controls={isSelected ? 'reviews-panel' : undefined}
            tabIndex={isSelected ? 0 : -1}
            onClick={() => onChange(item.id)}
            onKeyDown={handleKeyDown}
            className={cn(
              'inline-flex h-11 items-center rounded-md px-3 text-sm transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-7',
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
