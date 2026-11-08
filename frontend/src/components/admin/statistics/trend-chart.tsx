import { useMediaQuery } from '@/hooks/use-media-query'
import type { AdminStatisticsSeries } from '@/lib/admin-statistics'
import { formatStatisticsDay } from '@/lib/admin-statistics'
import { formatCount } from '@/lib/format'

/**
 * Линейный график «Записи и визиты»: две линии, три линии сетки и 3–4 подписи
 * по X. Свои SVG-примитивы, без библиотеки графиков и без градиентов.
 *
 * На узком экране у графика своя, компактная система координат: иначе подписи
 * масштабировались бы вместе с `viewBox` и становились нечитаемыми. Цвет не
 * единственный носитель смысла: у линий есть легенда, а под графиком — текстовая
 * сводка и таблица-дубль в `<details>`. Сам `<svg>` помечен `role="img"` и
 * `aria-label`: диктор получает сводку, а не набор координат.
 */
export function TrendChart({ series }: { series: AdminStatisticsSeries }) {
  const isWide = useMediaQuery('(min-width: 640px)')
  const width = isWide ? 640 : 320
  const height = isWide ? 220 : 180
  const padTop = isWide ? 16 : 12
  const padRight = isWide ? 12 : 8
  const padBottom = isWide ? 28 : 24
  const padLeft = isWide ? 44 : 32
  const innerWidth = width - padLeft - padRight
  const innerHeight = height - padTop - padBottom
  const fontSize = isWide ? 11 : 12

  const points = series.points
  const isDay = series.granularity === 'day'
  const maxValue = Math.max(
    1,
    ...points.map((point) => Math.max(point.created, point.completed))
  )

  const x = (index: number) =>
    points.length === 1
      ? padLeft + innerWidth / 2
      : padLeft + (index * innerWidth) / (points.length - 1)
  const y = (value: number) => padTop + innerHeight - (value / maxValue) * innerHeight

  const line = (key: 'created' | 'completed') =>
    points.map((point, index) => `${x(index)},${y(point[key])}`).join(' ')

  const totalCreated = points.reduce((sum, point) => sum + point.created, 0)
  const totalCompleted = points.reduce((sum, point) => sum + point.completed, 0)
  const totalCancelled = points.reduce((sum, point) => sum + point.cancelled, 0)
  const totalNoShow = points.reduce((sum, point) => sum + point.noShow, 0)

  const candidates = isWide
    ? [
        0,
        Math.floor((points.length - 1) / 3),
        Math.floor((2 * (points.length - 1)) / 3),
        points.length - 1,
      ]
    : [0, Math.floor((points.length - 1) / 2), points.length - 1]

  const labelIndexes = Array.from(new Set(candidates.filter((index) => index >= 0)))

  return (
    <div className="space-y-2">
      <ul className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
        <li className="flex items-center gap-1.5">
          <span className="h-0.5 w-5 bg-chart-2" aria-hidden />
          Созданные записи
        </li>
        <li className="flex items-center gap-1.5">
          <span className="h-0.5 w-5 bg-success" aria-hidden />
          Завершённые визиты
        </li>
      </ul>

      <svg
        viewBox={`0 0 ${width} ${height}`}
        role="img"
        aria-label={`Записи и визиты по ${isDay ? 'дням' : 'неделям'}: создано ${totalCreated}, завершено ${totalCompleted}. Таблица под графиком.`}
        className="h-auto w-full"
      >
        {[1, 0.5, 0].map((fraction) => {
          const value = Math.round(maxValue * fraction)
          const position = padTop + innerHeight * (1 - fraction)

          return (
            <g key={fraction}>
              <line
                x1={padLeft}
                x2={width - padRight}
                y1={position}
                y2={position}
                stroke="currentColor"
                className="text-border"
              />
              <text
                x={padLeft - 6}
                y={position + 4}
                textAnchor="end"
                fontSize={fontSize}
                fill="currentColor"
                className="text-muted-foreground"
              >
                {value}
              </text>
            </g>
          )
        })}

        {labelIndexes.map((index) => (
          <text
            key={index}
            x={x(index)}
            y={height - 7}
            textAnchor={
              index === 0 ? 'start' : index === points.length - 1 ? 'end' : 'middle'
            }
            fontSize={fontSize}
            fill="currentColor"
            className="text-muted-foreground"
          >
            {formatStatisticsDay(points[index].date)}
          </text>
        ))}

        <polyline
          points={line('created')}
          fill="none"
          stroke="currentColor"
          strokeWidth={2}
          className="text-chart-2"
        />
        <polyline
          points={line('completed')}
          fill="none"
          stroke="currentColor"
          strokeWidth={2}
          className="text-success"
        />
      </svg>

      <p className="text-sm text-muted-foreground">
        Создано {formatCount(totalCreated, ['запись', 'записи', 'записей'])},
        завершено {formatCount(totalCompleted, ['визит', 'визита', 'визитов'])}.
        Отменено {totalCancelled}, неявки {totalNoShow}.
      </p>

      <p className="text-xs text-muted-foreground">
        {isDay
          ? 'Сегодняшний день может быть неполным: записи появляются по мере визитов.'
          : 'Недели начинаются в понедельник (UTC); первый и последний бакеты могут быть неполными.'}
      </p>

      <details className="rounded-lg border">
        <summary className="flex min-h-11 cursor-pointer items-center px-3 py-2 text-sm font-medium outline-none focus-visible:ring-3 focus-visible:ring-ring/50">
          Показать таблицей
        </summary>
        <div className="overflow-x-auto border-t">
          <table className="w-full text-sm">
            <caption className="sr-only">
              Записи и визиты по {isDay ? 'дням' : 'неделям'}
            </caption>
            <thead>
              <tr className="border-b text-left text-muted-foreground">
                <th scope="col" className="px-3 py-2 font-medium">
                  {isDay ? 'День' : 'Неделя с'}
                </th>
                <th scope="col" className="px-3 py-2 text-right font-medium">
                  Создано
                </th>
                <th scope="col" className="px-3 py-2 text-right font-medium">
                  Завершено
                </th>
                <th scope="col" className="px-3 py-2 text-right font-medium">
                  Отменено
                </th>
                <th scope="col" className="px-3 py-2 text-right font-medium">
                  Неявки
                </th>
              </tr>
            </thead>
            <tbody>
              {points.map((point) => (
                <tr key={point.date} className="border-b last:border-b-0">
                  <th scope="row" className="px-3 py-2 text-left font-normal">
                    {formatStatisticsDay(point.date)}
                  </th>
                  <td className="px-3 py-2 text-right tabular-nums">{point.created}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{point.completed}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{point.cancelled}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{point.noShow}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </div>
  )
}
