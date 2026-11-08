import { TriangleAlert } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'

import {
  BarberBooking,
  type BookingSlotSnapshot,
} from '@/components/barber/barber-booking'
import { BarberSectionHeading } from '@/components/barber/barber-section'
import { BarberSlotOptions } from '@/components/barber/barber-slot-options'
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
  type BarberSlot,
  type BarberSlots as BarberSlotsData,
} from '@/lib/barber-slots'
import { formatSalonDate } from '@/lib/format'
import { PLACE_KIND_LABELS } from '@/lib/place-kind'
import { tp, t, useI18n } from '@/i18n'

const SELECT_CLASSES =
  'h-11 w-full min-w-0 rounded-lg border border-input bg-transparent px-2.5 text-base transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 sm:h-9 md:text-sm'

const SKELETON_KEYS = Array.from(
  { length: 6 },
  (_, index) => `slot-skeleton-${index}`
)

/** Выбранный слот и снимок ответа `/slots`: после перезапроса слота в списке может не быть. */
type SlotSelection = {
  slot: BarberSlot
  snapshot: BookingSlotSnapshot
}

/** «5 слотов», «1 слот», «2 слота» — объявление для `aria-live`. */
function slotsCountLabel(count: number): string {
  return tp('slots', count)
}

function resultMessage(slots: BarberSlotsData): string {
  if (!slots.isWorkingDay) {
    return t('В этот день барбер не работает.')
  }

  if (slots.slots.length === 0) {
    return t('Свободного времени на этот день нет.')
  }

  return t('Свободно {value}', { value: slotsCountLabel(slots.slots.length) })
}

/**
 * Секция «Свободное время»: дата, услуга, салон, чипы времени и запись.
 *
 * Слоты кликабельны только при выбранной услуге: `POST /api/bookings` требует
 * `barberServiceId`, а `/slots` без `hairServiceId` отдаёт его `null`. После брони
 * панель с подтверждением остаётся, а список слотов перезапрашивается.
 */
