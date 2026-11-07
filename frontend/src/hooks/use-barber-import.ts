import { useCallback, useEffect, useMemo, useRef, useState } from 'react'

import { useBarberBookingOptions } from '@/hooks/use-barber-booking-options'
import { ApiError } from '@/lib/api'
import { createBarberBooking } from '@/lib/barber-booking-create'
import type { BarberBookingOptions } from '@/lib/barber-booking-options'
import {
  fetchBarberSlots,
  todayInTimeZone,
  type BarberSlot,
} from '@/lib/barber-slots'
import { describeBarberBookingCreateFailure } from '@/lib/errors'
import { formatTime } from '@/lib/format'
import {
  IMPORT_MAX_ROWS,
  applyImportRowPatch,
  normalizeImportPhone,
  parseImportText,
  recomputeImportRows,
  type ImportDraft,
  type ImportParseOptions,
  type ImportRowPatch,
} from '@/lib/import-parse'

/**
 * Состояние переноса записей: вставка → проверка → создание.
 *
 * Отправка строго последовательная: порядок ошибок и `429` важнее скорости.
 * Ключ идемпотентности создаётся один раз при разборе и живёт в строке, поэтому
 * повтор после сбоя не создаёт дубль. `POST` после отправки не отменяется —
 * отменить его нельзя, а ключ защищает от второго витка.
 */

export type ImportStage = 'form' | 'preview' | 'result'

export type ImportRowStatus = 'ready' | 'creating' | 'created' | 'failed'

export type ImportRow = {
  draft: ImportDraft
  /** Ключ идемпотентности: один на строку на всё время экрана. */
  idempotencyKey: string
  status: ImportRowStatus
  /** Причина отказа после отправки; `null` — строка ещё не падала. */
  failure: string | null
  /** `409 booking.add.overlap`: можно предложить ближайшее свободное время. */
  overlap: boolean
  /** `409 booking.add.overlap`: ближайшие свободные слоты. */
  slots: BarberSlot[] | null
  slotsStatus: 'idle' | 'loading' | 'error'
}

export type ImportPause =
  | { kind: 'rateLimit'; message: string }
  | { kind: 'network'; message: string }
  | { kind: 'signIn'; message: string }
  | { kind: 'fatal'; message: string }
  | null

export type ImportSummary = {
  total: number
  ready: number
  missingService: number
  past: number
  failed: number
  created: number
}

export type BarberImportController = {
  stage: ImportStage
  text: string
  rows: ImportRow[]
  failure: string | null
  summary: ImportSummary
  isRunning: boolean
  progress: { done: number; total: number } | null
  pause: ImportPause
  optionsStatus: 'loading' | 'ready' | 'error'
  optionsError: string | null
  /** Услуги и салоны профиля для селектов предпросмотра. */
  options: BarberBookingOptions | null
  retryOptions: () => void
  setText: (value: string) => void
  runParse: () => void
  reset: () => void
  /** Вернуться к шагу вставки, сохранив текст. */
  backToForm: () => void
  updateRow: (line: number, patch: ImportRowPatch) => void
  createRows: () => Promise<void>
  resume: () => Promise<void>
  retryFailed: () => Promise<void>
  retryRow: (line: number) => Promise<void>
  loadSlots: (line: number) => Promise<void>
  applySlot: (line: number, slot: BarberSlot) => Promise<void>
}

const FALLBACK_FAILURE = 'Не удалось создать запись.'

const RATE_LIMIT_TEXT = 'Слишком много запросов. Продолжим через минуту.'
const NETWORK_TEXT = 'Связь пропала. Продолжить с этой строки.'
const SIGN_IN_TEXT = 'Сессия истекла — войдите снова.'

const TEXT_EMPTY = 'Вставьте хотя бы одну строку.'
const TEXT_TOO_MANY = `Слишком много строк: максимум ${IMPORT_MAX_ROWS}.`
const TEXT_UNPARSED =
  'Не удалось разобрать ни одной строки. Проверьте, что в каждой есть время и имя.'

const SLOTS_SUGGESTIONS = 3

