import { Loader2, RefreshCw } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { formatClockTime } from '@/lib/format'

/**
 * Строка «Обновлено в HH:MM» и кнопка «Обновить».
 *
 * Та же пара, что на админских экранах, но вынесена в компонент: кабинеты опрашивают
 * сервер сами, и подпись должна обновляться без перезагрузки страницы.
 *
 * Кнопка не блокируется на время запроса намеренно: фоновое обновление идёт каждые
 * полминуты, и `disabled` выбивал бы фокус у клавиатурного пользователя. Повторное
 * нажатие безопасно — контроллер страницы отменяет прошлый запрос.
 */
export function RefreshStatus({
  updatedAt,
  isRefreshing,
  onRefresh,
}: {
  /** Когда страница последний раз пришла с сервера; `null` — ещё ни разу. */
  updatedAt: number | null
  isRefreshing: boolean
  onRefresh: () => void
}) {
  return (
    <div className="flex flex-wrap items-center justify-end gap-x-3 gap-y-1">
      {updatedAt !== null && (
        <p className="text-xs text-muted-foreground tabular-nums">
          Обновлено в {formatClockTime(new Date(updatedAt))}
        </p>
      )}

      <Button
        type="button"
        variant="outline"
        size="sm"
        className="h-11 sm:h-8"
        onClick={onRefresh}
      >
        {isRefreshing ? (
          <Loader2 data-icon="inline-start" aria-hidden className="animate-spin" />
        ) : (
          <RefreshCw data-icon="inline-start" aria-hidden />
        )}
        {isRefreshing ? 'Обновляем…' : 'Обновить'}
      </Button>
    </div>
  )
}
