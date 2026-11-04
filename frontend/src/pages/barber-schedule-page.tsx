import { useEffect, useState } from 'react'
import { CalendarClock, Loader2, TriangleAlert, X } from 'lucide-react'

import { SignInGate } from '@/components/account/account-gates'
import { BarberCabinetNav } from '@/components/barber/barber-cabinet-nav'
import {
  BarberCabinetSkeleton,
  BarberProfileMissingGate,
  BarberRoleGate,
} from '@/components/barber/barber-cabinet-gates'
import { BarberScheduleExceptions } from '@/components/barber/barber-schedule-exceptions'
import { BarberTimeOffs } from '@/components/barber/barber-time-offs'
import { BarberWeekSchedule } from '@/components/barber/barber-week-schedule'
import { SessionUnavailable } from '@/components/session-unavailable'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { useBarberSchedule } from '@/hooks/use-barber-schedule'
import { useSession } from '@/hooks/use-session'
import { BARBER_SCHEDULE_PATH } from '@/lib/barber-cabinet-path'

/**
 * Расписание барбера: недельный шаблон, исключения на даты и отпуска.
 *
 * Экран отдельный от записей, но живёт в том же кабинете: те же гейты сессии,
 * роли и привязанного профиля. Пока проверки не пройдены, запрос расписания
 * не уходит — иначе аноним получил бы `401`, а клиент — `403`.
 */
export function BarberSchedulePage() {
  const { status, user } = useSession()

  useEffect(() => {
    document.title = 'Расписание — BarberSearch'
  }, [])

  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-8 sm:py-10">
      {status === 'loading' && (
        <div className="space-y-5">
          <p role="status" className="sr-only">
            Загружаем расписание…
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
            title="Войдите, чтобы настроить расписание"
            description="Рабочие дни, исключения и отпуска доступны барберу после входа. Вход по email и паролю."
            redirectTo={BARBER_SCHEDULE_PATH}
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
          <BarberScheduleScreen
            key={user.barberProfileId}
            barberProfileId={user.barberProfileId}
          />
        )
      )}
    </div>
  )
}

function BarberScheduleScreen({
  barberProfileId,
}: {
  barberProfileId: string
}) {
  const { status, schedule, error, isRefreshing, reload } =
    useBarberSchedule(barberProfileId)
  const [selectedSalonId, setSelectedSalonId] = useState<string | null>(null)
  const [announcement, setAnnouncement] = useState<string | null>(null)

  const selectedSalon =
    schedule?.salons.find((salon) => salon.salonId === selectedSalonId)
    ?? schedule?.salons[0]
  const primarySalon =
    schedule?.salons.find((salon) => salon.isPrimary) ?? schedule?.salons[0]

  function handleChanged(message: string) {
    setAnnouncement(message)
    reload()
  }

  return (
    <div className="space-y-5">
      <header className="flex items-center gap-3">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <CalendarClock className="size-5 text-muted-foreground" aria-hidden />
        </div>
        <div>
          <h1 className="font-heading text-2xl font-semibold tracking-tight sm:text-3xl">
            Расписание
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Рабочие дни, исключения на даты и отпуска. Клиенты видят только
            свободное время.
          </p>
        </div>
      </header>

      <BarberCabinetNav active="schedule" />

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
          Обновляем расписание…
        </p>
      )}

      {status === 'error' && (
        <Card>
          <CardHeader>
            <CardTitle>Не удалось загрузить расписание</CardTitle>
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

      {status === 'ready' && schedule && schedule.salons.length === 0 && (
        <Card>
          <CardHeader>
            <CardTitle>Нет привязанных салонов</CardTitle>
            <CardDescription>
              Расписание задаётся для пары «барбер — салон». Попросите
              администратора добавить салон в профиль.
            </CardDescription>
          </CardHeader>
        </Card>
      )}

      {status === 'ready' && schedule && selectedSalon && primarySalon && (
        <>
          {schedule.salons.length > 1 ? (
            <div className="max-w-sm space-y-1.5">
              <Label htmlFor="schedule-salon">Салон</Label>
              <select
                id="schedule-salon"
                value={selectedSalon.salonId}
                className="h-11 w-full rounded-md border border-input bg-transparent px-3 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-8"
                onChange={(event) => {
                  setSelectedSalonId(event.target.value)
                  setAnnouncement(null)
                }}
              >
                {schedule.salons.map((salon) => (
                  <option key={salon.salonId} value={salon.salonId}>
                    {salon.name}
                    {salon.isPrimary ? ' — основной' : ''}
                  </option>
                ))}
              </select>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">
              Салон: <span className="text-foreground">{selectedSalon.name}</span>
              {selectedSalon.city ? `, ${selectedSalon.city}` : ''}
            </p>
          )}

          <BarberWeekSchedule
            key={`week-${selectedSalon.salonId}`}
            barberProfileId={barberProfileId}
            salon={selectedSalon}
            onSaved={handleChanged}
          />

          <BarberScheduleExceptions
            key={`exceptions-${selectedSalon.salonId}`}
            barberProfileId={barberProfileId}
            salon={selectedSalon}
            onChanged={handleChanged}
          />

          <BarberTimeOffs
            key={`time-offs-${barberProfileId}`}
            barberProfileId={barberProfileId}
            timeOffs={schedule.timeOffs}
            timeZoneId={primarySalon.timeZoneId}
            onChanged={handleChanged}
          />
        </>
      )}
    </div>
  )
}
