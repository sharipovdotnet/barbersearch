import { Inbox, Loader2, RefreshCw, TriangleAlert } from 'lucide-react'
import { useEffect, useState } from 'react'

import { AdminSignInGate } from '@/components/admin/admin-gate'
import { StatSection } from '@/components/admin/statistics/stat-section'
import {
  MetricStrip,
  type StatisticsMetric,
} from '@/components/admin/statistics/metric-strip'
import { StatusBar } from '@/components/admin/statistics/status-bar'
import { TrendChart } from '@/components/admin/statistics/trend-chart'
import {
  BarberTopTable,
  CityTopTable,
  ServiceTopTable,
} from '@/components/admin/statistics/top-table'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { useAdminStatistics } from '@/hooks/use-admin-statistics'
import {
  STATISTICS_PRESETS,
  formatStatisticsRange,
  isEmptyStatistics,
  parseStatisticsPeriod,
  resolveStatisticsPeriod,
  statisticsDays,
  statisticsPath,
  statisticsPresetPeriod,
  type AdminStatistics,
  type AdminStatisticsModeration,
} from '@/lib/admin-statistics'
import {
  formatClockTime,
  formatCount,
  formatDelta,
  formatNumber,
  formatPercent,
  formatPrice,
  formatRating,
} from '@/lib/format'
import { PLACE_KIND_LABELS, isPlaceKind } from '@/lib/place-kind'
import { navigate, useLocation } from '@/lib/router'

/**
 * `/admin/statistics` — «Статистика сервиса» за период.
 *
 * Числа считает сервер (`GET api/admin/statistics`); экран не пересчитывает
 * статистику из списков и не выдумывает пропуски. Период живёт в URL
 * (`?from=&to=&compare=1`), даты — UTC-сутки включительно, максимум 366 дней.
 * Некорректная ссылка открывает дефолт в 30 дней без ошибки: настоящую проверку
 * всё равно делает сервер.
 *
 * Автообновления нет: период не должен меняться под руками. «Обновить» —
 * тихое обновление, ошибка которого не стирает уже показанные секции.
 */
