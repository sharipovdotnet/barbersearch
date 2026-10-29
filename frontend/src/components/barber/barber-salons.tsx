import { MapPin } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'
import { BarberSectionHeading } from '@/components/barber/barber-section'
import {
  DAY_OF_WEEK_LABELS,
  DAY_OF_WEEK_ORDER,
} from '@/lib/barber-profile-labels'
import type { BarberProfile, BarberProfileSalon } from '@/lib/catalog'
import { formatScheduleTime } from '@/lib/format'

/**
 * Салоны мастера и его недельное расписание.
 *
 * Это не свободные слоты: `isDayOff` — выходной, а время уже локальное для салона,
 * поэтому пересчитывать его в пояс браузера нельзя. Выходной подписан словом,
 * а не только цветом.
 */
export function BarberSalons({ profile }: { profile: BarberProfile }) {
  return (
    <section aria-labelledby="barber-salons-heading">
      <Card>
        <BarberSectionHeading id="barber-salons-heading">
          Салоны
        </BarberSectionHeading>

        <CardContent>
          {profile.salons.length === 0 ? (
            <p className="text-sm text-muted-foreground">Салон не указан.</p>
          ) : (
            <ul className="space-y-5">
              {profile.salons.map((salon, index) => (
                <li key={salon.id} className="space-y-3">
                  {index > 0 && <Separator />}
                  <SalonItem salon={salon} />
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
    </section>
  )
}

function SalonItem({ salon }: { salon: BarberProfileSalon }) {
  const address = [salon.address, salon.city, salon.country]
    .filter(Boolean)
    .join(', ')
  const schedules = [...salon.workingSchedules].sort(
    (left, right) =>
      DAY_OF_WEEK_ORDER.indexOf(left.dayOfWeek) -
      DAY_OF_WEEK_ORDER.indexOf(right.dayOfWeek)
  )

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2">
        <h3 className="font-medium break-words">{salon.name}</h3>
        {salon.isPrimary && <Badge variant="secondary">Основной салон</Badge>}
      </div>

      {address !== '' && (
        <p className="flex items-start gap-1 text-sm text-muted-foreground">
          <MapPin className="mt-0.5 size-3.5 shrink-0" aria-hidden />
          <span className="min-w-0 break-words">{address}</span>
        </p>
      )}

      {schedules.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          Расписание пока не заполнено.
        </p>
      ) : (
        <dl className="space-y-1 text-sm">
          {schedules.map((schedule) => (
            <div
              key={schedule.id}
              className="flex items-baseline justify-between gap-4"
            >
              <dt>{DAY_OF_WEEK_LABELS[schedule.dayOfWeek]}</dt>
              <dd
                className={
                  schedule.isDayOff
                    ? 'text-muted-foreground'
                    : 'tabular-nums'
                }
              >
                {schedule.isDayOff
                  ? 'Выходной'
                  : `${formatScheduleTime(schedule.startTime)}–${formatScheduleTime(schedule.endTime)}`}
              </dd>
            </div>
          ))}
        </dl>
      )}
    </div>
  )
}
