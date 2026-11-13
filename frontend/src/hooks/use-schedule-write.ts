import { useRef, useState } from 'react'

import { t } from '@/i18n'
import { ApiError } from '@/lib/api'
import {
  describeScheduleError,
  readScheduleConflicts,
  type ScheduleConflict,
  type ScheduleWriteKind,
} from '@/lib/barber-schedule'

/** Один и тот же запрос с `force`: карточка подставляет свой вызов API. */
export type ScheduleWriteSend = (force: boolean) => Promise<void>

type ScheduleWriteOptions = {
  /** Текст ошибки, когда код сервера неизвестен. */
  fallback: string
  /** Успех: страница объявляет сообщение и перечитывает расписание. */
  onSaved: () => void
}

export type ScheduleWrite = {
  /** Идёт любая из двух попыток: обычная или с `force`. */
  isSaving: boolean
  /** Открыт ли диалог: `null` — конфликтов нет. */
  conflicts: ScheduleConflict[] | null
  error: string | null
  /** Первая попытка с `force=false`; при `409` открывает диалог. */
  save: (send: ScheduleWriteSend) => Promise<void>
  /** Повтор с `force=true` по последнему `send`. */
  confirm: () => Promise<void>
  /** Закрыть диалог, сохранив введённое. */
  cancel: () => void
  /** Сброс ошибки на правку поля. */
  clearError: () => void
}

/**
 * Сохранение расписания с подтверждением конфликта.
 *
 * Один контроллер на три карточки: неделя, исключение и отпуск отличаются только
 * вызовом API и текстом успеха, а диалог, повтор и состояния — общие. Состояние
 * меняется в обработчике клика, `useEffect` для «открыть диалог по ошибке» нет:
 * иначе диалог мог бы открыться от чужой ошибки или после размонтирования.
 *
 * Повторный `409` на `force=true` — не сбой, а новые данные: список обновляется,
 * диалог остаётся открытым, и подтвердить можно ещё раз.
 *
 * `kind` входит в подпись §4.2, но всё, что от него зависит (заголовок диалога),
 * рендерит карточка. Префикс `_` оставляет вызов совместимым с подписью и
 * удовлетворяет `noUnusedParameters`.
 */
export function useScheduleWrite(
  _kind: ScheduleWriteKind,
  options: ScheduleWriteOptions
): ScheduleWrite {
  const [isSaving, setIsSaving] = useState(false)
  const [conflicts, setConflicts] = useState<ScheduleConflict[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const lastSendRef = useRef<ScheduleWriteSend | null>(null)
  const inFlightRef = useRef(false)

  async function run(send: ScheduleWriteSend, force: boolean): Promise<void> {
    if (inFlightRef.current) {
      return
    }

    inFlightRef.current = true
    setIsSaving(true)
    setError(null)

    try {
      await send(force)

      lastSendRef.current = null
      setConflicts(null)
      options.onSaved()
    } catch (cause) {
      const list = readScheduleConflicts(cause)

      if (list) {
        setConflicts(list)
        return
      }

      // Диалог уже закрыт: текст ошибки показывает карточка. `409` с кодом, но без
      // читаемого списка (§4.1) — это «данные изменились», а не «повторите»; ветку
      // держим здесь, потому что §4.4 запрещает обрабатывать код в describeScheduleError.
      setConflicts(null)
      setError(
        isUnreadableConflict(cause)
          ? t('Расписание не сохранено: записи изменились, обновите страницу.')
          : describeScheduleError(cause, options.fallback)
      )
    } finally {
      inFlightRef.current = false
      setIsSaving(false)
    }
  }

  function save(send: ScheduleWriteSend): Promise<void> {
    lastSendRef.current = send

    return run(send, false)
  }

  function confirm(): Promise<void> {
    const send = lastSendRef.current

    return send ? run(send, true) : Promise.resolve()
  }

  function cancel(): void {
    lastSendRef.current = null
    setConflicts(null)
  }

  function clearError(): void {
    setError(null)
  }

  return { isSaving, conflicts, error, save, confirm, cancel, clearError }
}

/** `409 barberSchedule.conflict`, тело которого не разобрал `readScheduleConflicts`. */
function isUnreadableConflict(error: unknown): boolean {
  return (
    error instanceof ApiError &&
    error.status === 409 &&
    error.hasCode('barberSchedule.conflict')
  )
}
