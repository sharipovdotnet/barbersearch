import { useState } from 'react'
import { Loader2, Trash2 } from 'lucide-react'

import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useI18n } from '@/i18n'
import {
  addDays,
  todayInTimeZone,
} from '@/lib/barber-slots'
import {
  describeScheduleError,
  formatScheduleDate,
  fromTimeInputValue,
  removeScheduleException,
  setScheduleException,
  toTimeInputValue,
  type BarberScheduleSalon,
} from '@/lib/barber-schedule'

type Props = {
  barberProfileId: string
  salon: BarberScheduleSalon
  onChanged: (message: string) => void
}

/**
 * Исключения на конкретные даты: выходной или свои часы.
 *
 * Исключение перекрывает недельный шаблон только в этом салоне. Дату выбирают
 * нативным `date`, поэтому клавиатура и мобильные календари работают без своей сетки.
 */
export function BarberScheduleExceptions({
  barberProfileId,
  salon,
  onChanged,
}: Props) {
  const { t } = useI18n()
  const today = todayInTimeZone(salon.timeZoneId)
  const [date, setDate] = useState('')
  const [isDayOff, setIsDayOff] = useState(true)
  const [startTime, setStartTime] = useState('10:00')
  const [endTime, setEndTime] = useState('14:00')
  const [reason, setReason] = useState('')
  const [isSaving, setIsSaving] = useState(false)
  const [deleting, setDeleting] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const hasInvalidHours = !isDayOff && endTime <= startTime

  async function handleAdd() {
    if (isSaving || date === '' || hasInvalidHours) {
      return
    }

    setIsSaving(true)
    setError(null)

    try {
      await setScheduleException(barberProfileId, salon.salonId, date, {
        isDayOff,
        startTime: isDayOff ? '00:00:00' : fromTimeInputValue(startTime),
        endTime: isDayOff ? '00:00:00' : fromTimeInputValue(endTime),
        reason: reason.trim() === '' ? null : reason.trim(),
      })

      setDate('')
      setReason('')
      onChanged(t('Исключение добавлено.'))
    } catch (cause) {
      setError(
        describeScheduleError(cause, t('Не удалось добавить исключение.'))
      )
    } finally {
      setIsSaving(false)
    }
  }

  async function handleDelete(exceptionDate: string) {
    if (deleting !== null) {
      return
    }

    setDeleting(exceptionDate)
    setError(null)

    try {
      await removeScheduleException(
        barberProfileId,
        salon.salonId,
        exceptionDate
      )
      onChanged(t('Исключение удалено.'))
    } catch (cause) {
      setError(
        describeScheduleError(cause, t('Не удалось удалить исключение.'))
      )
    } finally {
      setDeleting(null)
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('Исключения на даты')}</CardTitle>
        <CardDescription>
          {t(
            'Выходной или особые часы в конкретный день. Действует только в салоне «{name}»; недельное расписание не меняется.',
            { name: salon.name }
          )}
        </CardDescription>
      </CardHeader>

      <CardContent className="space-y-5">
        <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
          <div className="space-y-1.5">
            <Label htmlFor={`exception-date-${salon.salonId}`}>
              {t('Дата')}
            </Label>
            <Input
              id={`exception-date-${salon.salonId}`}
              type="date"
              value={date}
              min={today}
              max={addDays(today, 90)}
              className="h-11 sm:h-8"
              onChange={(event) => {
                setError(null)
                setDate(event.target.value)
              }}
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor={`exception-kind-${salon.salonId}`}>
              {t('Что в этот день')}
            </Label>
            <select
              id={`exception-kind-${salon.salonId}`}
              value={isDayOff ? 'day-off' : 'custom'}
              className="h-11 w-full rounded-md border border-input bg-transparent px-3 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-8"
              onChange={(event) => {
                setError(null)
                setIsDayOff(event.target.value === 'day-off')
              }}
            >
              <option value="day-off">{t('Выходной')}</option>
              <option value="custom">{t('Свои часы')}</option>
            </select>
          </div>
        </div>

        {!isDayOff && (
          <div className="flex flex-wrap items-center gap-2">
            <Input
              type="time"
              step={1800}
              value={startTime}
              aria-label={t('Начало работы в этот день')}
              aria-invalid={hasInvalidHours || undefined}
              className="h-11 w-32 sm:h-8"
              onChange={(event) => {
                setError(null)
                setStartTime(event.target.value)
              }}
            />
            <span className="text-sm text-muted-foreground">—</span>
            <Input
              type="time"
              step={1800}
              value={endTime}
              aria-label={t('Окончание работы в этот день')}
              aria-invalid={hasInvalidHours || undefined}
              className="h-11 w-32 sm:h-8"
              onChange={(event) => {
                setError(null)
                setEndTime(event.target.value)
              }}
            />
            {hasInvalidHours && (
              <p className="w-full text-xs text-destructive">
                {t('Конец должен быть позже начала.')}
              </p>
            )}
          </div>
        )}

        <div className="space-y-1.5">
          <Label htmlFor={`exception-reason-${salon.salonId}`}>
            {t('Пометка')}{' '}
            <span className="text-muted-foreground">{t('(необязательно)')}</span>
          </Label>
          <Input
            id={`exception-reason-${salon.salonId}`}
            value={reason}
            maxLength={500}
            placeholder={t('Например, личные дела')}
            className="h-11 sm:h-8"
            onChange={(event) => {
              setError(null)
              setReason(event.target.value)
            }}
          />
        </div>

        {error && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}

        <Button
          type="button"
          className="h-11 sm:h-8"
          disabled={isSaving || date === '' || hasInvalidHours}
          aria-busy={isSaving}
          onClick={() => {
            void handleAdd()
          }}
        >
          {isSaving && <Loader2 className="size-4 animate-spin" aria-hidden />}
          {t('Добавить исключение')}
        </Button>

        {salon.exceptions.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            {t('Исключений пока нет.')}
          </p>
        ) : (
          <ul className="divide-y rounded-lg border">
            {salon.exceptions.map((exception) => (
              <li
                key={exception.date}
                className="flex flex-wrap items-center justify-between gap-3 p-3"
              >
                <div className="min-w-0 space-y-0.5">
                  <p className="text-sm font-medium">
                    {formatScheduleDate(exception.date)}
                  </p>
                  <p className="text-sm text-muted-foreground">
                    {exception.isDayOff
                      ? t('Выходной')
                      : `${toTimeInputValue(exception.startTime)}–${toTimeInputValue(exception.endTime)}`}
                    {exception.reason ? ` · ${exception.reason}` : ''}
                  </p>
                </div>

                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="h-11 text-destructive hover:text-destructive sm:h-8"
                  disabled={deleting !== null}
                  aria-label={t('Удалить исключение {date}', {
                    date: formatScheduleDate(exception.date),
                  })}
                  onClick={() => {
                    void handleDelete(exception.date)
                  }}
                >
                  {deleting === exception.date ? (
                    <Loader2 className="size-4 animate-spin" aria-hidden />
                  ) : (
                    <Trash2 className="size-4" aria-hidden />
                  )}
                  {t('Удалить')}
                </Button>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}
