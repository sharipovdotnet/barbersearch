import type { MouseEvent } from 'react'
import { CalendarCheck, CalendarClock, type LucideIcon } from 'lucide-react'

import { BARBER_CABINET_PATH, BARBER_SCHEDULE_PATH } from '@/lib/barber-cabinet-path'
import { isPlainClick, navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

type Section = 'bookings' | 'schedule'

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
]

/**
 * Два раздела кабинета барбера: записи клиентов и собственное расписание.
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
            <li key={item.section} className="flex-1 sm:flex-none">
              <a
                href={item.href}
                aria-current={isCurrent ? 'page' : undefined}
                className={cn(
                  'flex h-11 items-center justify-center gap-2 rounded-md px-3 text-sm transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-8',
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
                <Icon className="size-4 shrink-0" aria-hidden />
                {item.label}
              </a>
            </li>
          )
        })}
      </ul>
    </nav>
  )
}
