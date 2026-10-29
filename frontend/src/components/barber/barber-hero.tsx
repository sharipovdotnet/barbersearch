import { BadgeCheck, MapPin, Star } from 'lucide-react'

import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import type { BarberProfile } from '@/lib/catalog'
import { BARBER_GRADE_LABELS } from '@/lib/barber-grade'
import {
  formatExperience,
  formatRating,
  formatReviewsCount,
  getInitials,
} from '@/lib/format'

/**
 * Шапка публичной карточки.
 *
 * Аватара в контракте нет — только инициалы. Основной салон показывается здесь,
 * а полный адрес и расписание живут в секции салонов.
 */
export function BarberHero({ profile }: { profile: BarberProfile }) {
  const primarySalon =
    profile.salons.find((salon) => salon.isPrimary) ?? profile.salons[0] ?? null
  const location = primarySalon
    ? [primarySalon.name, primarySalon.city].filter(Boolean).join(' · ')
    : 'Салон не указан'

  return (
    <Card>
      <CardContent className="flex flex-col gap-4 sm:flex-row sm:items-start">
        <Avatar size="lg" className="size-16" aria-hidden>
          <AvatarFallback className="text-lg">
            {getInitials(profile.displayName)}
          </AvatarFallback>
        </Avatar>

        <div className="min-w-0 flex-1 space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="font-heading text-2xl leading-tight font-semibold tracking-tight text-balance break-words">
              {profile.displayName}
            </h1>
            <Badge variant="outline">{BARBER_GRADE_LABELS[profile.grade]}</Badge>
            {profile.isVerified && (
              <Badge variant="secondary">
                <BadgeCheck aria-hidden />
                Проверен
              </Badge>
            )}
          </div>

          <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
            {profile.rating === null ? (
              <span className="text-muted-foreground">Пока нет отзывов</span>
            ) : (
              <>
                <span className="inline-flex items-center gap-1.5">
                  <Star className="size-3.5 fill-current" aria-hidden />
                  <span className="font-medium tabular-nums">
                    {formatRating(profile.rating)}
                  </span>
                </span>
                <span className="text-muted-foreground">
                  · {formatReviewsCount(profile.reviewsCount)}
                </span>
              </>
            )}
          </p>

          <p className="text-sm text-muted-foreground">
            Стаж: {formatExperience(profile.yearsOfExperience)}
          </p>

          <p className="flex items-start gap-1 text-sm text-muted-foreground">
            <MapPin className="mt-0.5 size-3.5 shrink-0" aria-hidden />
            <span className="min-w-0 break-words">{location}</span>
          </p>
        </div>
      </CardContent>
    </Card>
  )
}
