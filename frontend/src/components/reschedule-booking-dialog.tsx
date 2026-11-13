import { useState } from 'react'
import { Loader2, TriangleAlert } from 'lucide-react'

import { BarberSlotOptions } from '@/components/barber/barber-slot-options'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { useBarberSlots } from '@/hooks/use-barber-slots'
import { tp, useI18n } from '@/i18n'
import {
  addDays,
  RESCHEDULE_HORIZON_DAYS,
  salonDateOf,
  todayInTimeZone,
  type BarberSlot,
} from '@/lib/barber-slots'
import { describeRescheduleFailure } from '@/lib/errors'
import { formatDateTime } from '@/lib/format'

/**
 * Бронь, которую двигают: минимум полей, общий для обоих кабинетов.
 *
 * `timeZoneId` у клиента приходит из карточки барбера и может быть `undefined`,
 * пока она не загрузилась; тогда дата и время считаются в поясе браузера.
 */
export type RescheduleTarget = {
  bookingId: string
  startAt: string
  endAt: string
  /** Пояс салона: барбер берёт его из `salon.timeZoneId`; у клиента он `undefined`, пока карточка не загрузилась. */
  timeZoneId: string | undefined
  serviceName: string
}

/**
 * Диалог переноса визита — общий для кабинета клиента и барбера.
 *
 * Меняется только время: услуга, салон, барбер, цена, статус и длительность
 * остаются прежними. Внутри тот же `BarberSlotOptions`, что и в публичной записи,
 * поэтому чипы времени не расходятся с профилем. Запрос выполняет родитель (у него
 * уже есть локальная правка строки и объявление); диалог показывает ожидание и
 * раскладывает ошибку по кодам через `describeRescheduleFailure`.
 *
 * `role="dialog"`, а не `alertdialog`: перенос обратим, ничего не удаляется.
 */
export function RescheduleBookingDialog({
  target,
  variant,
  onClose,
  onRescheduled,
}: {
  target: RescheduleTarget | null
  variant: 'customer' | 'barber'
  onClose: () => void
  onRescheduled: (slot: BarberSlot) => Promise<void> | void
}) {
  return (
    <Dialog
      open={target !== null}
      onOpenChange={(next) => {
        if (!next) {
          onClose()
        }
      }}
    >
      {/* Содержимое монтируется на каждое открытие: дата и выбор слота не
          переносятся с прошлой строки. */}
      {target !== null && (
        <DialogContent className="max-h-[calc(100svh-2rem)] overflow-y-auto sm:max-w-lg">
          <RescheduleForm
            target={target}
            variant={variant}
            onClose={onClose}
            onRescheduled={onRescheduled}
          />
        </DialogContent>
      )}
    </Dialog>
  )
}

