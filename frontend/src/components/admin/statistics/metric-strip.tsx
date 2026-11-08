export type StatisticsMetric = {
  key: string
  label: string
  value: string
  /** Пояснение под значением: знаменатель, снимок или малый N. */
  hint?: string
  /** Готовая дельта; `null` — сравнение выключено или база нулевая. */
  delta?: string | null
}

/**
 * Шесть плиток обзора одной полосой, а не шестью карточками.
 *
 * Разделители — `gap-px` на фоне `bg-border`: это те же линии, что у `divide-x`,
 * но работают и в сетке, и в стопке на 320. Цвет не единственный носитель
 * смысла: знак дельты и подписи читаются текстом.
 */
export function MetricStrip({ metrics }: { metrics: StatisticsMetric[] }) {
  return (
    <dl className="grid grid-cols-1 gap-px overflow-hidden rounded-xl border border-border bg-border sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
      {metrics.map((metric) => (
        <div key={metric.key} className="min-w-0 bg-background p-4">
          <dt className="text-sm text-muted-foreground">{metric.label}</dt>
          <dd className="mt-1">
            <span className="block font-heading text-xl font-semibold break-words tabular-nums">
              {metric.value}
            </span>
            {metric.delta !== undefined && metric.delta !== null && (
              <span className="mt-1 block text-xs text-muted-foreground tabular-nums">
                {metric.delta}{' '}
                <span className="sr-only">к прошлому периоду</span>
              </span>
            )}
            {metric.hint && (
              <span className="mt-1 block text-xs text-muted-foreground">
                {metric.hint}
              </span>
            )}
          </dd>
        </div>
      ))}
    </dl>
  )
}