export function StatisticsScreen() {
  const location = useLocation()
  const [initial] = useState(() => parseStatisticsPeriod(searchOf(location)))
  const [from, setFrom] = useState(initial.from)
  const [to, setTo] = useState(initial.to)
  const [compare, setCompare] = useState(initial.compare)

  // Кривая дата или период длиннее года не блокируют экран: за границы формы
  // показывает дефолт, а API всё равно валидирует период.
  const period = resolveStatisticsPeriod(from, to, compare)
  const days = statisticsDays(period.from, period.to) ?? 0
  const isCustomPreset = !STATISTICS_PRESETS.some(
    (preset) => preset.days === days
  )

  useEffect(() => {
    navigate(
      statisticsPath({
        from: period.from,
        to: period.to,
        compare: period.compare,
      }),
      { replace: true }
    )
  }, [period.from, period.to, period.compare])

  const statistics = useAdminStatistics(period)

  function applyPreset(presetDays: number) {
    const next = statisticsPresetPeriod(presetDays)

    setFrom(next.from)
    setTo(next.to)
  }

  if (statistics.requiresSignIn) {
    return (
      <div className="mx-auto w-full max-w-md py-6">
        <AdminSignInGate
          returnTo={statisticsPath(period)}
          onSignedIn={statistics.reload}
        />
      </div>
    )
  }

  const isFirstLoad = statistics.status === 'loading'
  const data = statistics.statistics
  const isReady = statistics.status === 'ready' && data !== null
  const isEmpty = data !== null && isEmptyStatistics(data)

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="scroll-mt-20 font-heading text-2xl font-semibold tracking-tight sm:text-3xl">
            Статистика
          </h1>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
            Что происходит за период: записи, клиенты, предложение и качество.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {statistics.updatedAt !== null && (
            <span className="text-xs text-muted-foreground tabular-nums">
              Обновлено в {formatClockTime(new Date(statistics.updatedAt))}
            </span>
          )}
          <Button
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            disabled={isFirstLoad || statistics.isRefreshing}
            onClick={statistics.refresh}
          >
            {statistics.isRefreshing ? (
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
            ) : (
              <RefreshCw data-icon="inline-start" aria-hidden />
            )}
            {statistics.isRefreshing ? 'Обновляем…' : 'Обновить'}
          </Button>
        </div>
      </header>

      <div className="space-y-3 rounded-xl border p-3 sm:p-4">
        <div
          role="group"
          aria-label="Пресеты периода"
          className="flex flex-wrap gap-1"
        >
          {STATISTICS_PRESETS.map((preset) => {
            const isActive = !isCustomPreset && days === preset.days

            return (
              <Button
                key={preset.days}
                type="button"
                variant={isActive ? 'default' : 'outline'}
                size="sm"
                aria-pressed={isActive}
                className="h-11 sm:h-8"
                onClick={() => applyPreset(preset.days)}
              >
                {preset.label}
              </Button>
            )
          })}
        </div>

        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-[10rem_10rem_minmax(0,1fr)] lg:items-end">
          <div className="flex min-w-0 flex-col gap-1">
            <label htmlFor="statistics-from" className="text-sm font-medium">
              С даты
            </label>
            <Input
              id="statistics-from"
              type="date"
              value={from}
              aria-describedby="statistics-period-hint"
              className="h-11"
              onChange={(event) => setFrom(event.target.value)}
            />
          </div>

          <div className="flex min-w-0 flex-col gap-1">
            <label htmlFor="statistics-to" className="text-sm font-medium">
              По дату
            </label>
            <Input
              id="statistics-to"
              type="date"
              value={to}
              aria-describedby="statistics-period-hint"
              className="h-11"
              onChange={(event) => setTo(event.target.value)}
            />
          </div>

          <div className="flex min-h-11 items-center gap-2">
            <input
              id="statistics-compare"
              type="checkbox"
              checked={compare}
              className="size-4 accent-primary"
              onChange={(event) => setCompare(event.target.checked)}
            />
            <label htmlFor="statistics-compare" className="text-sm">
              Сравнить с прошлым периодом
            </label>
          </div>
        </div>

        <p
          id="statistics-period-hint"
          className="text-xs text-muted-foreground"
        >
          Даты — по UTC, границы включают обе даты. Максимум — 366 дней.
          Некорректный адрес открывает последние 30 дней.
        </p>
      </div>

      <p className="text-sm text-muted-foreground">
        <span className="font-medium text-foreground">
          {formatStatisticsRange(period)}
        </span>{' '}
        · дни по UTC
        {period.compare && data?.period.previous && (
          <>
            {' '}
            · сравнение с{' '}
            {formatStatisticsRange({
              from: data.period.previous.from,
              to: data.period.previous.to,
              compare: false,
            })}
          </>
        )}
      </p>

      {statistics.refreshError && (
        <p role="alert" className="text-sm text-destructive">
          {statistics.refreshError}
        </p>
      )}

      {statistics.isRefreshing && (
        <p role="status" className="sr-only">
          Обновляем статистику…
        </p>
      )}

      <div aria-busy={isFirstLoad || statistics.isRefreshing}>
        {isFirstLoad && <StatisticsSkeleton />}

        {statistics.status === 'error' && statistics.error !== null && (
          <StatisticsError
            message={statistics.error}
            onRetry={statistics.reload}
          />
        )}

        {isReady && data !== null && (
          <div className="space-y-6">
            <StatSection
              id="statistics-overview"
              title="Обзор"
              description="Шесть плиток периода. Дельты — к предыдущему периоду той же длины, если включено сравнение."
            >
              <OverviewTiles statistics={data} />
            </StatSection>

            {isEmpty ? (
              <StatisticsEmpty />
            ) : (
              <>
                <StatSection
                  id="statistics-bookings"
                  title="Записи"
                  description="Записи — по дате создания, визиты — по дате начала. Все дни — UTC."
                >
                  <TrendChart series={data.series} />

                  <div className="mt-5 space-y-3">
                    <h3 className="font-heading text-base font-semibold tracking-tight">
                      Статусы визитов
                    </h3>
                    <StatusBar statuses={data.statuses} />
                  </div>
                </StatSection>

                <div className="grid gap-6 lg:grid-cols-2">
                  <StatSection
                    id="statistics-clients"
                    title="Клиенты"
                    description="Только агрегаты: ни имён, ни идентификаторов клиентов в ответе нет."
                  >
                    <ClientsList statistics={data} />
                  </StatSection>

                  <StatSection
                    id="statistics-supply"
                    title="Предложение"
                    description="Барберы, профили и салоны. Салоны считаются все, включая выключенные."
                  >
                    <SupplyList statistics={data} />
                  </StatSection>
                </div>

                <StatSection
                  id="statistics-top"
                  title="Лидеры"
                  description="Барберы, услуги и города с минимум тремя завершёнными визитами за период."
                >
                  <div className="space-y-6">
                    <div className="space-y-2">
                      <h3 className="font-heading text-base font-semibold tracking-tight">
                        Барберы
                      </h3>
                      <BarberTopTable
                        rows={data.top.barbers.rows}
                        other={data.top.barbers.other}
                      />
                    </div>

                    <div className="space-y-2">
                      <h3 className="font-heading text-base font-semibold tracking-tight">
                        Услуги
                      </h3>
                      <ServiceTopTable
                        rows={data.top.services.rows}
                        other={data.top.services.other}
                      />
                    </div>

                    <div className="space-y-2">
                      <h3 className="font-heading text-base font-semibold tracking-tight">
                        Города
                      </h3>
                      <CityTopTable
                        rows={data.top.cities.rows}
                        other={data.top.cities.other}
                      />
                    </div>
                  </div>
                </StatSection>

                <StatSection
                  id="statistics-quality"
                  title="Качество и модерация"
                  description="Опубликованные отзывы периода и решения по заявкам. «На сейчас» — для того, что ещё может измениться."
                >
                  <QualityBlock statistics={data} />
                </StatSection>
              </>
            )}

            <CalculationDetails />
          </div>
        )}
      </div>
    </div>
  )
}

