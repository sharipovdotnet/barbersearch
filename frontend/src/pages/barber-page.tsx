import { useEffect } from 'react'
import { ArrowLeft } from 'lucide-react'

import { BarberAbout } from '@/components/barber/barber-about'
import { BarberHero } from '@/components/barber/barber-hero'
import { BarberSalons } from '@/components/barber/barber-salons'
import { BarberServices } from '@/components/barber/barber-services'
import { BarberSlots } from '@/components/barber/barber-slots'
import { BarberSpecializations } from '@/components/barber/barber-specializations'
import {
  BarberProfileError,
  BarberProfileNotFound,
  BarberProfileSkeleton,
} from '@/components/barber/barber-state'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { useBarberProfile } from '@/hooks/use-barber-profile'
import { PAYMENT_METHOD_LABELS } from '@/lib/barber-profile-labels'
import type { BarberProfile } from '@/lib/catalog'
import { goToSection, isPlainClick } from '@/lib/router'

/**
 * Публичная карточка барбера.
 *
 * Свободное время и запись живут в `BarberSlots`: клиент выбирает слот и отправляет
 * бронь, не уходя со страницы. Список отзывов на этом этапе не показывается. Страница
 * собирается из секций, а состояния профиля (загрузка, ошибка, скрытый профиль) живут
 * в `use-barber-profile`.
 */
export function BarberPage({ id }: { id: string }) {
  const { profile, status, error, retry } = useBarberProfile(id)
  const title = profile
    ? `${profile.displayName} — BarberSearch`
    : 'Барбер — BarberSearch'

  useEffect(() => {
    document.title = title
  }, [title])

  return (
    <div className="mx-auto max-w-5xl px-4 py-10">
      <a
        href="/#barbers"
        className="inline-flex items-center gap-1.5 rounded-md text-sm text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
        onClick={(event) => {
          if (!isPlainClick(event)) {
            return
          }

          event.preventDefault()
          goToSection('barbers')
        }}
      >
        <ArrowLeft className="size-4" aria-hidden />
        К поиску барберов
      </a>

      {status === 'loading' && <BarberProfileSkeleton />}

      {status === 'notFound' && <BarberProfileNotFound />}

      {status === 'error' && (
        <BarberProfileError
          message={error ?? 'Попробуйте ещё раз.'}
          onRetry={retry}
        />
      )}

      {status === 'ready' && profile && (
        <div className="mt-6 space-y-6">
          <BarberHero profile={profile} />

          <div className="grid gap-6 lg:grid-cols-3">
            <div className="space-y-6 lg:col-span-2">
              <BarberSlots profile={profile} />
              <BarberAbout profile={profile} />
              <BarberServices profile={profile} />
              <BarberSpecializations profile={profile} />
              <BarberSalons profile={profile} />
            </div>

            <BarberTerms profile={profile} />
          </div>
        </div>
      )}
    </div>
  )
}

/** Оплата и правило опозданий; при пустых данных колонки нет вовсе. */
function BarberTerms({ profile }: { profile: BarberProfile }) {
  const hasPayments = profile.paymentMethods.length > 0
  const latePolicy = profile.latePolicy.trim()

  if (!hasPayments && latePolicy === '') {
    return null
  }

  return (
    <aside aria-label="Условия" className="space-y-6">
      {hasPayments && (
        <Card>
          <CardContent className="space-y-3">
            <h2 className="font-heading text-base leading-snug font-medium tracking-tight">
              Оплата
            </h2>
            <ul className="flex flex-wrap gap-2">
              {profile.paymentMethods.map((method) => (
                <li key={method}>
                  <Badge variant="secondary">
                    {PAYMENT_METHOD_LABELS[method]}
                  </Badge>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}

      {latePolicy !== '' && (
        <Card>
          <CardContent className="space-y-3">
            <h2 className="font-heading text-base leading-snug font-medium tracking-tight">
              Правила визита
            </h2>
            <p className="text-sm leading-relaxed break-words">
              Опоздания: {latePolicy}
            </p>
          </CardContent>
        </Card>
      )}
    </aside>
  )
}