export function BarberSlots({ profile }: { profile: BarberProfile }) {
  const { t } = useI18n()
  const salons = profile.salons
  const initialSalon = salons.find((salon) => salon.isPrimary) ?? salons[0]

  // Барбер ещё не указал место приёма: записываться некуда, и `/slots` не
  // запрашивается. Секция остаётся на месте, чтобы клиент понимал причину,
  // а не видел молча пропавший блок.
  if (!initialSalon) {
    return (
      <section aria-labelledby="barber-slots-heading">
        <Card>
          <BarberSectionHeading id="barber-slots-heading">
            {t('Свободное время')}
          </BarberSectionHeading>
          <CardContent>
            <p className="text-sm text-muted-foreground">
              {t(
                'Барбер пока не указал место приёма. Онлайн-запись появится, когда место будет добавлено.'
              )}
            </p>
          </CardContent>
        </Card>
      </section>
    )
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
  const { t } = useI18n()
  const services = profile.services.filter((service) => service.isActive)
  const [salonId, setSalonId] = useState(initialSalonId)
  const [hairServiceId, setHairServiceId] = useState<string | null>(null)
  const salon = salons.find((item) => item.salonId === salonId) ?? salons[0]
  const today = todayInTimeZone(salon.timeZoneId)
  const maxDate = addDays(today, 30)
  const [date, setDate] = useState(today)
  const isDateValid = isSalonDate(date) && date >= today && date <= maxDate
  const [selection, setSelection] = useState<SlotSelection | null>(null)
  const focusSlotsRef = useRef(false)
  const slotsRegionRef = useRef<HTMLDivElement>(null)

  const { slots, status, error, retry, reload } = useBarberSlots({
    barberProfileId: profile.id,
    // Невалидную дату не отправляем: сервер ждёт `DateOnly` и ответит framework-400.
    date: isDateValid ? date : '',
    hairServiceId,
    salonId: salon.salonId,
  })

  const selectedService =
    hairServiceId === null
      ? null
      : (services.find((service) => service.hairServiceId === hairServiceId) ??
        null)

  const isSelectable =
    hairServiceId !== null && slots !== null && slots.barberServiceId !== null

  let liveMessage = ''

  if (!isDateValid) {
    liveMessage = t('Выберите дату.')
  } else if (status === 'loading') {
    liveMessage = t('Загружаем свободное время…')
  } else if (status === 'ready' && slots !== null) {
    liveMessage = resultMessage(slots)
  }
  // У `error` и `unavailable` свои живые регионы: второй не нужен.

  useEffect(() => {
    if (!focusSlotsRef.current || status !== 'ready' || slots === null) {
      return
    }

    focusSlotsRef.current = false
    slotsRegionRef.current
      ?.querySelector<HTMLInputElement>('input[type="radio"]')
      ?.focus()
  }, [status, slots])

  function handleSalonChange(nextSalonId: string) {
    const nextSalon =
      salons.find((item) => item.salonId === nextSalonId) ?? salons[0]
    const nextToday = todayInTimeZone(nextSalon.timeZoneId)

    setSalonId(nextSalonId)
    setSelection(null)
    // В другом поясе выбранная дата могла оказаться в прошлом — берём сегодня.
    setDate((current) =>
      isSalonDate(current) && current >= nextToday ? current : nextToday
    )
  }

  function handleDateChange(nextDate: string) {
    setDate(nextDate)
    setSelection(null)
  }

  function handleServiceChange(nextServiceId: string) {
    setHairServiceId(nextServiceId === '' ? null : nextServiceId)
    setSelection(null)
  }

  function handleSelect(slot: BarberSlot) {
    if (slots === null || slots.barberServiceId === null) {
      return
    }

    setSelection({
      slot,
      snapshot: {
        date: slots.date,
        timeZoneId: slots.timeZoneId,
        salonId: slots.salonId,
        barberServiceId: slots.barberServiceId,
      },
    })
  }

  // «Выбрать другое время» и «Записаться ещё раз»: закрыть панель и обновить список.
  function handleClear() {
    setSelection(null)
    focusSlotsRef.current = true
    reload()
  }

  return (
    <section aria-labelledby="barber-slots-heading">
      <Card>
        <BarberSectionHeading id="barber-slots-heading">
          {t('Свободное время')}
        </BarberSectionHeading>

        <CardContent className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-3">
            <div className="space-y-1.5">
              <Label htmlFor="slots-date">{t('Дата')}</Label>
              <Input
                id="slots-date"
                type="date"
                min={today}
                max={maxDate}
                value={date}
                className="h-11 sm:h-9"
                onChange={(event) => handleDateChange(event.target.value)}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="slots-service">{t('Услуга')}</Label>
              <select
                id="slots-service"
                value={hairServiceId ?? ''}
                className={SELECT_CLASSES}
                onChange={(event) => handleServiceChange(event.target.value)}
              >
                <option value="">{t('Любая услуга')}</option>
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
                <Label htmlFor="slots-salon">{t('Салон')}</Label>
                <select
                  id="slots-salon"
                  value={salon.salonId}
                  className={SELECT_CLASSES}
                  onChange={(event) => handleSalonChange(event.target.value)}
                >
                  {salons.map((item) => (
                    <option key={item.id} value={item.salonId}>
                      {item.name} · {t(PLACE_KIND_LABELS[item.kind])}
                    </option>
                  ))}
                </select>
              </div>
            ) : (
              <div className="space-y-1.5">
                <p className="text-sm leading-snug font-medium">
                  {t('Салон')}
                </p>
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
            <div className="space-y-3">
              <p className="text-sm font-medium">{formatSalonDate(date)}</p>

              <div ref={slotsRegionRef} aria-busy={status === 'loading'}>
                {status === 'loading' && <SlotsSkeleton />}

                {status === 'ready' && slots !== null && (
                  <BarberSlotOptions
                    slots={slots}
                    selectable={isSelectable}
                    selectedStartAt={selection?.slot.startAt ?? null}
                    onSelect={handleSelect}
                  />
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

              {status === 'ready' && slots !== null && !isSelectable && (
                <p className="text-sm text-muted-foreground">
                  {t('Выберите услугу, чтобы записаться.')}
                </p>
              )}

              {selection !== null && (
                <BarberBooking
                  key={selection.slot.startAt}
                  profile={profile}
                  slots={selection.snapshot}
                  slot={selection.slot}
                  serviceName={selectedService?.name ?? t('Услуга')}
                  price={selectedService?.price ?? null}
                  onBooked={reload}
                  onConflict={reload}
                  onClear={handleClear}
                />
              )}
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">
              {t('Выберите дату.')}
            </p>
          )}

          <p className="text-sm text-muted-foreground">
            {t('Время — местное для салона.')}
          </p>
        </CardContent>
      </Card>
    </section>
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
  const { t } = useI18n()

  return (
    <div
      role="alert"
      className="flex flex-col gap-3 rounded-lg border border-destructive/30 p-3 sm:flex-row sm:items-center sm:justify-between"
    >
      <p className="flex items-start gap-2 text-sm text-muted-foreground">
        <TriangleAlert className="mt-0.5 size-4 shrink-0 text-destructive" aria-hidden />
        <span>{message ?? t('Не удалось загрузить свободное время.')}</span>
      </p>
      <Button
        type="button"
        variant="outline"
        className="h-11 w-full shrink-0 sm:h-9 sm:w-auto"
        onClick={onRetry}
      >
        {t('Повторить')}
      </Button>
    </div>
  )
}
