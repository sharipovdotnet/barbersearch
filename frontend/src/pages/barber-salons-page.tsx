import { useEffect, useState } from 'react'
import { Loader2, MapPin, TriangleAlert, X } from 'lucide-react'

import { SignInGate } from '@/components/account/account-gates'
import { BarberCabinetNav } from '@/components/barber/barber-cabinet-nav'
import {
  BarberCabinetSkeleton,
  BarberProfileMissingGate,
  BarberRoleGate,
} from '@/components/barber/barber-cabinet-gates'
import { SalonPickerDialog } from '@/components/barber/barber-salon-picker-dialog'
import { SessionUnavailable } from '@/components/session-unavailable'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useBarberSalons } from '@/hooks/use-barber-salons'
import { useSession } from '@/hooks/use-session'
import { BARBER_SALONS_PATH, BARBER_SCHEDULE_PATH } from '@/lib/barber-cabinet-path'
import { navigate } from '@/lib/router'

/**
 * «Мои салоны»: привязанные салоны и выбор нового из публичного каталога.
 *
 * Гейты те же, что у расписания и записей: пока проверки не пройдены, запрос списка
 * не уходит. После привязки роутер не меняется — диалог закрывается, список
 * перечитывается, успех объявляется через `role="status"`.
 */
export function BarberSalonsPage() {
  const { status, user } = useSession()

  useEffect(() => {
    document.title = 'Мои салоны — BarberSearch'
  }, [])

  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-8 sm:py-10">
      {status === 'loading' && (
        <div className="space-y-5">
          <p role="status" className="sr-only">
            Загружаем салоны…
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
            title="Войдите, чтобы выбрать салон"
            description="Салоны, в которых вы работаете, видны только владельцу профиля. Вход по email и паролю."
            redirectTo={BARBER_SALONS_PATH}
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
          <BarberSalonsScreen
            key={user.barberProfileId}
            barberProfileId={user.barberProfileId}
          />
        )
      )}
    </div>
  )
}

function BarberSalonsScreen({ barberProfileId }: { barberProfileId: string }) {
  const { status, salons, error, isRefreshing, reload } =
    useBarberSalons(barberProfileId)
  const [announcement, setAnnouncement] = useState<string | null>(null)

  function handleAttached() {
    setAnnouncement('Салон добавлен.')
    reload()
  }

  return (
    <div className="space-y-5">
      <header className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-3">
          <div className="flex size-10 items-center justify-center rounded-full bg-muted">
            <MapPin className="size-5 text-muted-foreground" aria-hidden />
          </div>
          <div>
            <h1 className="font-heading text-2xl font-semibold tracking-tight sm:text-3xl">
              Мои салоны
            </h1>
            <p className="mt-1 text-sm text-muted-foreground">
              Салоны, в которых вы работаете. Расписание и запись ведутся по паре
              «барбер — салон».
            </p>
          </div>
        </div>

        {status === 'ready' && salons.length > 0 && (
          <SalonPickerDialog
            barberProfileId={barberProfileId}
            hasSalons={salons.length > 0}
            onAttached={handleAttached}
            onStale={reload}
          />
        )}
      </header>

      <BarberCabinetNav active="salons" />

      {announcement && (
        <div
          role="status"
          className="flex items-start justify-between gap-3 rounded-lg border bg-muted/50 px-3 py-2 text-sm"
        >
          <p>{announcement}</p>
          <button
            type="button"
            aria-label="Скрыть сообщение"
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
        <p role="status" className="flex items-center gap-2 text-sm text-muted-foreground">
          <Loader2 className="size-4 animate-spin" aria-hidden />
          Обновляем салоны…
        </p>
      )}

      {status === 'loading' && (
        <div aria-busy="true" className="space-y-3">
          <span className="sr-only">Загружаем салоны…</span>
          {['salons-skeleton-1', 'salons-skeleton-2', 'salons-skeleton-3'].map(
            (key) => (
              <div key={key} aria-hidden className="rounded-xl border p-4">
                <Skeleton className="h-4 w-48" />
                <Skeleton className="mt-2 h-4 w-32" />
                <Skeleton className="mt-2 h-4 w-56" />
              </div>
            )
          )}
        </div>
      )}

      {status === 'error' && (
        <Card>
          <CardHeader>
            <CardTitle>Не удалось загрузить салоны</CardTitle>
            <CardDescription>
              Сервер не ответил. Проверьте подключение и повторите попытку.
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
              Повторить
            </Button>
          </CardContent>
        </Card>
      )}

      {status === 'ready' && error && (
        <p role="alert" className="flex items-start gap-2 text-sm text-destructive">
          <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
          <span>
            {error}{' '}
            <button
              type="button"
              className="underline underline-offset-2"
              onClick={reload}
            >
              Повторить
            </button>
          </span>
        </p>
      )}

      {status === 'ready' && salons.length === 0 && (
        <Card>
          <CardHeader>
            <CardTitle>Салон не добавлен</CardTitle>
            <CardDescription>
              Без салона расписание и запись не работают.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <SalonPickerDialog
              barberProfileId={barberProfileId}
              hasSalons={false}
              onAttached={handleAttached}
              onStale={reload}
            />
          </CardContent>
        </Card>
      )}

      {status === 'ready' && salons.length > 0 && (
        <>
          <ul className="space-y-3">
            {salons.map((salon) => (
              <li key={salon.salonId}>
                <Card>
                  <CardContent className="space-y-2 pt-6">
                    <div className="flex flex-wrap items-center gap-2">
                      <h2 className="font-medium break-words">{salon.name}</h2>
                      {salon.isPrimary && (
                        <Badge variant="secondary">Основной</Badge>
                      )}
                    </div>

                    <p className="text-sm break-words text-muted-foreground">
                      {salon.city}
                      {salon.address ? `, ${salon.address}` : ''}
                    </p>

                    <p className="text-sm text-muted-foreground">
                      Часовой пояс:{' '}
                      <span className="text-foreground">{salon.timeZoneId}</span>
                    </p>
                  </CardContent>
                </Card>
              </li>
            ))}
          </ul>

          <Button
            type="button"
            variant="outline"
            className="h-11 sm:h-8"
            onClick={() => {
              navigate(BARBER_SCHEDULE_PATH)
            }}
          >
            Настроить расписание
          </Button>
        </>
      )}
    </div>
  )
}
