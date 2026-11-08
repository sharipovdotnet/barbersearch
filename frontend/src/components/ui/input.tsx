import * as React from "react"
import { Input as InputPrimitive } from "@base-ui/react/input"
import { cn } from "cn"

import { useI18n } from "@/i18n"

const inputClasses =
  "h-8 w-full min-w-0 rounded-lg border border-input bg-transparent px-2.5 py-1 text-base transition-colors outline-none file:inline-flex file:h-6 file:border-0 file:bg-transparent file:text-sm file:font-medium file:text-foreground placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:pointer-events-none disabled:cursor-not-allowed disabled:bg-input/50 disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 md:text-sm dark:bg-input/30 dark:disabled:bg-input/80 dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40"

/**
 * `input[type=date]` рисует подсказку «дд.мм.гггг» силами браузера и не слушает
 * `lang` страницы. Поэтому поверх пустого поля показываем свою подсказку на языке
 * интерфейса; на фокусе отдаём поле нативному контролу, чтобы не перекрывать
 * сегменты даты, которые пользователь начинает вводить.
 */
function DateInput({ className, ...props }: React.ComponentProps<"input">) {
  const { locale, t } = useI18n()
  const [isFocused, setIsFocused] = React.useState(false)
  const isEmpty = props.value === undefined || props.value === ""
  const showHint = isEmpty && !isFocused && !props.disabled

  return (
    <div className="relative w-full">
      <InputPrimitive
        {...props}
        type="date"
        lang={locale}
        data-slot="input"
        className={cn(
          inputClasses,
          showHint &&
            "[&::-webkit-datetime-edit-fields-wrapper]:text-transparent [&::-webkit-datetime-edit]:text-transparent",
          className
        )}
        onFocus={(event) => {
          setIsFocused(true)
          props.onFocus?.(event)
        }}
        onBlur={(event) => {
          setIsFocused(false)
          props.onBlur?.(event)
        }}
      />
      {showHint && (
        <span
          aria-hidden
          className="pointer-events-none absolute inset-y-0 left-2.5 flex items-center text-base text-muted-foreground md:text-sm"
        >
          {t("дд.мм.гггг")}
        </span>
      )}
    </div>
  )
}

function Input({ className, type, ...props }: React.ComponentProps<"input">) {
  if (type === "date") {
    return <DateInput className={className} {...props} />
  }

  return (
    <InputPrimitive
      type={type}
      data-slot="input"
      className={cn(inputClasses, className)}
      {...props}
    />
  )
}

export { Input }
