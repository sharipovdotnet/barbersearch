import type { RefObject } from 'react'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { useI18n } from '@/i18n'
import type { ScheduleConflict, ScheduleWriteKind } from '@/lib/barber-schedule'
import { bookingStatusLabel } from '@/lib/booking-status'
import { formatBookingsCount, formatDateTime } from '@/lib/format'

/** Больше двадцати строк не объясняют, а пугают: остальные считаются одной строкой. */
const VISIBLE_CONFLICTS = 20

type Props = {
  open: boolean
  kind: ScheduleWriteKind
  conflicts: readonly ScheduleConflict[]
  isSaving: boolean
  error: string | null
  /**
   * Кнопка, которая открыла диалог.
   *
   * `disabled` на время запроса снимает с неё фокус (браузер уводит его в `body`),
   * поэтому Base UI к моменту открытия уже не помнит «предыдущий» элемент и сам
   * вернуть фокус не может. `finalFocus` — штатный способ Base UI указать цель
   * возврата: кнопка к моменту закрытия снова доступна, и `ref.focus()` не нужен.
   */
  finalFocus?: RefObject<HTMLElement | null>
  onCancel: () => void
  onConfirm: () => void
}

/**
 * Подтверждение правки расписания, которая оставила активные визиты вне часов.
 *
 * Это `alertdialog`, а не обычная форма: сервер уже отказался писать, поэтому
 * решение принимается здесь. «Всё равно сохранить» — намеренно **не** `destructive`:
 * визит не удаляется и не отменяется, барбер подтверждает, что клиент всё равно
 * придёт. Контент монтируется на каждое открытие, поэтому список с прошлой правки
 * не переносится; фокус после закрытия Base UI возвращает кнопке, открывшей диалог.
 */
export function ScheduleConflictsDialog({
  open,
  kind,
  conflicts,
  isSaving,
  error,
  finalFocus,
  onCancel,
  onConfirm,
}: Props) {
  const { t } = useI18n()
  const hidden = conflicts.length - VISIBLE_CONFLICTS

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          onCancel()
        }
      }}
    >
      {open && (
        <DialogContent role="alertdialog" finalFocus={finalFocus}>
          <DialogHeader>
            <DialogTitle>
              {t(
                kind === 'hours'
                  ? 'Записи вне новых часов'
                  : 'Записи в нерабочем периоде'
              )}
            </DialogTitle>
            <DialogDescription>
              {formatBookingsCount(conflicts.length)}.{' '}
              {t(
                'Клиенты придут в назначенное время — визит можно отменить в разделе «Записи».'
              )}
            </DialogDescription>
          </DialogHeader>

          <ul className="max-h-64 divide-y overflow-y-auto rounded-lg border">
            {conflicts.slice(0, VISIBLE_CONFLICTS).map((conflict) => (
              <li
                key={conflict.id}
                className="flex flex-wrap items-baseline gap-x-2 p-2"
              >
                <span className="font-medium">
                  {formatDateTime(conflict.startAt, conflict.timeZoneId)}
                </span>
                <span className="text-muted-foreground">
                  {conflict.customerName} · {conflict.serviceName} ·{' '}
                  {bookingStatusLabel(conflict.status)}
                </span>
              </li>
            ))}
          </ul>

          {hidden > 0 && (
            <p className="text-sm text-muted-foreground">
              {t('и ещё {list}', { list: formatBookingsCount(hidden) })}
            </p>
          )}

          {error && (
            <p role="alert" className="text-sm text-destructive">
              {error}
            </p>
          )}

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              // Enter по инерции не должен подтверждать запись: первым в фокус
              // встаёт отказ, а не «Всё равно сохранить».
              autoFocus
              disabled={isSaving}
              onClick={onCancel}
            >
              {t('Отмена')}
            </Button>
            <Button type="button" disabled={isSaving} onClick={onConfirm}>
              {isSaving ? t('Сохраняем…') : t('Всё равно сохранить')}
            </Button>
          </DialogFooter>
        </DialogContent>
      )}
    </Dialog>
  )
}
