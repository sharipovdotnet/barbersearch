import type { ReactNode } from 'react'

import { useI18n } from '@/i18n'
import { barbersPath } from '@/lib/admin-path'
import type {
  AdminStatisticsBarberTopRow,
  AdminStatisticsCityTopRow,
  AdminStatisticsServiceTopRow,
  AdminStatisticsTopOther,
} from '@/lib/admin-statistics'
import { formatNumber, formatPrice, formatRating } from '@/lib/format'
import { navigate } from '@/lib/router'

/** Подпись под каждой таблицей: одно правило для всех трёх. */
const MIN_VISITS_NOTE =
  'В топ попадают минимум 3 завершённых визита; «Остальные» дополняют итог периода.'

function TopTableShell({
  caption,
  head,
  children,
}: {
  caption: string
  head: ReactNode
  children: ReactNode
}) {
  return (
    <div className="overflow-x-auto rounded-xl border">
      <table className="w-full min-w-[34rem] text-sm">
        <caption className="sr-only">{caption}</caption>
        <thead className="border-b text-left text-muted-foreground">
          {head}
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  )
}

function TopEmpty({ title }: { title: string }) {
  const { t } = useI18n()

  return (
    <p className="rounded-xl border border-dashed px-4 py-6 text-sm text-muted-foreground">
      {t('{title}: пока нет данных — нужно минимум три завершённых визита.', {
        title,
      })}
    </p>
  )
}

function average(turnover: number, visits: number): string {
  return visits === 0 ? '—' : formatPrice(turnover / visits)
}

/** Топ барберов: визиты, оборот, средний чек, неявки, рейтинг и число отзывов. */
export function BarberTopTable({
  rows,
  other,
}: {
  rows: AdminStatisticsBarberTopRow[]
  other: AdminStatisticsTopOther
}) {
  const { t } = useI18n()

  if (rows.length === 0) {
    return <TopEmpty title={t('Барберы')} />
  }

  return (
    <div className="space-y-2">
      <TopTableShell
        caption={t('Топ барберов по завершённым визитам')}
        head={
          <tr>
            <th scope="col" className="px-3 py-2 font-medium">
              {t('Барбер')}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              {t('Визиты')}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              {t('Оборот')}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              {t('Средний чек')}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              {t('Неявки')}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              {t('Рейтинг')}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              {t('Отзывы')}
            </th>
          </tr>
        }
      >
        {rows.map((row) => {
          const href = barbersPath(row.barberProfileId, 'all')

          return (
            <tr key={row.barberProfileId} className="border-b last:border-b-0">
              <th scope="row" className="px-3 py-2 text-left font-normal">
                <a
                  href={href}
                  className="inline-flex min-h-11 items-center rounded-md break-words underline-offset-4 hover:underline focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:min-h-8"
                  onClick={(event) => {
                    event.preventDefault()
                    navigate(href)
                  }}
                >
                  {row.displayName}
                </a>
              </th>
              <td className="px-3 py-2 text-right tabular-nums">
                {formatNumber(row.visits)}
              </td>
              <td className="px-3 py-2 text-right tabular-nums">
                {formatPrice(row.turnover)}
              </td>
              <td className="px-3 py-2 text-right tabular-nums">
                {formatPrice(row.averageCheck)}
              </td>
              <td className="px-3 py-2 text-right tabular-nums">
                {formatNumber(row.noShow)}
              </td>
              <td className="px-3 py-2 text-right tabular-nums">
                {formatRating(row.rating)}
              </td>
              <td className="px-3 py-2 text-right tabular-nums">
                {formatNumber(row.reviewsCount)}
              </td>
            </tr>
          )
        })}
        <tr className="text-muted-foreground">
          <th scope="row" className="px-3 py-2 text-left font-normal">
            {t('Остальные')}
          </th>
          <td className="px-3 py-2 text-right tabular-nums">
            {formatNumber(other.visits)}
          </td>
          <td className="px-3 py-2 text-right tabular-nums">
            {formatPrice(other.turnover)}
          </td>
          <td className="px-3 py-2 text-right tabular-nums">
            {average(other.turnover, other.visits)}
          </td>
          <td className="px-3 py-2 text-right">—</td>
          <td className="px-3 py-2 text-right">—</td>
          <td className="px-3 py-2 text-right">—</td>
        </tr>
      </TopTableShell>
      <p className="text-xs text-muted-foreground">{t(MIN_VISITS_NOTE)}</p>
    </div>
  )
}

