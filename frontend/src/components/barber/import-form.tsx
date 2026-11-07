import { useState, type MouseEvent } from 'react'

import { Button } from '@/components/ui/button'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Textarea } from '@/components/ui/textarea'
import { IMPORT_MAX_TEXT_LENGTH } from '@/lib/import-parse'
import { isPlainClick, navigate } from '@/lib/router'

/**
 * Шаг 1: вставка списка.
 *
 * Поле — главный элемент экрана, поэтому подсказка и пример рядом с ним, а не
 * в модалке. Вставленный текст живёт только в памяти этой страницы: ни
 * `localStorage`, ни запросов до кнопки «Разобрать».
 */

const PLACEHOLDER = '12 ноября\n10:00 Иван Петров +7 999 123-45-67 стрижка'

/** Пример из реального журнала: дата-заголовок, имя, телефон и услуга. */
const EXAMPLE = `12 ноября
10:00 Иван Петров +7 999 123-45-67 стрижка
11:30 Аня, окрашивание, +79990001122
14:00 Пётр | борода`

const TEXT_ID = 'import-text'
const HINT_ID = 'import-text-hint'
const ERROR_ID = 'import-text-error'
const EXAMPLE_ID = 'import-example'

export function ImportForm({
  text,
  failure,
  onChange,
  onParse,
  onClear,
}: {
  text: string
  /** Ошибка формы: пусто, больше строк, ничего не понято. */
  failure: string | null
  onChange: (value: string) => void
  onParse: () => void
  onClear: () => void
}) {
  const [showExample, setShowExample] = useState(false)

  const describedBy =
    failure === null ? HINT_ID : `${HINT_ID} ${ERROR_ID}`

  return (
    <form
      noValidate
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault()
        onParse()
      }}
    >
      <Field data-invalid={failure === null ? undefined : true}>
        <FieldLabel htmlFor={TEXT_ID}>Список записей</FieldLabel>
        <Textarea
          id={TEXT_ID}
          name="import-text"
          value={text}
          rows={8}
          placeholder={PLACEHOLDER}
          spellCheck={false}
          autoComplete="off"
          maxLength={IMPORT_MAX_TEXT_LENGTH}
          aria-invalid={failure !== null}
          aria-describedby={describedBy}
          className="min-h-44 font-mono text-sm"
          onChange={(event) => {
            onChange(event.target.value)
          }}
        />
        <FieldError id={ERROR_ID}>{failure}</FieldError>
        <p id={HINT_ID} className="text-sm text-muted-foreground">
          До 50 строк и 10 000 символов.
        </p>
      </Field>

      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="h-11 sm:h-8"
          aria-expanded={showExample}
          aria-controls={EXAMPLE_ID}
          onClick={() => {
            setShowExample((previous) => !previous)
          }}
        >
          {showExample ? 'Скрыть пример' : 'Показать пример'}
        </Button>

        <div className="ml-auto flex flex-wrap items-center gap-2">
          <Button
            type="submit"
            size="sm"
            className="h-11 sm:h-8"
            disabled={text.trim() === ''}
          >
            Разобрать
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            disabled={text === ''}
            onClick={onClear}
          >
            Очистить
          </Button>
        </div>
      </div>

      {showExample && (
        <div
          id={EXAMPLE_ID}
          className="rounded-lg border bg-muted/40 px-3 py-2 text-sm"
        >
          <p className="mb-2 text-muted-foreground">
            Строка с датой задаёт дату следующим — документ можно вставлять как
            есть.
          </p>
          <pre className="font-mono text-xs whitespace-pre-wrap text-foreground">
            {EXAMPLE}
          </pre>
        </div>
      )}

      <p className="text-sm text-muted-foreground">
        Данные клиентов сохранятся в сервисе и будут видны только вам. Не
        вставляйте лишнее: email, адреса, заметки.{' '}
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
          Политика обработки данных
        </a>
        .
      </p>
    </form>
  )
}
