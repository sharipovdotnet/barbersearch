import { navigate, usePathname } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * Рейл разделов администрирования.
 *
 * Пока реализован только один раздел, поэтому в навигации нет мёртвых ссылок «скоро».
 * На мобильном рейл становится горизонтальной лентой под шапкой.
 */
export function AdminRail() {
  const pathname = usePathname()
  const isReviews =
    pathname === '/admin' ||
    pathname === '/admin/reviews' ||
    pathname.startsWith('/admin/reviews/')

  return (
    <nav
      aria-label="Разделы администрирования"
      className="lg:sticky lg:top-20 lg:border-r lg:border-border lg:pr-4"
    >
      <p className="mb-2 hidden px-3 text-xs font-medium tracking-wide text-muted-foreground uppercase lg:block">
        Разделы
      </p>
      <ul className="flex gap-1 overflow-x-auto lg:flex-col">
        <li>
          <a
            href="/admin/reviews"
            aria-current={isReviews ? 'page' : undefined}
            className={cn(
              'flex h-11 items-center rounded-lg px-3 text-sm transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 lg:h-8',
              isReviews
                ? 'bg-muted font-medium text-foreground'
                : 'text-muted-foreground hover:text-foreground'
            )}
            onClick={(event) => {
              event.preventDefault()
              navigate('/admin/reviews')
            }}
          >
            Отзывы
          </a>
        </li>
      </ul>
    </nav>
  )
}
