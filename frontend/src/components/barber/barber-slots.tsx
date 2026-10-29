import { TriangleAlert } from 'lucide-react'
import { useState } from 'react'

import { BarberSectionHeading } from '@/components/barber/barber-section'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { useBarberSlots } from '@/hooks/use-barber-slots'
import type {
  BarberProfile,
  BarberProfileSalon,
} from '@/lib/catalog'
import {
  addDays,
  isSalonDate,
  todayInTimeZone,
  type BarberSlots as BarberSlotsData,
} from '@/lib/barber-slots'
import { formatSalonDate, formatTime } from '@/lib/format'

const SELECT_CLASSES =
  'h-11 w-full min-w-0 rounded-lg border border-input bg-transparent px-2.5 text-base transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 sm:h-9 md:text-sm'

const SKELETON_KEYS = Array.from(
  { length: 6 },
  (_, index) => `slot-skeleton-${index}`
)

/** «5 слотов», «1 слот», «2 слота» — объявление для `aria-live`. */
function slotsCountLabel(count: number): string {
  const mod10 = count % 10
  const mod100 = count % 100

  if (mod10 === 1 && mod100 !== 11) {
    return `${count} слот`
  }

  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) {
    return `${count} слота`
  }

  return `${count} слотов`
}

function resultMessage(slots: BarberSlotsData): string {
  if (!slots.isWorkingDay) {
    return 'В этот день барбер не работает.'
  }

  if (slots.slots.length === 0) {
    return 'Свободного времени на этот день нет.'
  }

  return `Свободно ${slotsCountLabel(slots.slots.length)}`
}

/**
 * Секция «Свободное время»: дата, услуга, салон и чипы времени.
 *
 * Только чтение: слоты не кликабельны и брони здесь нет — кнопка-пустышка хуже,
 * чем её отсутствие. Время показывается в поясе салона из ответа, без пересчёта
 * в пояс браузера. Запроса нет вовсе, если у барбера не указан ни один салон.
 */
export function BarberSlots({ profile }: { profile: BarberProfile }) {
  const salons = profile.salons
  const initialSalon = salons.find((salon) => salon.isPrimary) ?? salons[0]

  if (!initialSalon) {
    return null
  }

  return (
    <BarberSlotsSection
      profile={profile}
      salons={salons}
      initialSalonId={initialSalon.salonId}
    />
  )
}

