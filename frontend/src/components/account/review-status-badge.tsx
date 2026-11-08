import { Badge } from '@/components/ui/badge'
import { useI18n } from '@/i18n'
import { cn } from '@/lib/utils'

/**
 * Статус отзыва в списке автора.
 *
 * Точка и текст дублируют цвет: статус читается без цветового зрения. Отдельного
 * «скрыт администратором» нет — бэкенд хранит один boolean, поэтому `false` всегда
 * «на модерации».
 */
export function ReviewStatusBadge({
  isPublished,
  className,
}: {
  isPublished: boolean
  className?: string
}) {
  const { t } = useI18n()

  return (
    <Badge
      variant="outline"
      className={cn(
        'gap-1.5 font-medium',
        isPublished ? 'text-success' : 'text-muted-foreground',
        className
      )}
    >
      <span aria-hidden className="size-1.5 rounded-full bg-current" />
      {isPublished ? t('Опубликован') : t('На модерации')}
    </Badge>
  )
}