function searchOf(location: string): string {
  const index = location.indexOf('?')

  return index === -1 ? '' : location.slice(index)
}

function OverviewTiles({ statistics }: { statistics: AdminStatistics }) {
  const { overview, period } = statistics
  const compare = period.previous !== null

  const deltaPercent = (current: number | null, previous: number | null) => {
    if (!compare || previous === null) {
      return null
    }

    const value = formatDelta(current, previous)

    return value === '—' ? null : value
  }

  const noShow = overview.noShowRate
  const hasSample = noShow.sample > 0
  const isSmallSample = hasSample && noShow.sample < 10

  const metrics: StatisticsMetric[] = [
    {
      key: 'completedVisits',
      label: 'Завершённые визиты',
      value: formatCount(overview.completedVisits.value, [
        'визит',
        'визита',
        'визитов',
      ]),
      delta: deltaPercent(
        overview.completedVisits.value,
        overview.completedVisits.previous
      ),
    },
    {
      key: 'turnover',
      label: 'Оборот',
      value: formatPrice(overview.turnover.value),
      hint: 'по завершённым визитам',
      delta: deltaPercent(overview.turnover.value, overview.turnover.previous),
    },
    {
      key: 'averageCheck',
      label: 'Средний чек',
      value:
        overview.averageCheck.value === null
          ? '—'
          : formatPrice(overview.averageCheck.value),
      delta: deltaPercent(
        overview.averageCheck.value,
        overview.averageCheck.previous
      ),
    },
    {
      key: 'newAccounts',
      label: 'Новые аккаунты',
      value: formatCount(overview.newAccounts.value, [
        'аккаунт',
        'аккаунта',
        'аккаунтов',
      ]),
      hint: 'роль в аккаунте текущая',
      delta: deltaPercent(
        overview.newAccounts.value,
        overview.newAccounts.previous
      ),
    },
    {
      key: 'noShowRate',
      label: 'Неявки',
      value:
        !hasSample
          ? '—'
          : isSmallSample
            ? `${formatNumber(noShow.count)} из ${formatNumber(noShow.sample)}`
            : formatPercent(noShow.value),
      hint: hasSample
        ? `из ${formatNumber(noShow.sample)}`
        : 'визитов не было',
      delta:
        compare && !isSmallSample && hasSample
          ? noShow.previous === null
            ? null
            : formatDelta(noShow.value, noShow.previous, 'points')
          : null,
    },
    {
      key: 'stuckBookings',
      label: 'Зависшие брони',
      value: formatCount(overview.stuckBookings.value, [
        'бронь',
        'брони',
        'броней',
      ]),
      hint: 'активные со стартом в прошлом',
    },
  ]

  return <MetricStrip metrics={metrics} />
}