function BarberSlotsSection({
  profile,
  salons,
  initialSalonId,
}: {
  profile: BarberProfile
  salons: BarberProfileSalon[]
  initialSalonId: string
}) {
  const services = profile.services.filter((service) => service.isActive)
  const [salonId, setSalonId] = useState(initialSalonId)
  const [hairServiceId, setHairServiceId] = useState<string | null>(null)
  const salon = salons.find((item) => item.salonId === salonId) ?? salons[0]
  const today = todayInTimeZone(salon.timeZoneId)
  const maxDate = addDays(today, 30)
  const [date, setDate] = useState(today)
  const isDateValid = isSalonDate(date) && date >= today && date <= maxDate

  const { slots, status, error, retry } = useBarberSlots({
    barberProfileId: profile.id,
    // Невалидную дату не отправляем: сервер ждёт `DateOnly` и ответит framework-400.
    date: isDateValid ? date : '',
    hairServiceId,
    salonId: salon.salonId,
  })

  let liveMessage = ''

  if (!isDateValid) {
    liveMessage = 'Выберите дату.'
  } else if (status === 'loading') {
    liveMessage = 'Загружаем свободное время…'
  } else if (status === 'ready' && slots !== null) {
    liveMessage = resultMessage(slots)
  }
  // У `error` и `unavailable` свои живые регионы: второй не нужен.

  function handleSalonChange(nextSalonId: string) {
    const nextSalon =
      salons.find((item) => item.salonId === nextSalonId) ?? salons[0]
    const nextToday = todayInTimeZone(nextSalon.timeZoneId)

    setSalonId(nextSalonId)
    // В другом поясе выбранная дата могла оказаться в прошлом — берём сегодня.
    setDate((current) =>
      isSalonDate(current) && current >= nextToday ? current : nextToday
    )
  }

  return (
    <section aria-labelledby="barber-slots-heading">
      <Card>
        <BarberSectionHeading id="barber-slots-heading">
          Свободное время
        </BarberSectionHeading>

        <CardContent className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-3">
            <div className="space-y-1.5">
              <Label htmlFor="slots-date">Дата</Label>
              <Input
                id="slots-date"
                type="date"
                min={today}
                max={maxDate}
                value={date}
                className="h-11 sm:h-9"
                onChange={(event) => setDate(event.target.value)}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="slots-service">Услуга</Label>
              <select
                id="slots-service"
                value={hairServiceId ?? ''}
                className={SELECT_CLASSES}
                onChange={(event) =>
                  setHairServiceId(
                    event.target.value === '' ? null : event.target.value
                  )
                }
              >
                <option value="">Любая услуга</option>
                {services.map((service) => (
                  <option
                    key={service.barberServiceId}
                    value={service.hairServiceId}
                  >
                    {service.name}
                  </option>
                ))}
              </select>
            </div>

            {salons.length > 1 ? (
              <div className="space-y-1.5">
                <Label htmlFor="slots-salon">Салон</Label>
                <select
                  id="slots-salon"
                  value={salon.salonId}
                  className={SELECT_CLASSES}
                  onChange={(event) => handleSalonChange(event.target.value)}
                >
                  {salons.map((item) => (
                    <option key={item.id} value={item.salonId}>
                      {item.name}
                    </option>
                  ))}
                </select>
              </div>
            ) : (
              <div className="space-y-1.5">
                <p className="text-sm leading-snug font-medium">Салон</p>
                <p className="flex h-11 items-center text-sm text-muted-foreground sm:h-9">
                  {salon.name}
                </p>
              </div>
            )}
          </div>

          <p role="status" className="sr-only">
            {liveMessage}
          </p>

          {isDateValid ? (
            <div className="space-y-3" aria-busy={status === 'loading'}>
              <p className="text-sm font-medium">{formatSalonDate(date)}</p>

              {status === 'loading' && <SlotsSkeleton />}

              {status === 'ready' && slots !== null && (
                <SlotsList slots={slots} />
              )}

              {status === 'error' && (
                <SlotsError message={error} onRetry={retry} />
              )}

              {status === 'unavailable' && (
                <p role="status" className="text-sm text-muted-foreground">
                  {error}
                </p>
              )}
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">Выберите дату.</p>
          )}

          <p className="text-sm text-muted-foreground">
            Время — местное для салона.
          </p>
        </CardContent>
      </Card>
    </section>
  )
}

function SlotsList({ slots }: { slots: BarberSlotsData }) {
  if (!slots.isWorkingDay) {
    return (
      <p className="text-sm text-muted-foreground">
        В этот день барбер не работает.
      </p>
    )
  }

  if (slots.slots.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        Свободного времени на этот день нет.
      </p>
    )
  }

  return (
    <ul className="flex flex-wrap gap-2">
      {slots.slots.map((slot) => (
        <li key={slot.startAt}>
          <span className="inline-flex h-9 items-center rounded-lg bg-muted px-3 text-sm text-foreground tabular-nums">
            <time dateTime={slot.startAt}>
              {formatTime(slot.startAt, slots.timeZoneId)}
            </time>
          </span>
        </li>
      ))}
    </ul>
  )
}

function SlotsSkeleton() {
  return (
    <ul aria-hidden="true" className="flex flex-wrap gap-2">
      {SKELETON_KEYS.map((key) => (
        <li key={key}>
          <Skeleton className="h-9 w-16 rounded-lg" />
        </li>
      ))}
    </ul>
  )
}

function SlotsError({
  message,
  onRetry,
}: {
  message: string | null
  onRetry: () => void
}) {
  return (
    <div
      role="alert"
      className="flex flex-col gap-3 rounded-lg border border-destructive/30 p-3 sm:flex-row sm:items-center sm:justify-between"
    >
      <p className="flex items-start gap-2 text-sm text-muted-foreground">
        <TriangleAlert className="mt-0.5 size-4 shrink-0 text-destructive" aria-hidden />
        <span>{message ?? 'Не удалось загрузить свободное время.'}</span>
      </p>
      <Button
        type="button"
        variant="outline"
        className="h-11 w-full shrink-0 sm:h-9 sm:w-auto"
        onClick={onRetry}
      >
        Повторить
      </Button>
    </div>
  )
}
