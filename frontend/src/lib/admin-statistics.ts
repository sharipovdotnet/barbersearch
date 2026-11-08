import { getLocale, localeTag, t } from '@/i18n'
import { ApiError, apiRequest } from '@/lib/api'
import { describeApiFailure } from '@/lib/errors'
import { isAdminStatistics } from '@/lib/response-guards'

/**
 * Админская статистика сервиса (`GET api/admin/statistics`).
 *
 * Контракт типизирован по секциям: экран не собирает KPI из массива. Доли приходят
 * числами `0..1` или `null`, когда знаменатель нулевой; деньги — числом и валютой
 * `RUB` (в домене валюты нет, поэтому подпись честная: «оборот», а не «выручка»).
 *
 * Период — UTC-сутки, обе даты включительно, максимум 366 дней. Экран держит его
 * в URL (`?from=&to=&compare=1`), а неверные или слишком длинные параметры
 * заменяются дефолтом в 30 дней без ошибки: сервер всё равно валидирует период.
 */

/** Границы периода в ISO-датах и режим сравнения с прошлым периодом. */
export type AdminStatisticsPeriod = {
  from: string
  to: string
  compare: boolean
}

/** Период в ответе: длина, пояс и прошлый период при `compare=true`. */
export type AdminStatisticsPeriodInfo = {
  from: string
  to: string
  days: number
  timeZone: string
  previous: { from: string; to: string } | null
}

export type AdminStatisticsCountMetric = {
  value: number
  previous: number | null
}

export type AdminStatisticsMoneyMetric = {
  value: number
  previous: number | null
  currency: string
}

export type AdminStatisticsAverageMetric = {
  value: number | null
  previous: number | null
}

export type AdminStatisticsNoShowMetric = {
  value: number | null
  previous: number | null
  count: number
  sample: number
}

export type AdminStatisticsStuckMetric = {
  value: number
}

export type AdminStatisticsOverview = {
  completedVisits: AdminStatisticsCountMetric
  turnover: AdminStatisticsMoneyMetric
  averageCheck: AdminStatisticsAverageMetric
  newAccounts: AdminStatisticsCountMetric
  noShowRate: AdminStatisticsNoShowMetric
  stuckBookings: AdminStatisticsStuckMetric
}

export type AdminStatisticsSeriesPoint = {
  date: string
  created: number
  completed: number
  cancelled: number
  noShow: number
}

export type AdminStatisticsSeries = {
  granularity: 'day' | 'week'
  points: AdminStatisticsSeriesPoint[]
}

export type AdminStatisticsStatuses = {
  pending: number
  confirmed: number
  completed: number
  cancelled: number
  noShow: number
  due: number
  closed: number
}

export type AdminStatisticsClients = {
  withVisit: number
  returning: number
  firstVisit: number
  guestBookings: number
  guestShare: number | null
  reviewRate: number | null
}

export type AdminStatisticsSalonKind = {
  kind: string
  listed: number
  unlisted: number
}

export type AdminStatisticsSupply = {
  activeBarbers: number
  workingBarbers: number
  idleBarbers: number
  newProfiles: number
  salonsByKind: AdminStatisticsSalonKind[]
}

export type AdminStatisticsTopOther = {
  visits: number
  turnover: number
}

export type AdminStatisticsBarberTopRow = {
  barberProfileId: string
  displayName: string
  visits: number
  turnover: number
  averageCheck: number
  noShow: number
  rating: number | null
  reviewsCount: number
}

export type AdminStatisticsServiceTopRow = {
  hairServiceId: string
  name: string
  visits: number
  turnover: number
  averageCheck: number
}

export type AdminStatisticsCityTopRow = {
  name: string
  visits: number
  turnover: number
  averageCheck: number
  activeBarbers: number
}

export type AdminStatisticsTop = {
  barbers: {
    rows: AdminStatisticsBarberTopRow[]
    other: AdminStatisticsTopOther
  }
  services: {
    rows: AdminStatisticsServiceTopRow[]
    other: AdminStatisticsTopOther
  }
  cities: {
    rows: AdminStatisticsCityTopRow[]
    other: AdminStatisticsTopOther
  }
}

