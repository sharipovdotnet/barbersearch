import { useRef, useState } from 'react'
import { Loader2, Trash2 } from 'lucide-react'

import { ScheduleConflictsDialog } from '@/components/barber/schedule-conflicts-dialog'
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
import { useScheduleWrite } from '@/hooks/use-schedule-write'
import { useI18n } from '@/i18n'
import { todayInTimeZone } from '@/lib/barber-slots'
import {
  addTimeOff,
  describeScheduleError,
  formatTimeOffRange,
  localDateToUtcIso,
  removeTimeOff,
  type BarberScheduleTimeOff,
} from '@/lib/barber-schedule'

type Props = {
  barberProfileId: string
  timeOffs: readonly BarberScheduleTimeOff[]
  /** Пояс основного салона: в нём вводятся и показываются даты отпуска. */
  timeZoneId: string
  onChanged: (message: string) => void
}

/**
 * Отпуска и другие нерабочие периоды.
 *
 * `TimeOff` принадлежит профилю, а не салону, поэтому отпуск действует везде.
 * Пользователь вводит локальные даты включительно, а API получает исключающую
 * верхнюю границу в UTC — «с 1 по 10» превращается в `1 00:00` … `11 00:00`.
 */
export function BarberTimeOffs({
  barberProfileId,
  timeOffs,
  timeZoneId,
  onChanged,
}: Props) {
  const { t } = useI18n()
  const today = todayInTimeZone(timeZoneId)
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [reason, setReason] = useState('')
  const [deleting, setDeleting] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const write = useScheduleWrite('timeOff', {
    fallback: t('Не удалось добавить отпуск.'),
    onSaved: () => {
      setFrom('')
      setTo('')
      setReason('')
      onChanged(t('Отпуск добавлен.'))
    },
  })
  const addRef = useRef<HTMLButtonElement>(null)

  const isRangeInvalid = from !== '' && to !== '' && to < from
  const shownError = write.error ?? error

  function clearErrors() {
    write.clearError()
    setError(null)
  }

  function handleAdd() {
    if (write.isSaving || from === '' || to === '' || isRangeInvalid) {
      return
    }

    setError(null)

    void write.save((force) =>
      addTimeOff(
        barberProfileId,
        {
          startAt: localDateToUtcIso(from, timeZoneId),
          endAt: localDateToUtcIso(to, timeZoneId, 'end'),
          reason: reason.trim() === '' ? null : reason.trim(),
        },
        force
      )
    )
  }

  async function handleDelete(timeOffId: string) {
    if (deleting !== null) {
      return
    }

    setDeleting(timeOffId)
    setError(null)

    try {
      await removeTimeOff(barberProfileId, timeOffId)
      onChanged(t('Отпуск удалён.'))
    } catch (cause) {
      setError(describeScheduleError(cause, t('Не удалось удалить отпуск.')))
    } finally {
      setDeleting(null)
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('Отпуска и нерабочие периоды')}</CardTitle>
        <CardDescription>
          {t(
            'Период действует во всех салонах и сильнее недельного расписания.'
          )}{' '}
          {t('Даты — в поясе основного салона')}{' '}
          <span className="whitespace-nowrap">{timeZoneId}</span>.
        </CardDescription>
      </CardHeader>

      <CardContent className="space-y-5">
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label htmlFor="time-off-from">{t('С')}</Label>
            <Input
              id="time-off-from"
              type="date"
              value={from}
              min={today}
              className="h-11 sm:h-8"
              onChange={(event) => {
                clearErrors()
                setFrom(event.target.value)
              }}
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="time-off-to">{t('По')}</Label>
            <Input
              id="time-off-to"
              type="date"
              value={to}
              min={from || today}
              aria-invalid={isRangeInvalid || undefined}
              className="h-11 sm:h-8"
              onChange={(event) => {
                clearErrors()
                setTo(event.target.value)
              }}
            />
          </div>
        </div>

        {isRangeInvalid && (
          <p className="text-sm text-destructive">
            {t('Конец периода должен быть не раньше начала.')}
          </p>
        )}

        <div className="space-y-1.5">
          <Label htmlFor="time-off-reason">
            {t('Пометка')}{' '}
            <span className="text-muted-foreground">{t('(необязательно)')}</span>
          </Label>
          <Input
            id="time-off-reason"
            value={reason}
            maxLength={500}
            placeholder={t('Например, отпуск')}
            className="h-11 sm:h-8"
            onChange={(event) => {
              clearErrors()
              setReason(event.target.value)
            }}
          />
        </div>

        <p className="text-xs text-muted-foreground">
          {t(
            'Изменение графика не отменяет записи: если визит выйдет за новые часы, сохранение попросит подтвердить.'
          )}
        </p>

        {shownError && (
          <p role="alert" className="text-sm text-destructive">
            {shownError}
          </p>
        )}

        <Button
          ref={addRef}
          type="button"
          className="h-11 sm:h-8"
          disabled={write.isSaving || from === '' || to === '' || isRangeInvalid}
          aria-busy={write.isSaving}
          onClick={handleAdd}
        >
          {write.isSaving && (
            <Loader2 className="size-4 animate-spin" aria-hidden />
          )}
          {t('Добавить отпуск')}
        </Button>

        {timeOffs.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            {t('Отпусков пока нет.')}
          </p>
        ) : (
          <ul className="divide-y rounded-lg border">
            {timeOffs.map((timeOff) => (
              <li
                key={timeOff.id}
                className="flex flex-wrap items-center justify-between gap-3 p-3"
              >
                <div className="min-w-0 space-y-0.5">
                  <p className="text-sm font-medium">
                    {formatTimeOffRange(
                      timeOff.startAt,
                      timeOff.endAt,
                      timeZoneId
                    )}
                  </p>
                  {timeOff.reason && (
                    <p className="text-sm text-muted-foreground">
                      {timeOff.reason}
                    </p>
                  )}
                </div>

                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="h-11 text-destructive hover:text-destructive sm:h-8"
                  disabled={deleting !== null}
                  aria-label={t('Удалить отпуск {range}', {
                    range: formatTimeOffRange(
                      timeOff.startAt,
                      timeOff.endAt,
                      timeZoneId
                    ),
                  })}
                  onClick={() => {
                    void handleDelete(timeOff.id)
                  }}
                >
                  {deleting === timeOff.id ? (
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

      <ScheduleConflictsDialog
        open={write.conflicts !== null}
        kind="timeOff"
        conflicts={write.conflicts ?? []}
        isSaving={write.isSaving}
        error={write.error}
        finalFocus={addRef}
        onCancel={write.cancel}
        onConfirm={() => {
          void write.confirm()
        }}
      />
    </Card>
  )
}