function DefinitionList({
  items,
}: {
  items: { label: string; value: string; hint?: string }[]
}) {
  return (
    <dl className="divide-y divide-border rounded-xl border">
      {items.map((item) => (
        <div
          key={item.label}
          className="flex flex-wrap items-baseline justify-between gap-2 px-4 py-3"
        >
          <dt className="text-sm text-muted-foreground">{item.label}</dt>
          <dd className="text-sm font-medium tabular-nums">
            {item.value}
            {item.hint && (
              <span className="ml-2 text-xs font-normal text-muted-foreground">
                {item.hint}
              </span>
            )}
          </dd>
        </div>
      ))}
    </dl>
  )
}

function ClientsList({ statistics }: { statistics: AdminStatistics }) {
  const clients = statistics.clients

  return (
    <DefinitionList
      items={[
        {
          label: 'С визитом',
          value: formatNumber(clients.withVisit),
        },
        {
          label: 'Вернувшиеся',
          value: formatNumber(clients.returning),
          hint: clients.withVisit === 0 ? undefined : `из ${clients.withVisit}`,
        },
        {
          label: 'Первый визит',
          value: formatNumber(clients.firstVisit),
          hint:
            clients.withVisit === 0 ? undefined : `из ${clients.withVisit}`,
        },
        {
          label: 'Гостевые записи',
          value: formatNumber(clients.guestBookings),
          hint:
            clients.guestShare === null
              ? undefined
              : formatPercent(clients.guestShare, 0),
        },
        {
          label: 'Доля визитов с отзывом',
          value: formatPercent(clients.reviewRate),
        },
      ]}
    />
  )
}