function RescheduleForm({
  target,
  variant,
  onClose,
  onRescheduled,
}: {
  target: RescheduleTarget
  variant: 'customer' | 'barber'
  onClose: () => void
  onRescheduled: (slot: BarberSlot) => Promise<void> | void
}) {
  const { t } = useI18n()
  const [date, setDate] = useState(() =>
    salonDateOf(target.startAt, target.timeZoneId)
  )
  const [selected, setSelected] = useState<BarberSlot | null>(null)
  const [isPending, setIsPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const slots = useBarberSlots({ bookingId: target.bookingId, date })
  const timeZoneId = target.timeZoneId ?? slots.slots?.timeZoneId
  const today = todayInTimeZone(timeZoneId)
  const maxDate = today === '' ? '' : addDays(today, RESCHEDULE_HORIZON_DAYS)
  const canSubmit = selected !== null && !isPending

  let liveMessage = ''

  if (slots.status === 'loading') {
    liveMessage = t('Загружаем свободное время…')
  } else if (slots.status === 'ready' && slots.slots !== null) {
    liveMessage = !slots.slots.isWorkingDay
      ? t('В этот день барбер не работает.')
      : slots.slots.slots.length === 0
        ? t('Свободного времени на этот день нет.')
        : t('Свободно {value}', {
            value: tp('slots', slots.slots.slots.length),
          })
  }

  function handleDateChange(value: string) {
    setDate(value)
    setSelected(null)
    setError(null)
  }

  function handleSelect(slot: BarberSlot) {
    setSelected(slot)
    setError(null)
  }

  async function handleSubmit() {
    if (!canSubmit || selected === null) {
      return
    }

    setIsPending(true)
    setError(null)

    try {
      await onRescheduled(selected)
      onClose()
    } catch (cause) {
      const failure = describeRescheduleFailure(
        cause,
        t('Не удалось перенести запись.')
      )

      setError(failure.message)

      if (failure.reloadSlots) {
        setSelected(null)
        slots.reload()
      }
    } finally {
      setIsPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>{t('Перенести запись')}</DialogTitle>
        <DialogDescription>
          {formatDateTime(target.startAt, timeZoneId)} · {target.serviceName}
        </DialogDescription>
      </DialogHeader>

      <p className="text-sm text-muted-foreground">
        {t('Длительность и цена не изменятся.')}
      </p>

      {variant === 'barber' && (
        <p className="text-sm text-muted-foreground">
          {t(
            'Клиент увидит новое время в кабинете. Письмо не отправляется.'
          )}
        </p>
      )}

      <div className="space-y-1.5">
        <Label htmlFor="reschedule-date">{t('Новая дата')}</Label>
        <Input
          id="reschedule-date"
          type="date"
          min={today}
          max={maxDate}
          value={date}
          disabled={isPending}
          className="h-11 w-full sm:h-9"
          onChange={(event) => handleDateChange(event.target.value)}
        />
      </div>

      <p role="status" className="sr-only">
        {liveMessage}
      </p>

      <div aria-busy={slots.status === 'loading'}>
        {slots.status === 'loading' && <SlotsSkeleton />}

        {slots.status === 'ready' && slots.slots !== null && (
          <BarberSlotOptions
            slots={slots.slots}
            selectable
            selectedStartAt={selected?.startAt ?? null}
            onSelect={handleSelect}
            name="reschedule-slot"
            legend={t('Новое время')}
          />
        )}

        {slots.status === 'error' && (
          <div
            role="alert"
            className="flex flex-col gap-3 rounded-lg border border-destructive/30 p-3 sm:flex-row sm:items-center sm:justify-between"
          >
            <p className="flex items-start gap-2 text-sm text-muted-foreground">
              <TriangleAlert
                className="mt-0.5 size-4 shrink-0 text-destructive"
                aria-hidden
              />
              <span>
                {slots.error ?? t('Не удалось загрузить свободное время.')}
              </span>
            </p>
            <Button
              type="button"
              variant="outline"
              className="h-11 w-full shrink-0 sm:h-9 sm:w-auto"
              onClick={slots.retry}
            >
              {t('Повторить')}
            </Button>
          </div>
        )}

        {slots.status === 'unavailable' && (
          <p role="status" className="text-sm text-muted-foreground">
            {slots.error}
          </p>
        )}
      </div>

      {error !== null && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}

      <DialogFooter className="flex-col sm:flex-row">
        <Button
          type="button"
          variant="outline"
          className="h-11 sm:h-8"
          disabled={isPending}
          onClick={onClose}
        >
          {t('Отмена')}
        </Button>
        <Button
          type="button"
          className="h-11 sm:h-8"
          disabled={!canSubmit}
          aria-busy={isPending}
          onClick={() => void handleSubmit()}
        >
          {isPending && (
            <Loader2
              data-icon="inline-start"
              aria-hidden
              className="animate-spin"
            />
          )}
          {isPending ? t('Переносим…') : t('Перенести визит')}
        </Button>
      </DialogFooter>
    </>
  )
}

function SlotsSkeleton() {
  return (
    <ul aria-hidden="true" className="flex flex-wrap gap-2">
      {['slot-skeleton-1', 'slot-skeleton-2', 'slot-skeleton-3'].map((key) => (
        <li key={key}>
          <Skeleton className="h-11 w-16 rounded-lg sm:h-9" />
        </li>
      ))}
    </ul>
  )
}