export type AdminStatisticsModeration = {
  submitted: number
  approved: number
  rejected: number
  approvalRate: number | null
  medianDecisionHours: number | null
}

export type AdminStatisticsQuality = {
  rating: number | null
  reviews: number
  distribution: number[]
  publishedShare: number | null
  requests: AdminStatisticsModeration
  salons: AdminStatisticsModeration
}

export type AdminStatistics = {
  period: AdminStatisticsPeriodInfo
  generatedAt: string
  overview: AdminStatisticsOverview
  series: AdminStatisticsSeries
  statuses: AdminStatisticsStatuses
  clients: AdminStatisticsClients
  supply: AdminStatisticsSupply
  top: AdminStatisticsTop
  quality: AdminStatisticsQuality
}

/** Предел периода: год включительно. */
export const STATISTICS_MAX_DAYS = 366

/** Пресеты периода: ровно столько дней, включая сегодня. */
export const STATISTICS_PRESETS = [
  { days: 7, label: '7 дней' },
  { days: 30, label: '30 дней' },
  { days: 90, label: '90 дней' },
  { days: 365, label: 'Год' },
] as const

const DAY_MS = 24 * 60 * 60 * 1000
const ISO_DAY = /^\d{4}-\d{2}-\d{2}$/

/**
 * Форматтеры дат периода кэшируются отдельно для каждого языка: месяц в подписи
 * оси времени и диапазона — часть интерфейса, а не данные из API.
 */
const dayMonthFormatters = new Map<string, Intl.DateTimeFormat>()
const dayMonthYearFormatters = new Map<string, Intl.DateTimeFormat>()

function dayMonthFormatter(): Intl.DateTimeFormat {
  const tag = localeTag(getLocale())
  const cached = dayMonthFormatters.get(tag)

  if (cached) {
    return cached
  }

  const created = new Intl.DateTimeFormat(tag, {
    day: 'numeric',
    month: 'short',
    timeZone: 'UTC',
  })

  dayMonthFormatters.set(tag, created)

  return created
}

function dayMonthYearFormatter(): Intl.DateTimeFormat {
  const tag = localeTag(getLocale())
  const cached = dayMonthYearFormatters.get(tag)

  if (cached) {
    return cached
  }

  const created = new Intl.DateTimeFormat(tag, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  })

  dayMonthYearFormatters.set(tag, created)

  return created
}

function parseDay(date: string): Date {
  return new Date(`${date}T00:00:00Z`)
}

/** ISO-день `yyyy-MM-dd` и существующая календарная дата. */
export function isStatisticsDay(value: string): boolean {
  if (!ISO_DAY.test(value)) {
    return false
  }

  return parseDay(value).toISOString().slice(0, 10) === value
}

/** Сколько дней в периоде, включая обе границы; `null` для кривых дат. */
export function statisticsDays(from: string, to: string): number | null {
  if (!isStatisticsDay(from) || !isStatisticsDay(to)) {
    return null
  }

  const days = Math.round((parseDay(to).getTime() - parseDay(from).getTime()) / DAY_MS) + 1

  return days < 1 ? null : days
}

/** Сегодняшняя дата в UTC: период — UTC-сутки, а не дни браузера. */
export function todayUtc(): string {
  return new Date().toISOString().slice(0, 10)
}

/** Сдвиг даты на дни в UTC. */
export function addUtcDays(date: string, days: number): string {
  const value = parseDay(date)

  value.setUTCDate(value.getUTCDate() + days)

  return value.toISOString().slice(0, 10)
}

/** Дефолт: последние 30 дней, включая сегодня. */
export function defaultStatisticsPeriod(): AdminStatisticsPeriod {
  const to = todayUtc()

  return { from: addUtcDays(to, -29), to, compare: false }
}

/**
 * Период пресета: последние `days` дней, включая сегодня, без сравнения.
 *
 * Сравнение — отдельный флажок, а не часть пресета: пользователь может включить
 * его поверх любого периода.
 */
export function statisticsPresetPeriod(days: number): AdminStatisticsPeriod {
  const to = todayUtc()

  return { from: addUtcDays(to, -(days - 1)), to, compare: false }
}

/**
 * Нормализует период из полей формы.
 *
 * Пустая или кривая дата, обратный порядок и период длиннее 366 дней дают дефолт
 * в 30 дней без сообщения об ошибке: это подсказка URL и формы, а настоящую
 * валидацию всё равно делает сервер.
 */
