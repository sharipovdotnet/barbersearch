import { BadgeCheck, MapPin, Star } from 'lucide-react'

import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import type { BarberProfile } from '@/lib/catalog'
import { BARBER_GRADE_LABELS } from '@/lib/barber-grade'
import { useI18n } from '@/i18n'
import {
  formatExperience,
  formatRating,
  formatReviewsCount,
  getInitials,
} from '@/lib/format'
import { avatarImageSource } from '@/lib/media'
import { placeKindCaption } from '@/lib/place-kind'

/**
 * Шапка публичной карточки.
 *
 * Раньше аватара в контракте не было — карточка показывала только инициалы. Теперь
 * `avatarUrl` приходит с сервера, а инициалы остаются fallback'ом: пока файл грузится
 * или если аватара нет. Основной салон показывается здесь, а полный адрес и
 * расписание живут в секции салонов.
 */
export function BarberHero({ profile }: { profile: BarberProfile }) {
  const { t } = useI18n()
  const primarySalon =
    profile.salons.find((salon) => salon.isPrimary) ?? profile.salons[0] ?? null
  const location = primarySalon
    ? placeKindCaption(primarySalon.kind, primarySalon.name, primarySalon.city)
    : t('Салон не указан')

  return (
    <Card>
      <CardContent className="flex flex-col gap-4 sm:flex-row sm:items-start">
        <Avatar size="lg" className="size-16" aria-hidden>
          {profile.avatarUrl && (
            <AvatarImage {...avatarImageSource(profile.avatarUrl)} alt="" />
          )}
          <AvatarFallback className="text-lg">
            {getInitials(profile.displayName)}
          </AvatarFallback>
        </Avatar>

        <div className="min-w-0 flex-1 space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="font-heading text-2xl leading-tight font-semibold tracking-tight text-balance break-words">
              {profile.displayName}
            </h1>
            <Badge variant="outline">
              {t(BARBER_GRADE_LABELS[profile.grade])}
            </Badge>
            {profile.isVerified && (
              <Badge variant="secondary">
                <BadgeCheck aria-hidden />
                {t('Проверен')}
              </Badge>
            )}
          </div>

          <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
            {profile.rating === null ? (
              <span className="text-muted-foreground">
                {t('Пока нет отзывов')}
              </span>
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
            {t('Стаж:')} {formatExperience(profile.yearsOfExperience)}
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
