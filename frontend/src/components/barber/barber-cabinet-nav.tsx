import type { MouseEvent } from 'react'
import { CalendarCheck, CalendarClock, MapPin, type LucideIcon } from 'lucide-react'

import {
  BARBER_CABINET_PATH,
  BARBER_SALONS_PATH,
  BARBER_SCHEDULE_PATH,
} from '@/lib/barber-cabinet-path'
import { isPlainClick, navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

type Section = 'bookings' | 'schedule' | 'salons'

const ITEMS: readonly {
  section: Section
  href: string
  label: string
  icon: LucideIcon
}[] = [
  {
    section: 'bookings',
    href: BARBER_CABINET_PATH,
    label: 'Записи',
    icon: CalendarCheck,
  },
  {
    section: 'schedule',
    href: BARBER_SCHEDULE_PATH,
    label: 'Расписание',
    icon: CalendarClock,
  },
  {
    section: 'salons',
    href: BARBER_SALONS_PATH,
    label: 'Салоны',
    icon: MapPin,
  },
]

/**
 * Три раздела кабинета барбера: записи клиентов, расписание и салоны.
 *
 * Ссылки настоящие, поэтому средний клик и «открыть в новой вкладке» работают;
 * `aria-current` помечает текущий раздел.
 */
export function BarberCabinetNav({ active }: { active: Section }) {
  return (
    <nav aria-label="Разделы кабинета барбера" className="mb-5">
      <ul className="inline-flex w-full gap-1 rounded-lg border bg-muted/40 p-1 sm:w-auto">
        {ITEMS.map((item) => {
          const Icon = item.icon
          const isCurrent = item.section === active

          return (
            <li key={item.section} className="min-w-0 flex-1 sm:flex-none">
              <a
                href={item.href}
                aria-current={isCurrent ? 'page' : undefined}
                className={cn(
                  'flex h-11 min-w-0 items-center justify-center gap-1.5 rounded-md px-2 text-sm transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-8 sm:gap-2 sm:px-3',
                  isCurrent
                    ? 'bg-background font-medium text-foreground shadow-sm'
                    : 'text-muted-foreground hover:text-foreground'
                )}
                onClick={(event: MouseEvent<HTMLAnchorElement>) => {
                  if (isPlainClick(event)) {
                    event.preventDefault()
                    navigate(item.href)
                  }
                }}
              >
                <Icon className="hidden size-4 shrink-0 sm:block" aria-hidden />
                <span className="truncate">{item.label}</span>
              </a>
            </li>
          )
        })}
      </ul>
    </nav>
  )
}
