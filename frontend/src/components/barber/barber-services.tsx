import { Clock } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { BarberSectionHeading } from '@/components/barber/barber-section'
import { SERVICE_CATEGORY_LABELS } from '@/lib/barber-profile-labels'
import type { BarberProfile } from '@/lib/catalog'
import { formatDuration, formatPrice } from '@/lib/format'

/**
 * Прайс мастера: цена и длительность конкретной услуги.
 *
 * Кнопок записи здесь нет: слоты и `POST /api/bookings` — следующий этап,
 * а кнопка-пустышка хуже, чем её отсутствие.
 */
export function BarberServices({ profile }: { profile: BarberProfile }) {
  const services = profile.services.filter((service) => service.isActive)

  return (
    <section aria-labelledby="barber-services-heading">
      <Card>
        <BarberSectionHeading id="barber-services-heading">
          Услуги
        </BarberSectionHeading>

        <CardContent>
          {services.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              Услуги пока не добавлены.
            </p>
          ) : (
            <ul className="divide-y">
              {services.map((service) => {
                const duration = formatDuration(service.durationMinutes)

                return (
                  <li
                    key={service.barberServiceId}
                    className="flex flex-col gap-1 py-3 first:pt-0 last:pb-0 sm:flex-row sm:items-start sm:justify-between sm:gap-6"
                  >
                    <div className="min-w-0 space-y-1.5">
                      <p className="font-medium break-words">{service.name}</p>
                      <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
                        <Badge variant="outline">
                          {SERVICE_CATEGORY_LABELS[service.category]}
                        </Badge>
                        {duration !== '' && (
                          <span className="inline-flex items-center gap-1">
                            <Clock className="size-3.5" aria-hidden />
                            {duration}
                          </span>
                        )}
                      </p>
                    </div>
                    <p className="font-medium tabular-nums sm:shrink-0">
                      {formatPrice(service.price)}
                    </p>
                  </li>
                )
              })}
            </ul>
          )}
        </CardContent>
      </Card>
    </section>
  )
}