export function resolveStatisticsPeriod(
  from: string,
  to: string,
  compare: boolean
): AdminStatisticsPeriod {
  const days = statisticsDays(from, to)

  if (days === null || days > STATISTICS_MAX_DAYS) {
    return { ...defaultStatisticsPeriod(), compare }
  }

  return { from, to, compare }
}

/** Разбирает `?from=&to=&compare=1`; неверное — дефолт 30 дней. */
export function parseStatisticsPeriod(search: string): AdminStatisticsPeriod {
  const params = new URLSearchParams(search)
  const from = params.get('from') ?? ''
  const to = params.get('to') ?? ''
  const value = params.get('compare')
  const compare = value === '1' || value === 'true'

  return resolveStatisticsPeriod(from, to, compare)
}

/** Адрес раздела: период в query, `compare` только когда включён. */
export function statisticsPath(period: AdminStatisticsPeriod): string {
  const params = new URLSearchParams({ from: period.from, to: period.to })

  if (period.compare) {
    params.set('compare', '1')
  }

  return `/admin/statistics?${params.toString()}`
}

function withoutYearSuffix(value: string): string {
  return value.replace(/\s*г\.$/, '')
}

/** «10 окт. — 8 нояб. 2026» — подпись строки периода. */
export function formatStatisticsRange(period: AdminStatisticsPeriod): string {
  const from = parseDay(period.from)
  const to = parseDay(period.to)
  const sameYear = from.getUTCFullYear() === to.getUTCFullYear()

  const left = sameYear
    ? dayMonthFormatter().format(from)
    : withoutYearSuffix(dayMonthYearFormatter().format(from))
  const right = withoutYearSuffix(dayMonthYearFormatter().format(to))

  return `${left} — ${right}`
}

/** «10 окт.» — подпись оси времени. */
export function formatStatisticsDay(value: string): string {
  return dayMonthFormatter().format(parseDay(value))
}

/**
 * Пустой период: ни записей, ни аккаунтов, ни отзывов, ни заявок.
 *
 * Нули в плитках при этом остаются честными нулями — пустое состояние заменяет
 * только секции ниже, а не сами счётчики.
 */
export function isEmptyStatistics(statistics: AdminStatistics): boolean {
  const { overview, clients, quality, supply, top } = statistics

  return (
    overview.completedVisits.value === 0
    && overview.turnover.value === 0
    && overview.newAccounts.value === 0
    && clients.guestBookings === 0
    && (quality.rating === null || quality.reviews === 0)
    && quality.requests.submitted === 0
    && quality.salons.submitted === 0
    && supply.newProfiles === 0
    && top.barbers.rows.length === 0
    && top.services.rows.length === 0
    && top.cities.rows.length === 0
    && statistics.series.points.every((point) => point.created === 0)
  )
}

/** Читает статистику за период; `signal` отменяет запрос при смене периода. */
export async function fetchAdminStatistics(
  period: AdminStatisticsPeriod,
  signal?: AbortSignal
): Promise<AdminStatistics> {
  const params = new URLSearchParams({ from: period.from, to: period.to })

  if (period.compare) {
    params.set('compare', 'true')
  }

  return apiRequest<AdminStatistics>(
    `/admin/statistics?${params.toString()}`,
    { signal },
    { validate: isAdminStatistics }
  )
}

/**
 * Русский текст ошибки статистики: `403` — роль, `429` — частота, `400` — период.
 *
 * Английский `detail` не показывается: неизвестный сбой уходит в запасной текст.
 */
export function describeAdminStatisticsError(
  cause: unknown,
  fallback: string
): string {
  if (!(cause instanceof ApiError)) {
    return t(fallback)
  }

  if (cause.status === 403 || cause.hasCode('auth.forbidden')) {
    return t('Раздел доступен только администратору.')
  }

  if (cause.status === 429) {
    return t('Слишком много запросов. Подождите минуту и повторите.')
  }

  if (cause.status === 400 || cause.hasCode('admin.statistics.period')) {
    return t('Проверьте период: максимум — 366 дней.')
  }

  return describeApiFailure(cause, fallback)
}
