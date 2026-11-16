import {
  Combobox as ComboboxPrimitive,
  type ComboboxRootChangeEventDetails,
} from '@base-ui/react/combobox'
import { useState } from 'react'

import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useAddressSuggestions } from '@/hooks/use-address-suggestions'
import { useI18n } from '@/i18n'
import { cn } from '@/lib/utils'

export type SuggestionComboboxProps<T> = {
  /** id поля и основа для id ошибки и подсказки: на него опираются тесты формы. */
  id: string
  label: string
  value: string
  /** Ручной ввод: каждая правка сбрасывает выбор, сделанный подсказкой. */
  onValueChange: (value: string) => void
  /** Явный выбор из списка: клик, Enter или Space. */
  onSelect: (suggestion: T) => void
  getKey: (suggestion: T) => string
  getLabel: (suggestion: T) => string
  fetchSuggestions: (query: string, signal: AbortSignal) => Promise<T[]>
  minLength: number
  /** `aria-label` выпадающего списка: у него нет видимой подписи. */
  listLabel: string
  placeholder?: string
  /** Подсказка под полем; её id входит в `aria-describedby`. */
  hint?: string
  disabled?: boolean
  error?: string
  describedBy?: string
  autoFocus?: boolean
  /** Пока `false`, в сеть не ходим: например, адрес без выбранного города. */
  enabled?: boolean
  className?: string
}

/**
 * Поле с серверными подсказками на Base UI Combobox.
 *
 * Примитив не фильтрует выдачу повторно (`filter={null}`): поиск идёт на сервере, и
 * клиентская фильтрация только спрятала бы часть ответа. Поле остаётся обычным
 * `<input>` с ролью combobox — существующие `getByLabel('Город')` и `.fill(...)`
 * продолжают работать.
 *
 * Недоступность подсказок — тихая: список просто не открывается, а поле остаётся
 * редактируемым. Пустой ответ — не ошибка, а «Ничего не найдено.» внутри списка.
 */
export function SuggestionCombobox<T>({
  id,
  label,
  value,
  onValueChange,
  onSelect,
  getKey,
  getLabel,
  fetchSuggestions,
  minLength,
  listLabel,
  placeholder,
  hint,
  disabled,
  error,
  describedBy,
  autoFocus,
  enabled = true,
  className,
}: SuggestionComboboxProps<T>) {
  const { t } = useI18n()
  const { items, status } = useAddressSuggestions(
    enabled ? value : '',
    minLength,
    fetchSuggestions
  )
  const [isOpen, setIsOpen] = useState(false)

  const errorId = `${id}-error`
  const hintId = `${id}-hint`
  const describedByIds = [
    error ? errorId : null,
    hint ? hintId : null,
    describedBy,
  ]
    .filter(Boolean)
    .join(' ')
  // Скрытое состояние — тихий отказ подсказок или слишком короткая строка. Popup тогда
  // не открывается, но поле продолжает принимать текст.
  const isSuppressed = status === 'idle' || status === 'hidden'

  function handleInputValueChange(
    next: string,
    details: ComboboxRootChangeEventDetails
  ) {
    // Примитив зовёт этот обработчик и тогда, когда сам сбрасывает текст: при закрытии
    // списка, выборе пункта, потере фокуса или после очистки. Настоящий ввод приходит
    // только с причиной `input-change`, иначе Esc, Tab или подсказка стирали бы набранное.
    if (details.reason !== 'input-change') {
      return
    }

    onValueChange(next)
  }

  function handleValueChange(
    next: T | null,
    details: ComboboxRootChangeEventDetails
  ) {
    if (next === null || details.reason !== 'item-press') {
      return
    }

    onSelect(next)
  }

  return (
    <Field className={className} data-invalid={Boolean(error)}>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      <ComboboxPrimitive.Root
        items={items}
        filter={null}
        inputValue={value}
        onInputValueChange={handleInputValueChange}
        onValueChange={handleValueChange}
        itemToStringLabel={getLabel}
        isItemEqualToValue={(item, other) => getKey(item) === getKey(other)}
        open={isSuppressed ? false : isOpen}
        onOpenChange={setIsOpen}
      >
        <ComboboxPrimitive.Input
          id={id}
          render={<Input />}
          placeholder={placeholder}
          disabled={disabled}
          autoFocus={autoFocus}
          autoComplete="off"
          spellCheck={false}
          aria-invalid={Boolean(error)}
          aria-describedby={describedByIds === '' ? undefined : describedByIds}
          onBlur={() => setIsOpen(false)}
        />
        <ComboboxPrimitive.Portal>
          <ComboboxPrimitive.Positioner sideOffset={4} className="z-50">
            <ComboboxPrimitive.Popup className="max-h-64 w-(--anchor-width) overflow-y-auto rounded-lg border bg-popover p-1 text-popover-foreground shadow-md">
              <ComboboxPrimitive.List aria-label={listLabel}>
                {(item: T) => (
                  <ComboboxPrimitive.Item
                    key={getKey(item)}
                    value={item}
                    className={cn(
                      'cursor-default rounded-md px-2 py-1.5 text-sm select-none',
                      'data-highlighted:bg-accent data-highlighted:text-accent-foreground'
                    )}
                  >
                    {getLabel(item)}
                  </ComboboxPrimitive.Item>
                )}
              </ComboboxPrimitive.List>
              {status === 'empty' && (
                <p
                  role="status"
                  className="px-2 py-1.5 text-sm text-muted-foreground"
                >
                  {t('Ничего не найдено.')}
                </p>
              )}
            </ComboboxPrimitive.Popup>
          </ComboboxPrimitive.Positioner>
        </ComboboxPrimitive.Portal>
      </ComboboxPrimitive.Root>
      {hint && <FieldDescription id={hintId}>{hint}</FieldDescription>}
      {status === 'loading' && (
        <span role="status" className="sr-only">
          {t('Загрузка подсказок…')}
        </span>
      )}
      <FieldError id={errorId}>{error}</FieldError>
    </Field>
  )
}
