import type { MouseEvent } from 'react'
import {
  CalendarCheck,
  CalendarClock,
  MapPin,
  UserRound,
  type LucideIcon,
} from 'lucide-react'

import {
  BARBER_CABINET_PATH,
  BARBER_PROFILE_PATH,
  BARBER_SALONS_PATH,
  BARBER_SCHEDULE_PATH,
} from '@/lib/barber-cabinet-path'
import { isPlainClick, navigate } from '@/lib/router'
import { cn } from '@/lib/utils'
import { useI18n } from '@/i18n'

type Section = 'bookings' | 'schedule' | 'salons' | 'profile'

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
  {
    section: 'profile',
    href: BARBER_PROFILE_PATH,
    label: 'Профиль',
    icon: UserRound,
  },
]

/**
 * Четыре раздела кабинета барбера: записи клиентов, расписание, салоны и профиль.
 *
 * Ссылки настоящие, поэтому средний клик и «открыть в новой вкладке» работают;
 * `aria-current` помечает текущий раздел. На 320 px четыре подписи в строку не
 * влезают, поэтому там сетка 2×2 с ячейками 44 px; с `sm:` — прежняя строка.
 */
export function BarberCabinetNav({ active }: { active: Section }) {
  const { t } = useI18n()

  return (
    <nav aria-label={t('Разделы кабинета барбера')} className="mb-5">
      <ul className="grid w-full grid-cols-2 gap-1 rounded-lg border bg-muted/40 p-1 sm:inline-flex sm:w-auto">
        {ITEMS.map((item) => {
          const Icon = item.icon
          const isCurrent = item.section === active

          return (
            <li key={item.section} className="min-w-0 sm:flex-none">
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
                <span className="truncate">{t(item.label)}</span>
              </a>
            </li>
          )
        })}
      </ul>
    </nav>
  )
}
