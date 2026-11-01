import { navigate, usePathname } from '@/lib/router'
import { cn } from '@/lib/utils'

const SECTIONS = [
  { href: '/admin/reviews', label: 'Отзывы' },
  { href: '/admin/barbers', label: 'Барберы' },
  { href: '/admin/catalogue', label: 'Каталог' },
  { href: '/admin/users', label: 'Пользователи' },
] as const

/**
 * Рейл разделов администрирования.
 *
 * В навигации только работающие разделы: мёртвых ссылок «скоро» нет.
 * На мобильном рейл становится горизонтальной лентой под шапкой.
 */
export function AdminRail() {
  const pathname = usePathname()

  return (
    <nav
      aria-label="Разделы администрирования"
      className="lg:sticky lg:top-20 lg:border-r lg:border-border lg:pr-4"
    >
      <p className="mb-2 hidden px-3 text-xs font-medium tracking-wide text-muted-foreground uppercase lg:block">
        Разделы
      </p>
      <ul className="flex gap-1 overflow-x-auto lg:flex-col">
        {SECTIONS.map((section) => {
          const isCurrent =
            pathname === section.href || pathname.startsWith(`${section.href}/`)

          return (
            <li key={section.href}>
              <a
                href={section.href}
                aria-current={isCurrent ? 'page' : undefined}
                className={cn(
                  'flex h-11 items-center rounded-lg px-3 text-sm transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 lg:h-8',
                  isCurrent
                    ? 'bg-muted font-medium text-foreground'
                    : 'text-muted-foreground hover:text-foreground'
                )}
                onClick={(event) => {
                  event.preventDefault()
                  navigate(section.href)
                }}
              >
                {section.label}
              </a>
            </li>
          )
        })}
      </ul>
    </nav>
  )
}
