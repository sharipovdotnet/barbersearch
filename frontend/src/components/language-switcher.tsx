import { LOCALES, type Locale, useI18n } from '@/i18n'
import { cn } from '@/lib/utils'

/**
 * Самоназвания языков: носитель узнаёт свой язык без перевода, поэтому эти подписи
 * не проходят через каталог и всегда остаются на своём языке.
 */
const LOCALE_LABELS: Record<Locale, string> = {
  ru: 'Русский',
  en: 'English',
}

/**
 * Компактный переключатель RU/EN в шапке.
 *
 * Две кнопки вместо селекта: на 320 px селект с раскрытым списком перекрывает
 * полстраницы, а пара кнопок помещается рядом с иконкой аккаунта. Текущий язык
 * помечен `aria-pressed`; `lang` на каждой кнопке говорит скринридеру, как читать
 * её название.
 */
export function LanguageSwitcher({ className }: { className?: string }) {
  const { locale, setLocale, t } = useI18n()

  return (
    <div
      role="group"
      aria-label={t('Язык интерфейса')}
      className={cn(
        'flex shrink-0 items-center rounded-md border p-0.5',
        className
      )}
    >
      {LOCALES.map((value) => (
        <button
          key={value}
          type="button"
          lang={value}
          aria-label={`${value.toUpperCase()} — ${LOCALE_LABELS[value]}`}
          aria-pressed={locale === value}
          title={LOCALE_LABELS[value]}
          onClick={() => setLocale(value)}
          className={cn(
            'h-7 min-w-8 rounded-sm px-1.5 text-xs font-medium transition-colors focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none max-sm:h-9 max-sm:min-w-9',
            locale === value
              ? 'bg-muted text-foreground'
              : 'text-muted-foreground hover:text-foreground'
          )}
        >
          {value.toUpperCase()}
        </button>
      ))}
    </div>
  )
}
