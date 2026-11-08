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
import { decodeIcsFile } from '@/lib/ics-decode'
import { icsIdempotencyKey } from '@/lib/ics-identity'
import { mapIcsOccurrences } from '@/lib/ics-map'
import {
  parseIcs,
  type IcsParseFailure,
  type IcsParseOptions,
  type IcsSkippedEvent,
} from '@/lib/ics-parse'
import {
  IMPORT_MAX_ROWS,
  applyImportRowPatch,
  normalizeImportPhone,
  parseImportText,
  recomputeImportRows,
  type ImportDraft,
  type ImportParseOptions,
  type ImportRowPatch,
  type ImportSource,
} from '@/lib/import-parse'
import { salonDateFromInstant, salonTimeFromInstant } from '@/lib/salon-time'

/**
 * Состояние переноса записей: файл календаря или вставка → проверка → создание.
 *
 * Отправка строго последовательная: порядок ошибок и `429` важнее скорости.
 * Ключ идемпотентности создаётся один раз при разборе и живёт в строке, поэтому
 * повтор после сбоя не создаёт дубль. Для `.ics` ключ детерминирован по `UID`:
 * повторная загрузка того же файла возвращает уже созданные брони. `POST` после
 * отправки не отменяется — отменить его нельзя, а ключ защищает от второго витка.
 *
 * Файл хранится в `ref`, а не в состоянии: повторный разбор после «Изменить файл»
 * не должен зависеть от ре-рендера, и сырые байты нечего держать в снапшоте.
 * Токен разбора отбрасывает поздний результат, если барбер выбрал другой файл
 * или ушёл со страницы.
 */

export type { ImportSource }

export type ImportStage = 'form' | 'preview' | 'result'

export type ImportPeriodDays = 7 | 30 | 90

export type ImportFileInfo = {
  name: string
  size: number
}

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
  /** Галочка «Перенести событие»: отправляются только выбранные строки. */
  included: boolean
}

export type ImportPause =
  | { kind: 'rateLimit'; message: string }
  | { kind: 'network'; message: string }
  | { kind: 'signIn'; message: string }
  | { kind: 'fatal'; message: string }
  | null