function SupplyList({ statistics }: { statistics: AdminStatistics }) {
  const supply = statistics.supply

  return (
    <div className="space-y-3">
      <DefinitionList
        items={[
          {
            label: 'Активные барберы',
            value: formatNumber(supply.activeBarbers),
          },
          {
            label: 'Работавшие',
            value: formatNumber(supply.workingBarbers),
            hint:
              supply.activeBarbers === 0
                ? undefined
                : `из ${supply.activeBarbers}`,
          },
          {
            label: 'Без визитов',
            value: formatNumber(supply.idleBarbers),
          },
          {
            label: 'Новые профили',
            value: formatNumber(supply.newProfiles),
          },
        ]}
      />

      <div className="rounded-xl border p-4">
        <h3 className="text-sm font-medium">Салоны по типу</h3>
        {supply.salonsByKind.length === 0 ? (
          <p className="mt-2 text-sm text-muted-foreground">
            Данных о салонах нет.
          </p>
        ) : (
          <ul className="mt-2 space-y-1 text-sm">
            {supply.salonsByKind.map((kind) => (
              <li
                key={kind.kind}
                className="flex flex-wrap items-baseline justify-between gap-2"
              >
                <span className="text-muted-foreground">
                  {isPlaceKind(kind.kind)
                    ? PLACE_KIND_LABELS[kind.kind]
                    : kind.kind}
                </span>
                <span className="tabular-nums">
                  {formatNumber(kind.listed + kind.unlisted)} · в каталоге{' '}
                  {formatNumber(kind.listed)}, скрыто {formatNumber(kind.unlisted)}
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}

function QualityBlock({ statistics }: { statistics: AdminStatistics }) {
  const quality = statistics.quality
  const maxCount = Math.max(1, ...quality.distribution)

  return (
    <div className="space-y-6">
      <div className="grid gap-6 md:grid-cols-2">
        <div className="space-y-3">
          <h3 className="font-heading text-base font-semibold tracking-tight">
            Оценки
          </h3>
          <p className="text-sm">
            Средняя оценка:{' '}
            <span className="font-medium">{formatRating(quality.rating)}</span>
            {quality.rating !== null && ' из 5'}
          </p>
          <p className="text-sm text-muted-foreground">
            Новых отзывов за период: {formatNumber(quality.reviews)}
          </p>

          <div>
            <h4 className="text-sm font-medium">
              Распределение оценок 1–5
            </h4>
            <p className="mt-1 text-xs text-muted-foreground">
              Только опубликованные:{' '}
              {formatNumber(
                quality.distribution.reduce((sum, count) => sum + count, 0)
              )}
            </p>
            <ul className="mt-2 space-y-1.5 text-sm">
              {[5, 4, 3, 2, 1].map((rating) => {
                const count = quality.distribution[rating - 1] ?? 0

                return (
                  <li key={rating} className="flex items-center gap-2">
                    <span className="w-4 text-right tabular-nums">{rating}</span>
                    <span className="h-2 flex-1 overflow-hidden rounded-full bg-muted">
                      <svg
                        viewBox="0 0 100 8"
                        preserveAspectRatio="none"
                        className="h-2 w-full"
                        aria-hidden
                      >
                        <rect
                          x={0}
                          y={0}
                          width={(count / maxCount) * 100}
                          height={8}
                          className="fill-success"
                        />
                      </svg>
                    </span>
                    <span className="w-8 text-right tabular-nums">{count}</span>
                  </li>
                )
              })}
            </ul>
          </div>
        </div>

        <div className="space-y-3">
          <h3 className="font-heading text-base font-semibold tracking-tight">
            Отзывы и модерация
          </h3>
          <DefinitionList
            items={[
              {
                label: 'Новые отзывы',
                value: formatNumber(quality.reviews),
              },
              {
                label: 'Опубликованы сейчас',
                value: formatPercent(quality.publishedShare),
              },
            ]}
          />
          <div className="grid gap-3 sm:grid-cols-2">
            <ModerationBlock title="Заявки в барберы" data={quality.requests} />
            <ModerationBlock title="Салоны" data={quality.salons} />
          </div>
        </div>
      </div>
    </div>
  )
}

function ModerationBlock({
  title,
  data,
}: {
  title: string
  data: AdminStatisticsModeration
}) {
  return (
    <div className="rounded-xl border p-4">
      <h4 className="text-sm font-medium">{title}</h4>
      <dl className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
        <dt className="text-muted-foreground">Подано</dt>
        <dd className="text-right tabular-nums">{formatNumber(data.submitted)}</dd>
        <dt className="text-muted-foreground">Одобрено</dt>
        <dd className="text-right tabular-nums">{formatNumber(data.approved)}</dd>
        <dt className="text-muted-foreground">Отклонено</dt>
        <dd className="text-right tabular-nums">{formatNumber(data.rejected)}</dd>
        <dt className="text-muted-foreground">Доля одобрений</dt>
        <dd className="text-right tabular-nums">
          {formatPercent(data.approvalRate)}
        </dd>
        <dt className="text-muted-foreground">Медиана решения</dt>
        <dd className="text-right tabular-nums">
          {data.medianDecisionHours === null
            ? '—'
            : `${formatNumber(data.medianDecisionHours, 1)} ч`}
        </dd>
      </dl>
    </div>
  )
}

function CalculationDetails() {
  return (
    <details className="rounded-xl border">
      <summary className="flex min-h-11 cursor-pointer items-center px-4 py-3 text-sm font-medium outline-none focus-visible:ring-3 focus-visible:ring-ring/50">
        Как считаем
      </summary>
      <div className="space-y-3 border-t px-4 py-4 text-sm text-muted-foreground">
        <p>
          Период — UTC-сутки, обе даты включительно; запросы идут в границах
          <span className="font-mono"> [from 00:00Z, (to + 1) 00:00Z)</span>.
        </p>
        <p>
          «Завершённые визиты» — брони со статусом «Завершена», у которых начало
          визита попало в период. «Записи» считаются по дате создания, включая
          гостевые.
        </p>
        <p className="text-foreground">
          <span className="font-medium">Оборот</span> — сумма цен завершённых
          визитов. Это не выручка: платежей в сервисе нет, деньги учитываются по
          цене-снимку услуги.
        </p>
        <p>
          Средний чек — оборот, делённый на завершённые визиты. Неявки — неявки
          делятся на завершённые плюс неявившиеся; при знаменателе меньше 10
          показываются абсолютные числа. «—» означает неизвестное, а не ноль.
        </p>
        <p>
          «Вернувшиеся» — клиенты с завершённым визитом до периода; «первый визит» —
          у кого минимальный завершённый визит попал в период. «Новые аккаунты» —
          аккаунты, созданные в периоде, а не новые клиенты: роль в аккаунте
          текущая.
        </p>
        <p>
          Доля визитов с отзывом — отзывы по завершённым визитам периода, на
          сейчас: последние дни ещё неполные. Медиана решения — 50-й перцентиль
          времени от подачи до решения по заявкам, решённым в периоде.
        </p>
        <p>
          Активные барберы — профили, которые активны и подтверждены. Города — город
          салона визита; число активных барберов города — на сейчас.
        </p>
        <p>
          Зависшие брони — снимок: активные брони со стартом в прошлом. Прошлого
          значения у снимка нет.
        </p>
      </div>
    </details>
  )
}

function StatisticsSkeleton() {
  return (
    <div className="space-y-5" aria-busy="true">
      <p role="status" className="sr-only">
        Загружаем статистику…
      </p>
      <Skeleton className="h-28 rounded-xl" aria-hidden />
      <Skeleton className="h-80 rounded-xl" aria-hidden />
      <div className="grid gap-6 lg:grid-cols-2">
        <Skeleton className="h-56 rounded-xl" aria-hidden />
        <Skeleton className="h-56 rounded-xl" aria-hidden />
      </div>
    </div>
  )
}

function StatisticsEmpty() {
  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <Inbox className="size-4 text-muted-foreground" aria-hidden />
      </div>
      <p className="font-medium">
        За этот период данных нет. Попробуйте расширить период.
      </p>
    </div>
  )
}

function StatisticsError({
  message,
  onRetry,
}: {
  message: string
  onRetry: () => void
}) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
        <TriangleAlert className="size-4 text-destructive" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">Не удалось загрузить статистику</p>
        <p
          role="alert"
          className="mx-auto max-w-sm text-sm text-muted-foreground"
        >
          {message}
        </p>
      </div>
      <Button size="sm" variant="outline" className="h-11 sm:h-8" onClick={onRetry}>
        Повторить
      </Button>
    </div>
  )
}
