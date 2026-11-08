import type { AdminStatisticsStatuses } from '@/lib/admin-statistics'
import { formatNumber } from '@/lib/format'

type Segment = {
  key: string
  label: string
  value: number
  /** Класс заливки SVG: тот же токен, что у точки легенды. */
  fill: string
  /** Класс точки легенды; задан явно, чтобы Tailwind видел имя целиком. */
  dot: string
}

/**
 * «Статусы визитов»: 100 % полоса и легенда с числами.
 *
 * Ширины считает SVG по `viewBox="0 0 100 8"` — без inline-стилей и без
 * библиотеки графиков. Полоса `aria-hidden`: смысл целиком в легенде, поэтому
 * цвет остаётся оформлением, а не единственным носителем значения.
 */
export function StatusBar({
  statuses,
}: {
  statuses: AdminStatisticsStatuses
}) {
  const segments: Segment[] = [
    {
      key: 'completed',
      label: 'Завершены',
      value: statuses.completed,
      fill: 'fill-success',
      dot: 'bg-success',
    },
    {
      key: 'cancelled',
      label: 'Отменены',
      value: statuses.cancelled,
      fill: 'fill-warning',
      dot: 'bg-warning',
    },
    {
      key: 'noShow',
      label: 'Неявки',
      value: statuses.noShow,
      fill: 'fill-destructive',
      dot: 'bg-destructive',
    },
    {
      key: 'pending',
      label: 'Ждут подтверждения',
      value: statuses.pending,
      fill: 'fill-chart-3',
      dot: 'bg-chart-3',
    },
    {
      key: 'confirmed',
      label: 'Подтверждены',
      value: statuses.confirmed,
      fill: 'fill-chart-2',
      dot: 'bg-chart-2',
    },
  ]

  const total = segments.reduce((sum, segment) => sum + segment.value, 0)
  let offset = 0

  return (
    <div className="space-y-3">
      <div className="overflow-hidden rounded-full bg-muted">
        <svg
          viewBox="0 0 100 8"
          preserveAspectRatio="none"
          className="h-3 w-full"
          aria-hidden
        >
          {total > 0 &&
            segments.map((segment) => {
              const width = (segment.value / total) * 100
              const element = (
                <rect
                  key={segment.key}
                  x={offset}
                  y={0}
                  width={width}
                  height={8}
                  className={segment.fill}
                />
              )

              offset += width

              return element
            })}
        </svg>
      </div>

      <dl className="grid gap-2 text-sm sm:grid-cols-2">
        {segments.map((segment) => (
          <div key={segment.key} className="flex items-center justify-between gap-2">
            <dt className="flex min-w-0 items-center gap-2">
              <span
                className={`size-2.5 shrink-0 rounded-full ${segment.dot}`}
                aria-hidden
              />
              <span className="min-w-0 break-words">{segment.label}</span>
            </dt>
            <dd className="shrink-0 tabular-nums">{formatNumber(segment.value)}</dd>
          </div>
        ))}
      </dl>

      <p className="text-sm">
        Закрыто визитов:{' '}
        <span className="font-medium tabular-nums">
          {statuses.due === 0
            ? '—'
            : `${formatNumber(statuses.closed)} из ${formatNumber(statuses.due)}`}
        </span>
      </p>
    </div>
  )
}
