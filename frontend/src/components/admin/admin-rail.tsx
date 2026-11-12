import { useAdminQueues } from '@/hooks/use-admin-queues'
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip'
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
      {/* Общий провайдер: пока открыта одна подсказка, соседние открываются сразу —
          так рейл не заставляет ждать задержку на каждом пункте. */}
      <TooltipProvider>
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
            // Подсказка объясняет, что именно посчитано, и потому появляется
            // только вместе с бейджем: у пункта без числа объяснять нечего.
            const hint =
              section.count !== undefined && counts !== null && count > 0
                ? sectionHint(t, section.count, counts)
                : null

            const link = (
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
              </a>
            )

            return (
              <li key={section.href} className="relative">
                {/* Обёртка стоит всегда, даже без числа: иначе приход снимка сводки
                    пересоздавал бы `<a>` — терялись бы фокус и наведение у того, кто
                    уже навёл курсор на пункт. Без числа подсказка просто выключена. */}
                <Tooltip disabled={hint === null}>
                  {/* Триггер — сама ссылка: она и так в фокусе и под курсором, а
                      отдельная кнопка внутри `<a>` была бы вложенным интерактивом. */}
                  <TooltipTrigger render={link} />
                  {hint !== null && (
                    <TooltipContent side="right" align="center" sideOffset={10}>
                      {hint}
                    </TooltipContent>
                  )}
                </Tooltip>

                {count > 0 && (
                  // Круг — сам бейдж, цифра живёт внутри и центрируется `flex`.
                  // Заливка сплошная и одинаковая в обеих темах: `--destructive`
                  // в тёмной теме специально светлый (это цвет текста), поэтому
                  // залитый кружок берёт `--destructive-solid`. Плашка 10 %-й
                  // прозрачности давала 4.32:1 в тёмной теме и 3.67:1 на активном
                  // пункте — ниже AA; белые цифры на сплошной заливке дают 5.79:1
                  // и от подложки не зависят.
                  //
                  // Бейдж — сосед ссылки, а не её ребёнок: axe-правило
                  // `label-content-name-mismatch` (serious) требует, чтобы видимый
                  // текст входил в доступное имя, а цифра внутри `<a>` делает
                  // видимый текст «Отзывы14» — это не подстрока aria-label
                  // «Отзывы, на модерации: 14». Внутри ссылки бейдж к тому же
                  // читался бы дважды.
                  //
                  // `pointer-events-none`: клик и наведение проходят сквозь кружок
                  // на ссылку под ним, поэтому переход и подсказка работают как
                  // раньше; `aria-hidden` — число уже есть в имени ссылки.
                  <span
                    aria-hidden="true"
                    className="pointer-events-none absolute -top-1 right-2 inline-flex h-5 min-w-5 items-center justify-center rounded-full border border-destructive-solid bg-destructive-solid px-1 text-[11px] leading-none font-semibold text-white tabular-nums select-none"
                  >
                    {formatAdminBadgeCount(count)}
                  </span>
                )}
              </li>
            )
          })}
        </ul>
      </TooltipProvider>
    </nav>
  )
}

/**
 * Подсказка к бейджу: что означает число, без повтора `aria-label`.
 *
 * Имя ссылки уже говорит «Отзывы, на модерации: 12» — скринридеру этого хватает.
 * Видимая подсказка объясняет смысл очереди, иначе «12» в кружке ничего не значит
 * для того, кто впервые открыл админку. У «Фото» в подсказке видна разбивка:
 * бейдж там один, а очередь состоит из аватаров и портфолио.
 */
function sectionHint(
  t: (source: string, params?: MessageParams) => string,
  field: SectionCountKey,
  counts: AdminSectionCounts
): string {
  switch (field) {
    case 'reviews':
      return t('Скрытые отзывы ждут модерации')
    case 'photos':
      return t('На модерации: аватары — {avatars}, портфолио — {portfolio}', {
        avatars: counts.photosAvatars,
        portfolio: counts.photosPortfolio,
      })
    case 'barbers':
      return t('Профили ждут подтверждения и пока не видны в поиске')
    case 'users':
      return t('Аккаунты без подтверждённого email среди всех ролей')
    case 'barberRequests':
      return t('Заявки «Стать барбером» ждут решения')
    case 'salons':
      return t('Салоны на проверке — до одобрения их нет в поиске')
    case 'bookings':
      return t('Брони в статусе «Подтверждена», включая прошедшие')
  }
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
