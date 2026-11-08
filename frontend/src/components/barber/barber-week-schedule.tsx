import { useState } from 'react'
import { Loader2, TriangleAlert } from 'lucide-react'

import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useI18n } from '@/i18n'
import {
  DAY_LABELS,
  describeScheduleError,
  fromTimeInputValue,
  isSameWeek,
  replaceWorkingSchedules,
  toTimeInputValue,
  weekdayTemplate,
  weeklyDaysToInput,
  type BarberScheduleSalon,
  type WorkingDayInput,
} from '@/lib/barber-schedule'
import { cn } from '@/lib/utils'

type Props = {
  barberProfileId: string
  salon: BarberScheduleSalon
  /** Вызывается после `204`; родитель перечитывает расписание. */
  onSaved: (message: string) => void
}

/**
 * Редактор недельного шаблона одного салона.
 *
 * Семь строк, у каждой — «рабочий/выходной» и два времени. Сохранение отправляет
 * всю неделю одним `PUT`: сервер проверяет семь дней, дубликаты и границы, поэтому
 * частично сохранённого графика не бывает. Уже созданные записи не отменяются —
 * об этом честно сказано в карточке.
 */
export function BarberWeekSchedule({ barberProfileId, salon, onSaved }: Props) {
  const { t } = useI18n()
  const [days, setDays] = useState<WorkingDayInput[]>(() =>
    weeklyDaysToInput(salon.weekly)
  )
  const [isSaving, setIsSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const original = weeklyDaysToInput(salon.weekly)
  const isDirty = !isSameWeek(days, original)
  const hasInvalidHours = days.some(
    (day) => !day.isDayOff && day.endTime <= day.startTime
  )
  const isAllDaysOff = days.every((day) => day.isDayOff)

  function updateDay(dayOfWeek: WorkingDayInput['dayOfWeek'], patch: Partial<WorkingDayInput>) {
    setError(null)
    setDays((current) =>
      current.map((day) =>
        day.dayOfWeek === dayOfWeek ? { ...day, ...patch } : day
      )
    )
  }

  async function handleSave() {
    if (isSaving || !isDirty || hasInvalidHours) {
      return
    }

    setIsSaving(true)
    setError(null)

    try {
      await replaceWorkingSchedules(barberProfileId, salon.salonId, days)
      onSaved(t('Расписание сохранено.'))
    } catch (cause) {
      setError(
        describeScheduleError(cause, t('Не удалось сохранить расписание.'))
      )
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('Недельное расписание')}</CardTitle>
        <CardDescription>
          {t(
            'Обычные рабочие часы салона «{name}». Время — в поясе салона',
            { name: salon.name }
          )}{' '}
          <span className="whitespace-nowrap">{salon.timeZoneId}</span>.
        </CardDescription>
      </CardHeader>

      <CardContent className="space-y-4">
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            onClick={() => {
              setError(null)
              setDays(weekdayTemplate())
            }}
          >
            {t('Заполнить будни')}
          </Button>
        </div>

        <div className="divide-y rounded-lg border">
          {days.map((day) => {
            const label = DAY_LABELS[day.dayOfWeek]
            const isInvalid = !day.isDayOff && day.endTime <= day.startTime

            return (
              <div
                key={day.dayOfWeek}
                className="grid gap-2 p-3 sm:grid-cols-[9rem_minmax(0,1fr)] sm:items-center sm:gap-4"
              >
                <div className="flex items-center gap-2">
                  <Checkbox
                    id={`schedule-${salon.salonId}-${day.dayOfWeek}`}
                    checked={!day.isDayOff}
                    onCheckedChange={(checked) => {
                      updateDay(day.dayOfWeek, { isDayOff: checked !== true })
                    }}
                  />
                  <Label
                    htmlFor={`schedule-${salon.salonId}-${day.dayOfWeek}`}
                    className="cursor-pointer"
                  >
                    {t(label)}
                  </Label>
                </div>

                {day.isDayOff ? (
                  <p className="text-sm text-muted-foreground sm:pl-1">
                    {t('Выходной')}
                  </p>
                ) : (
                  <div className="flex flex-wrap items-center gap-2">
                    <Input
                      type="time"
                      step={1800}
                      value={toTimeInputValue(day.startTime)}
                      aria-label={t('Начало работы, {day}', { day: t(label) })}
                      aria-invalid={isInvalid || undefined}
                      className="h-11 w-32 sm:h-8"
                      onChange={(event) => {
                        updateDay(day.dayOfWeek, {
                          startTime: fromTimeInputValue(event.target.value),
                        })
                      }}
                    />
                    <span className="text-sm text-muted-foreground">—</span>
                    <Input
                      type="time"
                      step={1800}
                      value={toTimeInputValue(day.endTime)}
                      aria-label={t('Окончание работы, {day}', {
                        day: t(label),
                      })}
                      aria-invalid={isInvalid || undefined}
                      className="h-11 w-32 sm:h-8"
                      onChange={(event) => {
                        updateDay(day.dayOfWeek, {
                          endTime: fromTimeInputValue(event.target.value),
                        })
                      }}
                    />
                    {isInvalid && (
                      <p className="w-full text-xs text-destructive">
                        {t('Конец должен быть позже начала.')}
                      </p>
                    )}
                  </div>
                )}
              </div>
            )
          })}
        </div>

        {isAllDaysOff && (
          <p className="flex items-start gap-2 text-sm text-muted-foreground">
            <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
            <span>
              {t(
                'Все дни отмечены выходными: клиенты не увидят свободное время.'
              )}
            </span>
          </p>
        )}

        <p className="text-xs text-muted-foreground">
          {t(
            'Изменение графика не отменяет уже созданные записи. Если запись выйдет за новые часы, она останется в силе — проверьте её в разделе «Записи».'
          )}
        </p>

        {error && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}

        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="button"
            className="h-11 sm:h-8"
            disabled={!isDirty || hasInvalidHours || isSaving}
            aria-busy={isSaving}
            onClick={() => {
              void handleSave()
            }}
          >
            {isSaving && <Loader2 className="size-4 animate-spin" aria-hidden />}
            {t('Сохранить расписание')}
          </Button>

          <span
            className={cn(
              'text-xs text-muted-foreground',
              !isDirty && 'invisible'
            )}
            aria-hidden={!isDirty}
          >
            {t('Есть несохранённые изменения')}
          </span>
        </div>
      </CardContent>
    </Card>
  )
}