/** Топ услуг каталога: у одного каталога много строк барберов, считается услуга. */
export function ServiceTopTable({
  rows,
  other,
}: {
  rows: AdminStatisticsServiceTopRow[]
  other: AdminStatisticsTopOther
}) {
  const { t } = useI18n()

  if (rows.length === 0) {
    return <TopEmpty title={t('Услуги')} />
  }

  return (
    <div className="space-y-2">
      <TopTableShell
        caption={t('Топ услуг по завершённым визитам')}
        head={
          <tr>
            <th scope="col" className="px-3 py-2 font-medium">
              {t('Услуга')}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              {t('Визиты')}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              {t('Оборот')}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              {t('Средний чек')}
            </th>
          </tr>
        }
      >
        {rows.map((row) => (
          <tr key={row.hairServiceId} className="border-b last:border-b-0">
            <th scope="row" className="px-3 py-2 text-left font-normal">
              {row.name}
            </th>
            <td className="px-3 py-2 text-right tabular-nums">
              {formatNumber(row.visits)}
            </td>
            <td className="px-3 py-2 text-right tabular-nums">
              {formatPrice(row.turnover)}
            </td>
            <td className="px-3 py-2 text-right tabular-nums">
              {formatPrice(row.averageCheck)}
            </td>
          </tr>
        ))}
        <tr className="text-muted-foreground">
          <th scope="row" className="px-3 py-2 text-left font-normal">
            {t('Остальные')}
          </th>
          <td className="px-3 py-2 text-right tabular-nums">
            {formatNumber(other.visits)}
          </td>
          <td className="px-3 py-2 text-right tabular-nums">
            {formatPrice(other.turnover)}
          </td>
          <td className="px-3 py-2 text-right tabular-nums">
            {average(other.turnover, other.visits)}
          </td>
        </tr>
      </TopTableShell>
      <p className="text-xs text-muted-foreground">{t(MIN_VISITS_NOTE)}</p>
    </div>
  )
}

/** Топ городов: город салона визита и активные барберы города на сейчас. */
export function CityTopTable({
  rows,
  other,
}: {
  rows: AdminStatisticsCityTopRow[]
  other: AdminStatisticsTopOther
}) {
  const { t } = useI18n()

  if (rows.length === 0) {
    return <TopEmpty title={t('Города')} />
  }

  return (
    <div className="space-y-2">
      <TopTableShell
        caption={t('Топ городов по завершённым визитам')}
        head={
          <tr>
            <th scope="col" className="px-3 py-2 font-medium">
              {t('Город')}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              {t('Визиты')}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              {t('Оборот')}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              {t('Средний чек')}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              {t('Активные барберы')}
            </th>
          </tr>
        }
      >
        {rows.map((row) => (
          <tr key={row.name} className="border-b last:border-b-0">
            <th scope="row" className="px-3 py-2 text-left font-normal">
              {row.name}
            </th>
            <td className="px-3 py-2 text-right tabular-nums">
              {formatNumber(row.visits)}
            </td>
            <td className="px-3 py-2 text-right tabular-nums">
              {formatPrice(row.turnover)}
            </td>
            <td className="px-3 py-2 text-right tabular-nums">
              {formatPrice(row.averageCheck)}
            </td>
            <td className="px-3 py-2 text-right tabular-nums">
              {formatNumber(row.activeBarbers)}
            </td>
          </tr>
        ))}
        <tr className="text-muted-foreground">
          <th scope="row" className="px-3 py-2 text-left font-normal">
            {t('Остальные')}
          </th>
          <td className="px-3 py-2 text-right tabular-nums">
            {formatNumber(other.visits)}
          </td>
          <td className="px-3 py-2 text-right tabular-nums">
            {formatPrice(other.turnover)}
          </td>
          <td className="px-3 py-2 text-right tabular-nums">
            {average(other.turnover, other.visits)}
          </td>
          <td className="px-3 py-2 text-right">—</td>
        </tr>
      </TopTableShell>
      <p className="text-xs text-muted-foreground">{t(MIN_VISITS_NOTE)}</p>
    </div>
  )
}
