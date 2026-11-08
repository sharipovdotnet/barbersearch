import type { ReactNode } from 'react'

import { cn } from '@/lib/utils'

/**
 * Секция статистики: один `h2` и описание.
 *
 * Заголовок связан с секцией через `aria-labelledby`, поэтому экранный диктор
 * называет регион так же, как видит его зрячий пользователь.
 */
export function StatSection({
  id,
  title,
  description,
  children,
  className,
}: {
  id: string
  title: string
  description?: string
  children: ReactNode
  className?: string
}) {
  return (
    <section
      aria-labelledby={`${id}-heading`}
      className={cn('space-y-3', className)}
    >
      <div className="space-y-1">
        <h2
          id={`${id}-heading`}
          className="font-heading text-lg font-semibold tracking-tight"
        >
          {title}
        </h2>
        {description && (
          <p className="max-w-3xl text-sm text-muted-foreground">{description}</p>
        )}
      </div>
      {children}
    </section>
  )
}
