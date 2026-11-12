import { useAdminQueues } from '@/hooks/use-admin-queues'
import { useI18n, type MessageParams } from '@/i18n'
import {
  formatAdminBadgeCount,
  selectSectionCounts,
  type AdminSectionCounts,
} from '@/lib/admin-summary'
import { navigate, usePathname } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * Поля снимка сводки, у которых есть свой пункт рейла.
 *
 * `photosAvatars` и `photosPortfolio` сюда не входят: у «Фото» один бейдж-сумма,
 * разбивка уходит в `aria-label`.
 */
type SectionCountKey =
  | 'reviews'
  | 'photos'
  | 'barbers'
  | 'users'
  | 'barberRequests'
  | 'salons'
  | 'bookings'

/** Пункт рейла: подпись и, у очередей, поле снимка сводки. */
type Section = {
  href: string
  label: string
  /** Нет поля — нет бейджа: «Сводка», «Статистика» и «Журнал» ничего не копят. */
  count?: SectionCountKey
}

const SECTIONS: readonly Section[] = [
  { href: '/admin/summary', label: 'Сводка' },
  { href: '/admin/statistics', label: 'Статистика' },
  { href: '/admin/reviews', label: 'Отзывы', count: 'reviews' },
  { href: '/admin/photos', label: 'Фото', count: 'photos' },
  { href: '/admin/barbers', label: 'Барберы', count: 'barbers' },
  { href: '/admin/users', label: 'Пользователи', count: 'users' },
  { href: '/admin/barber-requests', label: 'Заявки', count: 'barberRequests' },
  { href: '/admin/catalogue', label: 'Каталог', count: 'salons' },
  { href: '/admin/bookings', label: 'Брони', count: 'bookings' },
  { href: '/admin/audit', label: 'Журнал' },
]

/**
 * Рейл разделов администрирования.
 *
 * В навигации только работающие разделы: мёртвых ссылок «скоро» нет.
 * «Сводка» стоит первой как ответ на вопрос «что требует внимания», но стартовой
 * страницей не становится: `/admin` по-прежнему открывает очередь отзывов — рейл
 * это карта разделов, а не стартовая страница. «Журнал» остаётся последним.
 * На мобильном рейл становится горизонтальной лентой под шапкой.
 *
 * Счётчики приходят из того же снимка сводки, что читает экран «Сводка»: числа
 * считает сервер, клиент их не пересчитывает. Пока снимка нет — бейджей нет, и
 * layout не прыгает: бейдж абсолютный и не участвует в потоке.
 */
export function AdminRail() {
  const pathname = usePathname()
  const { t } = useI18n()
  const { summary } = useAdminQueues()
  const counts = summary === null ? null : selectSectionCounts(summary)

  return (
    <nav
      aria-label={t('Разделы администрирования')}
      className="lg:sticky lg:top-20 lg:border-r lg:border-border lg:pr-4"
    >
      <p className="mb-2 hidden px-3 text-xs font-medium tracking-wide text-muted-foreground uppercase lg:block">
        {t('Разделы')}
      </p>
      {/* `pt-2` — место под выступ бейджа: при `overflow-x: auto` второе измерение
          тоже становится `auto`, и отрицательный `top` обрезался бы. `lg:overflow-visible`
          снимает обрезку в колонке, где горизонтальной прокрутки и не бывает. */}
      <ul className="flex gap-1 overflow-x-auto pt-2 lg:flex-col lg:overflow-visible">
        {SECTIONS.map((section) => {
          const isCurrent =
            pathname === section.href || pathname.startsWith(`${section.href}/`)
          const count =
            section.count !== undefined ? (counts?.[section.count] ?? 0) : 0
          const label =
            section.count !== undefined && counts !== null && count > 0
              ? sectionLabel(t, section.count, counts, count)
              : undefined

          return (
            <li key={section.href}>
              <a
                href={section.href}
                aria-current={isCurrent ? 'page' : undefined}
                aria-label={label}
                className={cn(
                  'relative flex h-11 items-center rounded-lg px-3 text-sm transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 lg:h-8',
                  isCurrent
                    ? 'bg-muted font-medium text-foreground'
                    : 'text-muted-foreground hover:text-foreground'
                )}
                onClick={(event) => {
                  event.preventDefault()
                  navigate(section.href)
                }}
              >
                {t(section.label)}
                {count > 0 && (
                  // Круг — сам бейдж, цифра живёт внутри и центрируется `flex`.
                  // Фон непрозрачный: полупрозрачный `bg-destructive/10` в тёмной
                  // теме давал 4.32:1, а на активном пункте — 3.67:1, ниже AA.
                  // С `bg-background` и красной обводкой — 5.79:1 и 5.31:1 в light,
                  // 6.84:1 и 5.23:1 в dark (второе число — активный пункт).
                  // `aria-hidden`: точное число уже есть в `aria-label` ссылки.
                  <span
                    aria-hidden="true"
                    className="pointer-events-none absolute -top-1 right-2 inline-flex h-5 min-w-5 items-center justify-center rounded-full border border-destructive/40 bg-background px-1 text-[11px] leading-none font-semibold text-destructive tabular-nums select-none"
                  >
                    {formatAdminBadgeCount(count)}
                  </span>
                )}
              </a>
            </li>
          )
        })}
      </ul>
    </nav>
  )
}

/**
 * Имя ссылки с числом: скринридер читает пункт один раз и с точным значением.
 *
 * У «Фото» в имени есть разбивка по двум очередям — бейдж там один, а раздел
 * открывает две вкладки. При нуле имени нет: остаётся видимый текст, а «0» не
 * озвучивается как работа.
 */
function sectionLabel(
  t: (source: string, params?: MessageParams) => string,
  field: SectionCountKey,
  counts: AdminSectionCounts,
  count: number
): string {
  switch (field) {
    case 'reviews':
      return t('Отзывы, на модерации: {count}', { count })
    case 'photos':
      return t(
        'Фото, на модерации: {count} (аватары: {avatars}, портфолио: {portfolio})',
        {
          count,
          avatars: counts.photosAvatars,
          portfolio: counts.photosPortfolio,
        }
      )
    case 'barbers':
      return t('Барберы, на проверке: {count}', { count })
    case 'users':
      return t('Пользователи, без подтверждения email: {count}', { count })
    case 'barberRequests':
      return t('Заявки, в очереди: {count}', { count })
    case 'salons':
      return t('Каталог, салоны на проверке: {count}', { count })
    case 'bookings':
      return t('Брони, подтверждённые: {count}', { count })
  }
}