export type ImportSummary = {
  /** Все разобранные строки. */
  total: number
  /** Строки с галочкой. */
  selected: number
  /** Выбранные, готовые к отправке строки. */
  ready: number
  created: number
  failed: number
  /** Выбранные строки без услуги или с неоднозначной услугой. */
  missingService: number
  /** Выбранные строки без телефона или с неполным телефоном. */
  missingPhone: number
  /** Выбранные строки, время которых уже прошло. */
  past: number
  /** Пропущенные события календаря. */
  skipped: number
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
  source: ImportSource
  setSource: (source: ImportSource) => void
  file: ImportFileInfo | null
  periodDays: ImportPeriodDays
  setPeriod: (days: ImportPeriodDays) => void
  isParsing: boolean
  skipped: IcsSkippedEvent[]
  /** «За один раз — не больше 50 записей.» после попытки выбрать 51-ю. */
  selectionNotice: string | null
  /** Выбор файла: сохраняет его и сбрасывает прежний разбор, но не парсит. */
  selectFile: (file: File) => void
  /** Разбор сохранённого файла; без выбранного файла — no-op. */
  parseFile: () => Promise<void>
  toggleRow: (line: number) => void
  setAllIncluded: (included: boolean) => void
  /** Есть ли не созданные строки для «Перенести ещё». */
  canContinue: boolean
  continueImport: () => void
  /** Вернуться к предпросмотру из отчёта, не меняя строки и выбор. */
  reviewRows: () => void
  setText: (value: string) => void
  runParse: () => void
  reset: () => void
  /** Вернуться к шагу вставки, сохранив текст, файл и период. */
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

/**
 * Текст отказа одной строки.
 *
 * Коды окна и длительности важнее `overlap`: «Обновите свободное время» не
 * лечит запись, которую сервер не примет из-за 90-дневного предела или
 * слишком длинной услуги.
 */
function createFailureText(cause: unknown, isOverlap: boolean): string {
  if (cause instanceof ApiError) {
    if (cause.hasCode('booking.create.startAt.inPast')) {
      return 'Время уже прошло.'
    }

    if (cause.hasCode('booking.create.startAt.tooFar')) {
      return 'Слишком далеко: запись можно создать не более чем на 90 дней вперёд.'
    }

    if (cause.hasCode('booking.create.period.tooLong')) {
      return 'Слишком длинное событие.'
    }
  }

  if (isOverlap) {
    return 'Время занято.'
  }

  return describeBarberBookingCreateFailure(cause, FALLBACK_FAILURE).message
}

const RATE_LIMIT_TEXT = 'Слишком много запросов. Продолжим через минуту.'
const NETWORK_TEXT = 'Связь пропала. Продолжить с этой строки.'
const SIGN_IN_TEXT = 'Сессия истекла — войдите снова.'

const TEXT_EMPTY = 'Вставьте хотя бы одну строку.'
const TEXT_TOO_MANY = `Слишком много строк: максимум ${IMPORT_MAX_ROWS}.`
const TEXT_UNPARSED =
  'Не удалось разобрать ни одной строки. Проверьте, что в каждой есть время и имя.'

const SELECTION_LIMIT_TEXT = 'За один раз — не больше 50 записей.'

/** Ошибки чтения: файл не прочитался или разбор упал уже после декодирования. */
const READ_FAILURE = 'Не удалось прочитать файл. Попробуйте ещё раз.'
/** Ошибка загрузки ленивого чанка `ical.js`: это связь, а не содержимое файла. */
const CHUNK_FAILURE =
  'Не удалось загрузить обработчик календаря. Проверьте связь и повторите.'

/**
 * Коды `.ics` → текст формы (спека §8, байт в байт).
 *
 * Один словарь на чтение и разбор: барберу не важно, на каком шаге файл
 * оказался не календарём, а формулировка должна быть одинаковой.
 */
const FAILURE_TEXT: Record<Exclude<IcsParseFailure, null>, string> = {
  tooLarge:
    'Файл больше 2 МБ. Выгрузите только нужный календарь или меньший период.',
  notCalendar: 'Это не файл календаря: нужен .ics с BEGIN:VCALENDAR.',
  invalid:
    'Не удалось разобрать календарь. Файл повреждён, содержит несколько календарей или использует неподдерживаемый формат.',
  invalidEncoding:
    'Не удалось прочитать файл. Сохраните календарь в кодировке UTF-8.',
  empty: 'В файле нет будущих событий с датой и временем.',
  noEventsInPeriod: 'В выбранном периоде нет событий — увеличьте период.',
  tooManyEvents: 'В файле больше 5000 событий. Выгрузите один календарь.',
  tooManyOccurrences:
    'В выбранном периоде больше 500 событий — уменьшите период.',
}

const SLOTS_SUGGESTIONS = 3

/** Мягкий предел длительности импорта: длиннее — уже не визит. */
const MAX_IMPORT_DURATION_MINUTES = 720

function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }

  return `import-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

/**
 * Полная и готовая строка: именно такие уходят в `POST` и считаются в «Готово».
 *
 * `issues.length === 0` уже почти всё гарантирует, но проверки полей дублируются
 * явно: строка с пустым телефоном не должна попасть в отправку даже при
 * рассинхронизированных ошибках.
 */
function isCompleteDraft(draft: ImportDraft): boolean {
  if (draft.issues.length > 0) {
    return false
  }

  if (
    draft.startAt === null
    || draft.endAt === null
    || draft.phone === null
    || draft.phone.trim() === ''
    || draft.firstName === null
    || draft.serviceId === null
    || draft.salonId === null
  ) {
    return false
  }

  const start = Date.parse(draft.startAt)
  const end = Date.parse(draft.endAt)

  if (Number.isNaN(start) || Number.isNaN(end)) {
    return false
  }

  const minutes = (end - start) / 60_000

  return minutes > 0 && minutes <= MAX_IMPORT_DURATION_MINUTES
}

/**
 * Стартовый выбор календаря: первые 50 строк по времени среди тех, у кого
 * `defaultIncluded`. Вхождения уже отсортированы, но порядок строк проверяем
 * ещё раз: пачка ограничена по времени, а не по порядку в файле.
 */
function selectDefaultLines(drafts: readonly ImportDraft[]): Set<number> {
  const ordered = [...drafts].sort((left, right) => {
    const leftStart = left.startAt === null ? 0 : Date.parse(left.startAt)
    const rightStart = right.startAt === null ? 0 : Date.parse(right.startAt)

    return leftStart - rightStart || left.line - right.line
  })
  const lines = new Set<number>()

  for (const draft of ordered) {
    if (lines.size >= IMPORT_MAX_ROWS) {
      break
    }

    if (draft.defaultIncluded) {
      lines.add(draft.line)
    }
  }

  return lines
}

/**
 * Явная загрузка календарного чанка.
 *
 * `parseIcs` ловит ошибку собственного `import()` и возвращает `invalid`; чтобы
 * отличить «нет связи» от «файл повреждён», чанк загружается здесь. Модуль уже
 * в кеше, поэтому второй `import()` внутри `parseIcs` бесплатный.
 */
async function loadIcsParser(): Promise<boolean> {
  try {
    const module = await import('ical.js')

    return typeof module === 'object'
  } catch {
    return false
  }
}

export function useBarberImport(barberProfileId: string): BarberImportController {
  const { options, status: optionsStatus, error: optionsError, retry } =
    useBarberBookingOptions()
  const [source, setSourceState] = useState<ImportSource>('ics')
  const [stage, setStage] = useState<ImportStage>('form')
  const [text, setText] = useState('')
  const [file, setFile] = useState<ImportFileInfo | null>(null)
  const [periodDays, setPeriodDays] = useState<ImportPeriodDays>(30)
  const [isParsing, setIsParsing] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)
  const [skipped, setSkipped] = useState<IcsSkippedEvent[]>([])
  const [selectionNotice, setSelectionNotice] = useState<string | null>(null)
  const [rows, setRows] = useState<ImportRow[]>([])
  const [isRunning, setIsRunning] = useState(false)
  const [progress, setProgress] = useState<{ done: number; total: number } | null>(null)
  const [pause, setPause] = useState<ImportPause>(null)

  // Строки в ref: последовательная отправка читает их между `await`, и состояние
  // React к этому моменту уже устарело бы.
  const rowsRef = useRef<ImportRow[]>([])
  // Сырой `File` — только в ref: после «Изменить файл» его нужно разобрать снова,
  // а в состоянии достаточно имени и размера для подписи.
  const fileRef = useRef<File | null>(null)
  // Токен разбора: результат устаревшего вызова не применяется.
  const parseTokenRef = useRef(0)
  const mountedRef = useRef(true)
  const slotsControllerRef = useRef<AbortController | null>(null)

  useEffect(() => {
    mountedRef.current = true

    return () => {
      mountedRef.current = false
      parseTokenRef.current += 1
      fileRef.current = null
      slotsControllerRef.current?.abort()
    }
  }, [])

  const commit = useCallback((next: ImportRow[]) => {
    rowsRef.current = next

    if (mountedRef.current) {
      setRows(next)
    }
  }, [])

  /** Отменяет незавершённый разбор: его результат уже никому не нужен. */
  const invalidateParse = useCallback(() => {
    parseTokenRef.current += 1

    if (mountedRef.current) {
      setIsParsing(false)
    }
  }, [])

  const buildParseOptions = useCallback(
    (now: number): ImportParseOptions | null => {
      if (options === null) {
        return null
      }

      const primary =
        options.salons.find((salon) => salon.isPrimary) ?? options.salons[0]

      if (primary === undefined) {
        return null
      }

      return {
        services: options.services,
        salons: options.salons,
        today: todayInTimeZone(primary.timeZoneId),
        now,
        defaultSalonId: primary.salonId,
      }
    },
    [options]
  )

  const buildIcsOptions = useCallback(
    (now: number): IcsParseOptions | null => {
      if (options === null) {
        return null
      }

      const primary =
        options.salons.find((salon) => salon.isPrimary) ?? options.salons[0]

      if (primary === undefined) {
        return null
      }

      return {
        services: options.services,
        salons: options.salons,
        defaultSalonId: primary.salonId,
        now,
        periodDays,
      }
    },
    [options, periodDays]
  )

  const summary = useMemo<ImportSummary>(() => {
    const included = rows.filter((row) => row.included)

    return {
      total: rows.length,
      selected: included.length,
      ready: included.filter(
        (row) => row.status === 'ready' && isCompleteDraft(row.draft)
      ).length,
      created: rows.filter((row) => row.status === 'created').length,
      failed: rows.filter((row) => row.status === 'failed').length,
      missingService: included.filter(
        (row) =>
          row.draft.issues.includes('noService')
          || row.draft.issues.includes('ambiguousService')
      ).length,
      missingPhone: included.filter(
        (row) =>
          row.draft.issues.includes('noPhone')
          || row.draft.issues.includes('badPhone')
      ).length,
      past: included.filter((row) => row.draft.issues.includes('past')).length,
      skipped: skipped.length,
    }
  }, [rows, skipped])

  const runParse = useCallback(() => {
    invalidateParse()

    const parseOptions = buildParseOptions(Date.now())

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
    setSkipped([])
    setSelectionNotice(null)
    commit(
      result.rows.map((draft) => ({
        draft,
        idempotencyKey: newIdempotencyKey(),
        included: draft.defaultIncluded,
        status: 'ready',
        failure: null,
        overlap: false,
        slots: null,
        slotsStatus: 'idle',
      }))
    )
    setStage('preview')
  }, [buildParseOptions, commit, invalidateParse, text])

  /**
   * Применяет новый набор выбранных строк и пересчитывает пересечения.
   *
   * Статусы, слоты и причины отказа не трогаются: выбор влияет только на
   * `overlap`, потому что конфликт считается между выбранными строками.
   */
  const applyIncluded = useCallback(
    (includedLines: ReadonlySet<number>) => {
      const parseOptions = buildParseOptions(Date.now())
      const drafts = rowsRef.current.map((row) => row.draft)
      const nextDrafts = parseOptions === null
        ? drafts
        : recomputeImportRows(drafts, parseOptions, includedLines)

      commit(
        rowsRef.current.map((row, index) => ({
          ...row,
          draft: nextDrafts[index],
          included: includedLines.has(row.draft.line),
        }))
      )
      setSelectionNotice(null)
    },
    [buildParseOptions, commit]
  )

  const toggleRow = useCallback(
    (line: number) => {
      const row = rowsRef.current.find((item) => item.draft.line === line)

      if (row === undefined || row.status === 'created') {
        return
      }

      const next = new Set(
        rowsRef.current
          .filter((item) => item.included)
          .map((item) => item.draft.line)
      )

      if (!row.included) {
        if (next.size >= IMPORT_MAX_ROWS) {
          // 51-я галочка не ставится: выбор остаётся прежним.
          setSelectionNotice(SELECTION_LIMIT_TEXT)
          return
        }

        next.add(line)
      } else {
        next.delete(line)
      }

      applyIncluded(next)
    },
    [applyIncluded]
  )

  const setAllIncluded = useCallback(
    (included: boolean) => {
      const next = new Set<number>()
      let truncated = false

      if (included) {
        for (const row of rowsRef.current) {
          if (row.status === 'created') {
            continue
          }

          if (next.size >= IMPORT_MAX_ROWS) {
            // «Выбрать все» тоже упирается в предел: 51-я строка не влезает.
            truncated = true
            break
          }

          next.add(row.draft.line)
        }
      }

      applyIncluded(next)

      if (truncated) {
        setSelectionNotice(SELECTION_LIMIT_TEXT)
      }
    },
    [applyIncluded]
  )

  const setSource = useCallback(
    (next: ImportSource) => {
      invalidateParse()
      setSourceState(next)
      // Текст и файл переживают переключение, а результат разбора — нет.
      setStage('form')
      setFailure(null)
      setSkipped([])
      setSelectionNotice(null)
      setProgress(null)
      setPause(null)
      rowsRef.current = []
      setRows([])
    },
    [invalidateParse]
  )

  const setPeriod = useCallback(
    (days: ImportPeriodDays) => {
      invalidateParse()
      setPeriodDays(days)
      setStage('form')
      setFailure(null)
      setSkipped([])
      setSelectionNotice(null)
      setProgress(null)
      setPause(null)
      rowsRef.current = []
      setRows([])
    },
    [invalidateParse]
  )

  const reset = useCallback(() => {
    invalidateParse()
    setStage('form')
    setText('')
    setFile(null)
    setPeriodDays(30)
    setFailure(null)
    setSkipped([])
    setSelectionNotice(null)
    setProgress(null)
    setPause(null)
    fileRef.current = null
    rowsRef.current = []
    setRows([])
  }, [invalidateParse])

  const backToForm = useCallback(() => {
    invalidateParse()
    setStage('form')
    setFailure(null)
    setSkipped([])
    setSelectionNotice(null)
    setProgress(null)
    setPause(null)
    rowsRef.current = []
    setRows([])
  }, [invalidateParse])

  const updateRow = useCallback(
    (line: number, patch: ImportRowPatch) => {
      const parseOptions = buildParseOptions(Date.now())

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
      // Пересечения считаются по выбранным строкам: правка одной строки может
      // снять или создать конфликт у соседней, но только в пределах пачки.
      const includedLines = new Set(
        rowsRef.current
          .filter((row) => row.included)
          .map((row) => row.draft.line)
      )
      const nextDrafts = recomputeImportRows(drafts, parseOptions, includedLines)

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
    [buildParseOptions, commit]
  )

  /**
   * Выбор файла — отдельный шаг формы: барбер сначала выбирает `.ics`, потом
   * жмёт «Разобрать файл». Разбор, оставшийся от прошлого файла, отменяется, а
   * сам файл живёт в `ref` и переживает «Изменить файл» и смену периода.
   */
  const selectFile = useCallback(
    (picked: File) => {
      invalidateParse()
      fileRef.current = picked
      setFile({ name: picked.name, size: picked.size })
      setStage('form')
      setFailure(null)
      setSkipped([])
      setSelectionNotice(null)
      setProgress(null)
      setPause(null)
      rowsRef.current = []
      setRows([])
    },
    [invalidateParse]
  )

  const parseFile = useCallback(
    async () => {
      const nextFile = fileRef.current

      if (nextFile === null || options === null) {
        return
      }

      const now = Date.now()
      const icsOptions = buildIcsOptions(now)
      const parseOptions = buildParseOptions(now)

      if (icsOptions === null || parseOptions === null) {
        return
      }

      const token = parseTokenRef.current + 1
      parseTokenRef.current = token

      const isCurrent = (): boolean =>
        mountedRef.current && parseTokenRef.current === token

      setIsParsing(true)
      setStage('form')
      setFailure(null)
      setSkipped([])
      setSelectionNotice(null)
      setProgress(null)
      setPause(null)
      commit([])

      try {
        const decoded = await decodeIcsFile(nextFile)

        if (!isCurrent()) {
          return
        }

        if (decoded.failure !== null) {
          setFailure(FAILURE_TEXT[decoded.failure])
          return
        }

        if (!(await loadIcsParser())) {
          if (isCurrent()) {
            setFailure(CHUNK_FAILURE)
          }

          return
        }

        if (!isCurrent()) {
          return
        }

        const parsed = await parseIcs(decoded.text, icsOptions)

        if (!isCurrent()) {
          return
        }

        if (parsed.failure !== null) {
          setFailure(FAILURE_TEXT[parsed.failure])

          // Пустой период объясняется только списком пропущенных: иначе барбер
          // не поймёт, куда делись события.
          if (parsed.failure === 'noEventsInPeriod') {
            setSkipped(parsed.skipped)
          }

          return
        }

        const drafts = mapIcsOccurrences(parsed.occurrences, icsOptions)
        const includedLines = selectDefaultLines(drafts)
        const nextDrafts = recomputeImportRows(
          drafts,
          parseOptions,
          includedLines
        )

        commit(
          nextDrafts.map((draft) => ({
            draft,
            idempotencyKey: icsIdempotencyKey(
              draft.externalUid ?? '',
              draft.recurrenceId
            ),
            included: includedLines.has(draft.line),
            status: 'ready',
            failure: null,
            overlap: false,
            slots: null,
            slotsStatus: 'idle',
          }))
        )
        setSkipped(parsed.skipped)
        setFailure(null)
        setStage('preview')
      } catch {
        // Сюда попадают `file.arrayBuffer()`, пустой `UID` и прочие неожиданные
        // ошибки: показываем чтение, а не «файл повреждён».
        if (isCurrent()) {
          setFailure(READ_FAILURE)
        }
      } finally {
        if (isCurrent()) {
          setIsParsing(false)
        }
      }
    },
    [buildIcsOptions, buildParseOptions, commit, options]
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

        if (
          current === undefined
          || current.status === 'created'
          || !current.included
        ) {
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
          const failureMessage = createFailureText(cause, isOverlap)

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

  /**
   * Строки, которые можно отправить: выбранные, не созданные и полностью готовые.
   *
   * `included` проверяется здесь, а не в момент нажатия: пока пачка на паузе,
   * барбер может снять галочку, и повтор не должен отправить снятую строку.
   */
  const sendableLines = useCallback(
    (statuses: readonly ImportRowStatus[]) =>
      rowsRef.current
        .filter(
          (row) =>
            statuses.includes(row.status)
            && row.included
            && isCompleteDraft(row.draft)
        )
        .map((row) => row.draft.line),
    []
  )

  const createRows = useCallback(async () => {
    setStage('preview')
    await send(sendableLines(['ready']))
  }, [send, sendableLines])

  const resume = useCallback(async () => {
    await send(sendableLines(['ready', 'creating']))
  }, [send, sendableLines])

  const retryFailed = useCallback(async () => {
    // Только упавшие строки и только их прежние ключи: успешные не пересоздаются.
    const lines = sendableLines(['failed'])

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
  }, [commit, send, sendableLines])

  const retryLine = useCallback(
    async (line: number, toPreview: boolean) => {
      const row = rowsRef.current.find((item) => item.draft.line === line)

      if (
        row === undefined
        || row.status === 'created'
        || !row.included
        || !isCompleteDraft(row.draft)
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

      if (toPreview) {
        setStage('preview')
      }

      await send([line])
    },
    [commit, send]
  )

  const retryRow = useCallback(
    async (line: number) => {
      await retryLine(line, true)
    },
    [retryLine]
  )

  /**
   * Подстановка ближайшего слота: время строки меняется на слот, длительность
   * события сбрасывается — иначе она может не влезть в предложенный слот.
   *
   * Из отчёта строка повторяется на месте: возврат в предпросмотр перехватил бы
   * фокус и показал бы отчёт дважды.
   */
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
        date: salonDateFromInstant(slot.startAt, salon.timeZoneId),
        time: salonTimeFromInstant(slot.startAt, salon.timeZoneId),
        useEventTime: false,
        eventDurationMinutes: null,
      })
      await retryLine(line, stage !== 'result')
    },
    [options, retryLine, stage, updateRow]
  )

  // «Перенести ещё» — только календарь: у текста нет частичного выбора,
  // и кнопка в его отчёте лишь повторяла бы уже отправленное.
  const canContinue = useMemo(
    () => source === 'ics' && rows.some((row) => row.status !== 'created'),
    [rows, source]
  )

  /**
   * «Перенести ещё»: созданные строки снимаются с выбора, следующие до 50 готовых
   * выбираются заново. Файл не перечитывается — строки уже в памяти.
   */
  const continueImport = useCallback(() => {
    const next = new Set<number>()

    for (const row of rowsRef.current) {
      if (next.size >= IMPORT_MAX_ROWS) {
        break
      }

      if (row.status === 'ready' && isCompleteDraft(row.draft)) {
        next.add(row.draft.line)
      }
    }

    applyIncluded(next)
    setProgress(null)
    setStage('preview')
  }, [applyIncluded])

  /**
   * Возврат из отчёта в предпросмотр: «N строк требуют внимания».
   *
   * Ничего не пересчитывается и не перевыбирается — барбер видит ровно те строки
   * и те правки, с которыми пришёл к отчёту.
   */
  const reviewRows = useCallback(() => {
    setProgress(null)
    setStage('preview')
  }, [])

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
    source,
    setSource,
    file,
    periodDays,
    setPeriod,
    isParsing,
    skipped,
    selectionNotice,
    selectFile,
    parseFile,
    toggleRow,
    setAllIncluded,
    canContinue,
    continueImport,
    reviewRows,
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
