import { useEffect, useRef, useState, type DragEvent, type MouseEvent } from 'react'
import { FileText, Upload } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import type {
  ImportFileInfo,
  ImportPeriodDays,
} from '@/hooks/use-barber-import'
import { t } from '@/i18n'
import { formatDaysCount, formatNumber } from '@/lib/format'
import { isPlainClick, navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * Шаг 2 для календаря: файл `.ics` и период.
 *
 * Файл читается в браузере: до кнопки «Разобрать файл» ни байта не уходит в сеть.
 * Поэтому здесь только выбор файла и период — разбор начинается по действию
 * барбера, а ошибка чтения возвращает фокус на input, не оставляя его в форме.
 */

const FILE_ID = 'import-ics-file'
const FILE_HINT_ID = 'import-ics-file-hint'
const FILE_ERROR_ID = 'import-ics-file-error'
const DROP_ERROR_ID = 'import-ics-drop-error'
const PERIOD_ID = 'import-ics-period'

const PERIODS: readonly ImportPeriodDays[] = [7, 30, 90]

/** Размер файла без внешних библиотек: «248 КБ», «2,0 МБ». */
function formatFileSize(bytes: number): string {
  if (bytes < 1024) {
    return t('{size} Б', { size: bytes })
  }

  const kilobytes = bytes / 1024

  if (kilobytes < 1024) {
    return t('{size} КБ', { size: Math.round(kilobytes) })
  }

  return t('{size} МБ', { size: formatNumber(kilobytes / 1024, 1) })
}

function periodFromValue(value: string): ImportPeriodDays {
  if (value === '7') {
    return 7
  }

  return value === '90' ? 90 : 30
}

export function IcsImportForm({
  file,
  periodDays,
  isParsing,
  failure,
  onFile,
  onPeriod,
  onParse,
  onClear,
}: {
  file: ImportFileInfo | null
  periodDays: ImportPeriodDays
  isParsing: boolean
  /** Уже переведённая на русский ошибка чтения или разбора. */
  failure: string | null
  onFile: (file: File) => void
  onPeriod: (days: ImportPeriodDays) => void
  onParse: () => void
  onClear: () => void
}) {
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [dropError, setDropError] = useState<string | null>(null)
  const [isDragging, setIsDragging] = useState(false)

  const hasError = failure !== null || dropError !== null
  const describedBy = `${FILE_HINT_ID}${dropError === null ? '' : ` ${DROP_ERROR_ID}`}${failure === null ? '' : ` ${FILE_ERROR_ID}`}`

  useEffect(() => {
    // Ошибка чтения относится к файлу: фокус возвращается туда, где его чинят.
    if (failure !== null) {
      fileInputRef.current?.focus({ preventScroll: true })
    }
  }, [failure])

  function selectFile(picked: File) {
    setDropError(null)
    onFile(picked)
  }

  function handleDrop(event: DragEvent<HTMLLabelElement>) {
    event.preventDefault()
    setIsDragging(false)

    const files = Array.from(event.dataTransfer.files)

    if (files.length === 0) {
      return
    }

    if (files.length > 1) {
      setDropError(t('Один файл за раз.'))
      return
    }

    selectFile(files[0])
  }

  function handleDragLeave(event: DragEvent<HTMLLabelElement>) {
    const next = event.relatedTarget

    if (next instanceof Node && event.currentTarget.contains(next)) {
      return
    }

    setIsDragging(false)
  }

  return (
    <form
      noValidate
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault()
        onParse()
      }}
    >
      <Field data-invalid={hasError ? true : undefined}>
        <label
          htmlFor={FILE_ID}
          onDragOver={(event) => {
            event.preventDefault()
            setIsDragging(true)
          }}
          onDragLeave={handleDragLeave}
          onDrop={handleDrop}
          className={cn(
            'flex min-h-28 cursor-pointer flex-col items-center justify-center gap-1.5 rounded-xl border border-dashed bg-muted/20 px-4 py-6 text-center transition-colors focus-within:border-ring focus-within:ring-3 focus-within:ring-ring/50 hover:bg-muted/40',
            isDragging && 'border-ring bg-muted/50',
            hasError && 'border-destructive/60'
          )}
        >
          <Upload className="size-5 text-muted-foreground" aria-hidden />
          <span className="text-sm font-medium">
            {t('Перетащите .ics сюда или выберите файл')}
          </span>
          <span id={FILE_HINT_ID} className="text-sm text-muted-foreground">
            {t('Один календарь, до 2 МБ.')}
          </span>
          <input
            ref={fileInputRef}
            id={FILE_ID}
            type="file"
            accept=".ics,text/calendar,application/ics"
            aria-invalid={hasError}
            aria-describedby={describedBy}
            // `display:none` сделал бы input недостижимым с клавиатуры, поэтому
            // только `sr-only`: Tab доводит до него, Space открывает выбор.
            className="sr-only"
            onChange={(event) => {
              const picked = event.target.files?.[0]

              if (picked !== undefined) {
                selectFile(picked)
              }

              // Сброс значения: тот же файл можно выбрать повторно.
              event.target.value = ''
            }}
          />
        </label>

        <FieldError id={DROP_ERROR_ID}>{dropError}</FieldError>
        <FieldError id={FILE_ERROR_ID}>{failure}</FieldError>
      </Field>

      {file !== null && (
        <p className="flex flex-wrap items-center gap-x-1.5 text-sm">
          <FileText className="size-4 text-muted-foreground" aria-hidden />
          <span className="font-medium break-all">{file.name}</span>
          <span className="text-muted-foreground">
            · {formatFileSize(file.size)}
          </span>
        </p>
      )}

      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <Field className="sm:w-40">
          <FieldLabel htmlFor={PERIOD_ID}>{t('Период')}</FieldLabel>
          <select
            id={PERIOD_ID}
            value={periodDays}
            className="h-11 w-full rounded-md border border-input bg-transparent px-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-8"
            onChange={(event) => {
              onPeriod(periodFromValue(event.target.value))
            }}
          >
            {PERIODS.map((period) => (
              <option key={period} value={period}>
                {formatDaysCount(period)}
              </option>
            ))}
          </select>
        </Field>

        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="submit"
            size="sm"
            className="h-11 sm:h-8"
            aria-busy={isParsing}
            disabled={isParsing || file === null}
          >
            {isParsing ? t('Разбираем…') : t('Разобрать файл')}
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            disabled={file === null && dropError === null}
            onClick={() => {
              setDropError(null)
              setIsDragging(false)

              if (fileInputRef.current !== null) {
                fileInputRef.current.value = ''
              }

              onClear()
            }}
          >
            {t('Очистить')}
          </Button>
        </div>
      </div>

      {isParsing && (
        <p role="status" className="text-sm text-muted-foreground">
          {t('Разбираем…')}
        </p>
      )}

      <p className="text-sm text-muted-foreground">
        {t('Файл читается в браузере и не загружается на сервер.')}{' '}
        <a
          href="/privacy"
          className="underline underline-offset-2 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
          onClick={(event: MouseEvent<HTMLAnchorElement>) => {
            if (isPlainClick(event)) {
              event.preventDefault()
              navigate('/privacy')
            }
          }}
        >
          {t('Политика обработки данных')}
        </a>
        .
      </p>

      <details className="rounded-xl border px-3 py-2 text-sm">
        <summary className="flex min-h-11 cursor-pointer items-center rounded-sm font-medium outline-none focus-visible:ring-3 focus-visible:ring-ring/50 sm:min-h-0 sm:py-1">
          {t('Как выгрузить календарь')}
        </summary>
        <ul className="mt-2 space-y-1 text-muted-foreground">
          <li>
            <span className="font-medium text-foreground">Google</span> —{' '}
            {t('«Настройки → Импорт и экспорт → Экспорт»')}
          </li>
          <li>
            <span className="font-medium text-foreground">Apple</span> —{' '}
            {t('«Файл → Экспорт»')}
          </li>
          <li>
            <span className="font-medium text-foreground">Outlook</span> —{' '}
            {t('«Настройки → Календарь → Экспорт»')}
          </li>
        </ul>
      </details>
    </form>
  )
}
