import { Card, CardContent } from '@/components/ui/card'
import { BarberSectionHeading } from '@/components/barber/barber-section'
import type { BarberProfile } from '@/lib/catalog'

/** Специализации с описаниями. Пустой блок не показываем: заголовок без данных — шум. */
export function BarberSpecializations({ profile }: { profile: BarberProfile }) {
  if (profile.specializations.length === 0) {
    return null
  }

  return (
    <section aria-labelledby="barber-specializations-heading">
      <Card>
        <BarberSectionHeading id="barber-specializations-heading">
          Специализации
        </BarberSectionHeading>

        <CardContent>
          <ul className="divide-y">
            {profile.specializations.map((specialization) => (
              <li
                key={specialization.id}
                className="space-y-1 py-3 first:pt-0 last:pb-0"
              >
                <p className="font-medium break-words">
                  {specialization.name}
                </p>
                {specialization.description && (
                  <p className="text-sm leading-relaxed break-words text-muted-foreground">
                    {specialization.description}
                  </p>
                )}
              </li>
            ))}
          </ul>
        </CardContent>
      </Card>
    </section>
  )
}
