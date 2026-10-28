import type { BookingStatus } from '@/lib/bookings'
import { cn } from '@/lib/utils'

const STEPS = [
  { key: 'request', label: 'Заявка' },
  { key: 'confirm', label: 'Подтверждение' },
  { key: 'visit', label: 'Визит' },
] as const

/**
 * Путь брони от заявки до визита.
 *
 * Для `Pending` пройден только первый шаг, для `Confirmed` — два. Терминальные
 * статусы сюда не попадают: отменённую запись рисует бейдж, а не трекер прогресса,
 * иначе он читается как «визит ещё будет».
 */
export function StatusTimeline({ status }: { status: BookingStatus }) {
  const reached = status === 'Confirmed' ? 2 : 1

  return (
    <ol className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs">
      {STEPS.map((step, index) => {
        const isReached = index < reached

        return (
          <li key={step.key} className="flex items-center gap-2">
            <span
              aria-hidden
              className={cn(
                'size-1.5 rounded-full',
                isReached ? 'bg-foreground' : 'bg-border'
              )}
            />
            <span
              className={cn(
                isReached ? 'font-medium text-foreground' : 'text-muted-foreground'
              )}
            >
              {step.label}
            </span>
            {index < STEPS.length - 1 && (
              <span aria-hidden className="h-px w-4 bg-border sm:w-6" />
            )}
          </li>
        )
      })}
    </ol>
  )
}
