import { MapPin, Star } from 'lucide-react'

import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { BARBER_GRADE_LABELS } from '@/lib/barber-grade'
import type { BarberSearchItem } from '@/lib/barber-search'
import {
  formatExperience,
  formatRating,
  formatReviewsCount,
  getInitials,
} from '@/lib/format'

/**
 * Карточка результата поиска.
 *
 * Цены, услуг и кнопки записи здесь нет: DTO поиска их не отдаёт, а кнопка-пустышка
 * хуже, чем её отсутствие. Переход в карточку барбера появится на следующем этапе.
 */
export function BarberCard({ barber }: { barber: BarberSearchItem }) {
  const location = [barber.primarySalonName, barber.city]
    .filter(Boolean)
    .join(' · ')

  return (
    <Card className="h-full">
      <CardHeader>
        <div className="flex items-start gap-3">
          <Avatar size="lg" aria-hidden>
            <AvatarFallback>{getInitials(barber.displayName)}</AvatarFallback>
          </Avatar>
          <div className="min-w-0 flex-1">
            <CardTitle className="break-words">{barber.displayName}</CardTitle>
            <p className="mt-1 flex items-start gap-1 text-sm text-muted-foreground">
              <MapPin className="mt-0.5 size-3.5 shrink-0" aria-hidden />
              <span className="min-w-0 break-words">
                {location || 'Салон не указан'}
              </span>
            </p>
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="outline">{BARBER_GRADE_LABELS[barber.grade]}</Badge>
          <p className="flex items-center gap-1.5 text-sm">
            <Star className="size-3.5 fill-current" aria-hidden />
            {barber.rating === null ? (
              <span className="text-muted-foreground">Пока нет отзывов</span>
            ) : (
              <>
                <span className="font-medium">
                  {formatRating(barber.rating)}
                </span>
                <span className="text-muted-foreground">
                  · {formatReviewsCount(barber.reviewsCount)}
                </span>
              </>
            )}
          </p>
        </div>

        <p className="text-sm text-muted-foreground">
          Стаж: {formatExperience(barber.yearsOfExperience)}
        </p>
      </CardContent>
    </Card>
  )
}