function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }

  return `import-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

/** Календарная дата момента в поясе салона — для подстановки слота. */
function dateInTimeZone(iso: string, timeZoneId: string): string {
  try {
    return new Intl.DateTimeFormat('en-CA', {
      timeZone: timeZoneId,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(new Date(iso))
  } catch {
    return iso.slice(0, 10)
  }
}

export function useBarberImport(barberProfileId: string): BarberImportController {
  const { options, status: optionsStatus, error: optionsError, retry } =
    useBarberBookingOptions()
  const [stage, setStage] = useState<ImportStage>('form')
  const [text, setText] = useState('')
  const [failure, setFailure] = useState<string | null>(null)
  const [rows, setRows] = useState<ImportRow[]>([])
  const [isRunning, setIsRunning] = useState(false)
  const [progress, setProgress] = useState<{ done: number; total: number } | null>(null)
  const [pause, setPause] = useState<ImportPause>(null)

  // Строки в ref: последовательная отправка читает их между `await`, и состояние
  // React к этому моменту уже устарело бы.
  const rowsRef = useRef<ImportRow[]>([])
  const mountedRef = useRef(true)
  const slotsControllerRef = useRef<AbortController | null>(null)

  useEffect(() => {
    mountedRef.current = true

    return () => {
      mountedRef.current = false
      slotsControllerRef.current?.abort()
    }
  }, [])

  const commit = useCallback((next: ImportRow[]) => {
    rowsRef.current = next

    if (mountedRef.current) {
      setRows(next)
    }
  }, [])

  const buildOptions = useCallback((): ImportParseOptions | null => {
    if (options === null) {
      return null
    }

    const primary = options.salons.find((salon) => salon.isPrimary) ?? options.salons[0]

    if (primary === undefined) {
      return null
    }

    return {
      services: options.services,
      salons: options.salons,
      today: todayInTimeZone(primary.timeZoneId),
      now: Date.now(),
      defaultSalonId: primary.salonId,
    }
  }, [options])

  const summary = useMemo<ImportSummary>(() => {
    return {
      total: rows.length,
      ready: rows.filter(
        (row) => row.draft.issues.length === 0 && row.draft.startAt !== null
      ).length,
      missingService: rows.filter((row) => row.draft.issues.includes('noService')).length,
      past: rows.filter((row) => row.draft.issues.includes('past')).length,
      failed: rows.filter((row) => row.status === 'failed').length,
      created: rows.filter((row) => row.status === 'created').length,
    }
  }, [rows])

  const runParse = useCallback(() => {
    const parseOptions = buildOptions()

    if (parseOptions === null) {
      return
    }

    const result = parseImportText(text, parseOptions)

    if (result.failure === 'empty') {
      setFailure(TEXT_EMPTY)
      return
    }

    if (result.failure === 'tooManyRows') {
      setFailure(TEXT_TOO_MANY)
      return
    }

    if (result.rows.every((row) => row.time === null)) {
      setFailure(TEXT_UNPARSED)
      return
    }

    setFailure(null)
    setPause(null)
    setProgress(null)
    commit(
      result.rows.map((draft) => ({
        draft,
        idempotencyKey: newIdempotencyKey(),
        status: 'ready',
        failure: null,
        overlap: false,
        slots: null,
        slotsStatus: 'idle',
      }))
    )
    setStage('preview')
  }, [buildOptions, commit, text])

  const reset = useCallback(() => {
    setStage('form')
    setText('')
    setFailure(null)
    rowsRef.current = []
    setRows([])
    setProgress(null)
    setPause(null)
  }, [])

  const backToForm = useCallback(() => {
    setStage('form')
    rowsRef.current = []
    setRows([])
    setProgress(null)
    setPause(null)
  }, [])

  const updateRow = useCallback(
    (line: number, patch: ImportRowPatch) => {
      const parseOptions = buildOptions()

      if (parseOptions === null) {
        return
      }

      const phone =
        patch.phone === undefined
          ? undefined
          : patch.phone === null || patch.phone.trim() === ''
            ? null
            : (normalizeImportPhone(patch.phone) ?? patch.phone.trim())

      const drafts = rowsRef.current.map((row) =>
        row.draft.line === line
          ? applyImportRowPatch(row.draft, phone === undefined ? patch : { ...patch, phone })
          : row.draft
      )
      const nextDrafts = recomputeImportRows(drafts, parseOptions)

      commit(
        rowsRef.current.map((row, index) => ({
          ...row,
          draft: nextDrafts[index],
          failure: null,
          overlap: false,
          slots: null,
          slotsStatus: 'idle',
          status: row.status === 'created' ? row.status : 'ready',
        }))
      )
    },
    [buildOptions, commit]
  )

  /** Ближайшие свободные слоты строки после `409`. */
  const loadSlots = useCallback(
    async (line: number) => {
      const row = rowsRef.current.find((item) => item.draft.line === line)

      if (row === undefined || options === null || row.draft.date === null) {
        return
      }

      const service = options.services.find(
        (item) => item.barberServiceId === row.draft.serviceId
      )
      const salon = options.salons.find((item) => item.salonId === row.draft.salonId)

      if (salon === undefined) {
        return
      }

      slotsControllerRef.current?.abort()
      const controller = new AbortController()
      slotsControllerRef.current = controller

      commit(
        rowsRef.current.map((item) =>
          item.draft.line === line
            ? { ...item, slotsStatus: 'loading', slots: null }
            : item
        )
      )

      try {
        const slots = await fetchBarberSlots(
          {
            barberProfileId,
            date: row.draft.date,
            hairServiceId: service?.hairServiceId ?? null,
            salonId: row.draft.salonId ?? salon.salonId,
            own: true,
          },
          controller.signal
        )

        if (!mountedRef.current || controller.signal.aborted) {
          return
        }

        const now = Date.now()
        const upcoming = slots.slots
          .filter((slot) => Date.parse(slot.startAt) > now)
          .slice(0, SLOTS_SUGGESTIONS)

        commit(
          rowsRef.current.map((item) =>
            item.draft.line === line
              ? { ...item, slots: upcoming, slotsStatus: 'idle' }
              : item
          )
        )
      } catch {
        if (!mountedRef.current || controller.signal.aborted) {
          return
        }

        commit(
          rowsRef.current.map((item) =>
            item.draft.line === line
              ? { ...item, slots: null, slotsStatus: 'error' }
              : item
          )
        )
      }
    },
    [barberProfileId, commit, options]
  )

  const send = useCallback(
    async (targets: readonly number[]) => {
      if (targets.length === 0) {
        return
      }

      setIsRunning(true)
      setPause(null)
      setProgress({ done: 0, total: targets.length })

      let done = 0

      for (const line of targets) {
        if (!mountedRef.current) {
          return
        }

        const current = rowsRef.current.find((item) => item.draft.line === line)

        if (current === undefined || current.status === 'created') {
          continue
        }

        commit(
          rowsRef.current.map((item) =>
            item.draft.line === line ? { ...item, status: 'creating' } : item
          )
        )

        try {
          await createBarberBooking({
            customerId: null,
            guestFirstName: current.draft.firstName ?? '',
            guestLastName: current.draft.lastName,
            guestPhone: current.draft.phone ?? '',
            barberServiceId: current.draft.serviceId ?? '',
            salonId: current.draft.salonId ?? '',
            startAt: current.draft.startAt ?? '',
            endAt: current.draft.endAt ?? '',
            idempotencyKey: current.idempotencyKey,
            // Исходная строка в заметку не попадает: её увидит клиент.
            customerNote: null,
          })

          if (!mountedRef.current) {
            return
          }

          done += 1
          commit(
            rowsRef.current.map((item) =>
              item.draft.line === line
                ? { ...item, status: 'created', failure: null, overlap: false }
                : item
            )
          )
          setProgress({ done, total: targets.length })
        } catch (cause: unknown) {
          if (!mountedRef.current) {
            return
          }

          const stop = describeStop(cause)

          if (stop !== null) {
            // Строка остаётся готовой: «Продолжить» начнёт ровно с неё.
            commit(
              rowsRef.current.map((item) =>
                item.draft.line === line ? { ...item, status: 'ready' } : item
              )
            )
            setPause(stop)
            setIsRunning(false)
            return
          }

          const isOverlap =
            cause instanceof ApiError && cause.hasCode('booking.add.overlap')
          const failureMessage =
            cause instanceof ApiError && cause.hasCode('booking.create.startAt.inPast')
              ? 'Время уже прошло.'
              : isOverlap
                ? 'Время занято.'
                : describeBarberBookingCreateFailure(cause, FALLBACK_FAILURE).message

          done += 1
          commit(
            rowsRef.current.map((item) =>
              item.draft.line === line
                ? { ...item, status: 'failed', failure: failureMessage, overlap: isOverlap }
                : item
            )
          )
          setProgress({ done, total: targets.length })

          if (isOverlap) {
            await loadSlots(line)
          }
        }
      }

      if (!mountedRef.current) {
        return
      }

      setIsRunning(false)
      setStage('result')
    },
    [commit, loadSlots]
  )

  const readyLines = useCallback(
    (statuses: readonly ImportRowStatus[]) =>
      rowsRef.current
        .filter(
          (row) =>
            statuses.includes(row.status)
            && row.draft.issues.length === 0
            && row.draft.startAt !== null
            && row.draft.endAt !== null
        )
        .map((row) => row.draft.line),
    []
  )

  const createRows = useCallback(async () => {
    setStage('preview')
    await send(readyLines(['ready']))
  }, [readyLines, send])

  const resume = useCallback(async () => {
    await send(readyLines(['ready', 'creating']))
  }, [readyLines, send])

  const retryFailed = useCallback(async () => {
    const lines = readyLines(['failed'])

    if (lines.length === 0) {
      return
    }

    commit(
      rowsRef.current.map((row) =>
        lines.includes(row.draft.line)
          ? { ...row, status: 'ready', failure: null, overlap: false, slots: null }
          : row
      )
    )
    setStage('preview')
    await send(lines)
  }, [commit, readyLines, send])

  const retryRow = useCallback(
    async (line: number) => {
      const row = rowsRef.current.find((item) => item.draft.line === line)

      if (
        row === undefined
        || row.draft.issues.length > 0
        || row.draft.startAt === null
      ) {
        return
      }

      commit(
        rowsRef.current.map((item) =>
          item.draft.line === line
            ? { ...item, status: 'ready', failure: null, overlap: false, slots: null }
            : item
        )
      )
      setStage('preview')
      await send([line])
    },
    [commit, send]
  )

  /** Подстановка ближайшего слота: время строки меняется, и она сразу отправляется. */
  const applySlot = useCallback(
    async (line: number, slot: BarberSlot) => {
      const row = rowsRef.current.find((item) => item.draft.line === line)
      const salon = options?.salons.find(
        (item) => item.salonId === row?.draft.salonId
      )

      if (row === undefined || salon === undefined) {
        return
      }

      updateRow(line, {
        date: dateInTimeZone(slot.startAt, salon.timeZoneId),
        time: formatTime(slot.startAt, salon.timeZoneId),
      })
      await retryRow(line)
    },
    [options, retryRow, updateRow]
  )

  return {
    stage,
    text,
    rows,
    failure,
    summary,
    isRunning,
    progress,
    pause,
    optionsStatus,
    optionsError,
    options,
    retryOptions: retry,
    setText,
    runParse,
    reset,
    backToForm,
    updateRow,
    createRows,
    resume,
    retryFailed,
    retryRow,
    loadSlots,
    applySlot,
  }
}

/**
 * Ошибка, которая останавливает пачку.
 *
 * `429` и сеть лечатся повтором с той же строки, `401` — входом, `403`/`404`
 * означают, что профиль или услуга исчезли: остальные строки трогать нельзя.
 */
function describeStop(error: unknown): ImportPause {
  if (!(error instanceof ApiError)) {
    return { kind: 'network', message: NETWORK_TEXT }
  }

  if (error.status === 429) {
    return { kind: 'rateLimit', message: RATE_LIMIT_TEXT }
  }

  if (error.status === 0 || error.status >= 500) {
    return { kind: 'network', message: NETWORK_TEXT }
  }

  if (error.status === 401) {
    return { kind: 'signIn', message: SIGN_IN_TEXT }
  }

  if (error.status === 403 || error.status === 404) {
    return {
      kind: 'fatal',
      message: describeBarberBookingCreateFailure(error, FALLBACK_FAILURE).message,
    }
  }

  return null
}
