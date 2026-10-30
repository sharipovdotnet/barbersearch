import { Star } from 'lucide-react'

const STAR_VALUES = [1, 2, 3, 4, 5] as const

/** Пять звёзд с текстовой альтернативой: оценка не передаётся только цветом. */
export function ReviewStars({ rating }: { rating: number }) {
  const filled = Math.min(5, Math.max(0, Math.round(rating)))

  return (
    <span
      role="img"
      aria-label={`Оценка ${filled} из 5`}
      className="inline-flex items-center gap-0.5"
    >
      {STAR_VALUES.map((value) => (
        <Star
          key={value}
          aria-hidden
          className={
            value <= filled
              ? 'size-3.5 fill-current text-warning'
              : 'size-3.5 text-muted-foreground/30'
          }
        />
      ))}
    </span>
  )
}
