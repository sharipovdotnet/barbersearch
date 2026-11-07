import { useCallback, useEffect, useRef, useState } from 'react'

import {
  describeNotificationSettingsFailure,
  describeNotificationSettingsLoadError,
  fetchSettings,
  updateSettings,
  type NotificationSettings,
  type NotificationSettingsField,
  type NotificationSettingsInput,
} from '@/lib/barber-notifications'

/** Одна `PUT` на пачку изменений: пятьсот миллисекунд после последней правки. */
export const NOTIFICATION_SETTINGS_DEBOUNCE_MS = 500

export type NotificationSettingsStatus = 'loading' | 'ready' | 'error'

export type NotificationSettingsController = {
  status: NotificationSettingsStatus
  settings: NotificationSettings | null
  /** Ошибка чтения настроек; при уже показанных настройках — ошибка обновления. */
  loadError: string | null
  /** Ошибка формы: сеть, `429`, `403` или иной код без привязки к полю. */
  saveError: string | null
  /** `400 notificationSettings.*` — сообщение у конкретного поля. */
  fieldErrors: Partial<Record<NotificationSettingsField, string>>
  isSaving: boolean
  /** Тихое перечитывание (кнопка «Проверить» в блоке Telegram). */
  isRefreshing: boolean
  /**
   * Меняет значение и планирует одну `PUT` через 500 мс.
   *
   * Новый вызов отменяет предыдущий таймер: уходит последнее состояние, а не очередь
   * промежуточных. Ошибки полей сбрасываются при следующей правке.
   */
  update: (patch: Partial<NotificationSettingsInput>) => void
  reload: () => void
}

type SettingsState = {
  status: NotificationSettingsStatus
  settings: NotificationSettings | null
  loadError: string | null
  isRefreshing: boolean
}

function toInput(settings: NotificationSettings): NotificationSettingsInput {
  return {
    isEnabled: settings.isEnabled,
    digestEnabled: settings.digestEnabled,
    digestTime: settings.digestTime,
    leadMinutes: settings.leadMinutes,
  }
}

/**
 * Настройки напоминаний: чтение и дебаунс-сохранение.
 *
 * `PUT` шлёт полный набор изменяемых полей, поэтому одно изменение не затирает
 * соседние. Ответ сервера применяется, только если пользователь не успел изменить
 * что-то ещё за время запроса; иначе локальное состояние остаётся новее серверного,
 * а следующий таймер сохранит его. `400` с публичным кодом показывается у поля,
 * остальные ошибки — общей строкой.
 */
export function useNotificationSettings(): NotificationSettingsController {
  const [state, setState] = useState<SettingsState>({
    status: 'loading',
    settings: null,
    loadError: null,
    isRefreshing: false,
  })
  const [saveError, setSaveError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<
    Partial<Record<NotificationSettingsField, string>>
  >({})
  const [isSaving, setIsSaving] = useState(false)
  const [attempt, setAttempt] = useState(0)

  // Последнее локальное значение: оно уйдёт в таймер, даже если состояние ещё не
  // перерисовалось. Серверный ответ обновляет его только когда новых правок не было.
  const latestRef = useRef<NotificationSettingsInput | null>(null)
  const timerRef = useRef<number | null>(null)
  const saveControllerRef = useRef<AbortController | null>(null)
  const saveVersionRef = useRef(0)
  const mountedRef = useRef(true)

  useEffect(() => {
    mountedRef.current = true

    return () => {
      mountedRef.current = false

      if (timerRef.current !== null) {
        window.clearTimeout(timerRef.current)
        timerRef.current = null
      }

      saveControllerRef.current?.abort()
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    let isActive = true

    fetchSettings(controller.signal)
      .then((settings) => {
        if (!isActive) {
          return
        }

        const input = toInput(settings)
        latestRef.current = input

        setState({
          status: 'ready',
          settings,
          loadError: null,
          isRefreshing: false,
        })
        setFieldErrors({})
        setSaveError(null)
        setIsSaving(false)
      })
      .catch((cause: unknown) => {
        if (!isActive || controller.signal.aborted) {
          return
        }

        const message = describeNotificationSettingsLoadError(cause)

        // Уже показанные настройки не прячутся: ошибка чтения — это баннер, а не
        // скелетон на месте формы.
        setState((current) => ({
          status: current.settings === null ? 'error' : 'ready',
          settings: current.settings,
          loadError: message,
          isRefreshing: false,
        }))
      })

    return () => {
      isActive = false
      controller.abort()
    }
  }, [attempt])

  const save = useCallback(
    async (input: NotificationSettingsInput): Promise<void> => {
      const version = saveVersionRef.current + 1
      saveVersionRef.current = version
      saveControllerRef.current?.abort()
      const controller = new AbortController()
      saveControllerRef.current = controller

      setIsSaving(true)

      try {
        const settings = await updateSettings(input, controller.signal)

        if (!mountedRef.current || version !== saveVersionRef.current) {
          return
        }

        // Пока запрос летел, пользователь мог изменить другое поле: серверный ответ
        // не должен откатывать более новое локальное состояние.
        if (latestRef.current === input) {
          const confirmed = toInput(settings)
          latestRef.current = confirmed

          setState((current) => ({
            ...current,
            status: 'ready',
            settings,
            loadError: null,
          }))
        }

        setFieldErrors({})
        setSaveError(null)
      } catch (cause) {
        if (
          !mountedRef.current ||
          version !== saveVersionRef.current ||
          controller.signal.aborted
        ) {
          return
        }

        // Ошибка относится к устаревшему значению: пользователь уже изменил поле,
        // и следующий таймер отправит новое. Показывать старый `400` у нового
        // значения было бы враньём.
        if (latestRef.current !== input) {
          return
        }

        const failure = describeNotificationSettingsFailure(cause)

        if (failure.field === null) {
          setFieldErrors({})
          setSaveError(failure.message)
        } else {
          setSaveError(null)
          setFieldErrors({ [failure.field]: failure.message })
        }
      } finally {
        if (version === saveVersionRef.current) {
          setIsSaving(false)
        }
      }
    },
    []
  )

  const update = useCallback(
    (patch: Partial<NotificationSettingsInput>) => {
      setState((current) => {
        if (current.settings === null) {
          return current
        }

        return {
          ...current,
          settings: { ...current.settings, ...patch },
        }
      })

      const base = latestRef.current

      if (base === null) {
        return
      }

      const next: NotificationSettingsInput = { ...base, ...patch }
      latestRef.current = next
      setFieldErrors({})
      setSaveError(null)

      if (timerRef.current !== null) {
        window.clearTimeout(timerRef.current)
      }

      timerRef.current = window.setTimeout(() => {
        timerRef.current = null
        void save(next)
      }, NOTIFICATION_SETTINGS_DEBOUNCE_MS)
    },
    [save]
  )

  const reload = useCallback(() => {
    if (timerRef.current !== null) {
      window.clearTimeout(timerRef.current)
      timerRef.current = null
    }

    // Незавершённое сохранение больше не актуально: перечитывание показывает сервер.
    saveControllerRef.current?.abort()
    saveVersionRef.current += 1
    setIsSaving(false)

    setState((current) => ({
      ...current,
      status: current.settings === null ? 'loading' : 'ready',
      loadError: null,
      isRefreshing: current.settings !== null,
    }))
    setAttempt((current) => current + 1)
  }, [])

  return {
    status: state.status,
    settings: state.settings,
    loadError: state.loadError,
    saveError,
    fieldErrors,
    isSaving,
    isRefreshing: state.isRefreshing,
    update,
    reload,
  }
}
