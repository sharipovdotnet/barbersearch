import { Badge, badgeVariants } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import { BarberSectionHeading } from '@/components/barber/barber-section'
import type { BarberProfile } from '@/lib/catalog'
import { cn } from '@/lib/utils'
import { useI18n } from '@/i18n'

/**
 * Специализации: название — бейдж, описание — во всплывающей подсказке.
 *
 * Триггер подсказки — `TooltipTrigger` (Base UI рендерит `<button>`), поэтому
 * описание открывается и наведением, и фокусом с клавиатуры, а не только
 * мышью. Без описания бейдж не интерактивен. Пустой блок не показываем:
 * заголовок без данных — шум.
 */
export function BarberSpecializations({ profile }: { profile: BarberProfile }) {
  const { t } = useI18n()

  if (profile.specializations.length === 0) {
    return null
  }

  return (
    <section aria-labelledby="barber-specializations-heading">
      <Card>
        <BarberSectionHeading id="barber-specializations-heading">
          {t('Специализации')}
        </BarberSectionHeading>

        <CardContent>
          <TooltipProvider>
            <ul className="flex flex-wrap gap-2">
              {profile.specializations.map((specialization) => {
                const description = specialization.description?.trim() ?? ''

                return (
                  <li key={specialization.id}>
                    {description === '' ? (
                      <Badge variant="secondary">
                        {specialization.name}
                      </Badge>
                    ) : (
                      <Tooltip>
                        <TooltipTrigger
                          className={cn(
                            badgeVariants({ variant: 'secondary' }),
                            'cursor-help'
                          )}
                        >
                          {specialization.name}
                        </TooltipTrigger>
                        <TooltipContent className="max-w-72 text-sm leading-relaxed">
                          {description}
                        </TooltipContent>
                      </Tooltip>
                    )}
                  </li>
                )
              })}
            </ul>
          </TooltipProvider>
        </CardContent>
      </Card>
    </section>
  )
}
