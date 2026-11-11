import { useEffect, useState } from 'react'
import { Loader2, TriangleAlert, UserRound, X } from 'lucide-react'

import { SignInGate } from '@/components/account/account-gates'
import { BarberCabinetNav } from '@/components/barber/barber-cabinet-nav'
import {
  BarberCabinetSkeleton,
  BarberProfileMissingGate,
  BarberRoleGate,
  BarberUnreadBell,
} from '@/components/barber/barber-cabinet-gates'
import { BarberProfileForm } from '@/components/barber/barber-profile-form'
import { BarberProfilePhotos } from '@/components/barber/barber-profile-photos'
import { BarberProfileServices } from '@/components/barber/barber-profile-services'
import { BarberProfileSpecializations } from '@/components/barber/barber-profile-specializations'
import { SessionUnavailable } from '@/components/session-unavailable'
import { Button, buttonVariants } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { useOwnBarberProfile } from '@/hooks/use-barber-profile'
import { useSession } from '@/hooks/use-session'
import { useI18n } from '@/i18n'
import { BARBER_PROFILE_PATH } from '@/lib/barber-cabinet-path'
import { isPlainClick, navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * Профиль барбера: карточка, прайс услуг и специализации.
 *
 * Один экран на один агрегат (§3.1): отдельные маршруты для услуг не заводятся.
 * Запрос уходит только после гейтов сессии, роли и привязанного профиля — иначе
 * аноним получил бы `401`, а клиент `403`. Правка идёт через `PUT`, каждая мутация
 * объявляется `role="status"` и перечитывает карточку, не пряча её.
 */
export function BarberProfilePage() {
  const { status, user } = useSession()
  const { t, locale } = useI18n()

  useEffect(() => {
    document.title = t('Профиль — BarberSearch')
  }, [t, locale])

  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-8 sm:py-10">
      {status === 'loading' && (
        <div className="space-y-5">
          <p role="status" className="sr-only">
            {t('Загружаем профиль…')}
          </p>
          <BarberCabinetSkeleton />
        </div>
      )}

      {status === 'unavailable' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <SessionUnavailable />
        </div>
      )}

      {status === 'anonymous' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <SignInGate
            title={t('Войдите, чтобы открыть профиль')}
            description={t(
              'Карточка, услуги и специализации доступны барберу после входа. Вход по email и паролю.'
            )}
            redirectTo={BARBER_PROFILE_PATH}
          />
        </div>
      )}

      {status === 'authenticated' && user && user.role !== 'Barber' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <BarberRoleGate role={user.role} />
        </div>
      )}

      {status === 'authenticated' && user && user.role === 'Barber' && (
        user.barberProfileId === null ? (
          <div className="flex min-h-[55vh] items-center justify-center py-6">
            <BarberProfileMissingGate />
          </div>
        ) : (
          <BarberProfileScreen
            key={user.barberProfileId}
            barberProfileId={user.barberProfileId}
          />
        )
      )}
    </div>
  )
}

function BarberProfileScreen({
  barberProfileId,
}: {
  barberProfileId: string
}) {
  const { t } = useI18n()
  const { status, profile, error, isRefreshing, reload } =
    useOwnBarberProfile(barberProfileId)
  const [announcement, setAnnouncement] = useState<string | null>(null)

  function handleChanged(message: string): void {
    setAnnouncement(message)
    reload()
  }

  return (
    <div className="space-y-5">
      <header className="flex items-start gap-3">
        <div className="flex size-10 shrink-0 items-center justify-center rounded-full bg-muted">
          <UserRound className="size-5 text-muted-foreground" aria-hidden />
        </div>
        <div className="min-w-0 flex-1">
          <h1 className="font-heading text-2xl font-semibold tracking-tight break-words sm:text-3xl">
            {t('Профиль')}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {t(
              'Карточка, которую видят клиенты: услуги, цены и специализации.'
            )}
          </p>
        </div>
        <BarberUnreadBell />
      </header>

      <BarberCabinetNav active="profile" />

      {announcement && (
        <div
          role="status"
          className="flex items-start justify-between gap-3 rounded-lg border bg-muted/50 px-3 py-2 text-sm"
        >
          <p>{announcement}</p>
          <button
            type="button"
            aria-label={t('Скрыть сообщение')}
            className="rounded-md p-0.5 text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
            onClick={() => {
              setAnnouncement(null)
            }}
          >
            <X className="size-3.5" aria-hidden />
          </button>
        </div>
      )}

      {isRefreshing && (
        <p
          role="status"
          className="flex items-center gap-2 text-sm text-muted-foreground"
        >
          <Loader2 className="size-4 animate-spin" aria-hidden />
          {t('Обновляем профиль…')}
        </p>
      )}

      {status === 'loading' && (
        <div className="space-y-5">
          <p role="status" className="sr-only">
            {t('Загружаем профиль…')}
          </p>
          <BarberCabinetSkeleton />
        </div>
      )}

      {status === 'error' && (
        <Card>
          <CardHeader>
            <CardTitle>{t('Не удалось загрузить профиль')}</CardTitle>
            <CardDescription>
              {t(
                'Сервер не ответил. Проверьте подключение и повторите попытку.'
              )}
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <p role="alert" className="text-sm text-destructive">
              {error}
            </p>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              onClick={reload}
            >
              {t('Повторить')}
            </Button>
          </CardContent>
        </Card>
      )}

      {status === 'ready' && error && (
        <p
          role="alert"
          className="flex items-start gap-2 text-sm text-destructive"
        >
          <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
          <span>
            {error}{' '}
            <button
              type="button"
              className="underline underline-offset-2"
              onClick={reload}
            >
              {t('Повторить')}
            </button>
          </span>
        </p>
      )}

      {status === 'ready' && profile && (
        <>
          {!profile.isVerified ? (
            <Card>
              <CardContent className="pt-6">
                <p className="text-sm">
                  {t(
                    'Профиль на проверке. Заполняйте карточку — она появится в поиске после подтверждения администратором.'
                  )}
                </p>
              </CardContent>
            </Card>
          ) : profile.isActive ? (
            <div>
              <a
                href={`/barbers/${profile.id}`}
                className={cn(
                  buttonVariants({ variant: 'outline', size: 'sm' }),
                  'h-11 sm:h-8'
                )}
                onClick={(event) => {
                  if (isPlainClick(event)) {
                    event.preventDefault()
                    navigate(`/barbers/${profile.id}`)
                  }
                }}
              >
                {t('Открыть, как видят клиенты')}
              </a>
            </div>
          ) : null}

          <section aria-labelledby="barber-profile-card-heading">
            <Card>
              <CardHeader>
                <h2
                  id="barber-profile-card-heading"
                  className="font-heading text-base leading-snug font-medium tracking-tight"
                >
                  {t('Карточка')}
                </h2>
              </CardHeader>
              <CardContent>
                <BarberProfileForm
                  mode="edit"
                  initial={profile}
                  onSaved={() => {
                    handleChanged(t('Профиль сохранён.'))
                  }}
                />
              </CardContent>
            </Card>
          </section>

          <BarberProfilePhotos
            barberProfileId={barberProfileId}
            photos={profile.photos}
            onChanged={handleChanged}
          />

          <BarberProfileServices
            barberProfileId={barberProfileId}
            services={profile.services}
            onChanged={handleChanged}
          />

          <BarberProfileSpecializations
            barberProfileId={barberProfileId}
            specializations={profile.specializations}
            onChanged={handleChanged}
          />
        </>
      )}
    </div>
  )
}
