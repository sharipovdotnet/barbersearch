import { ChevronDown, Loader2, Search } from 'lucide-react'
import { useState, type FormEvent, type ReactNode } from 'react'

import { SuggestionCombobox } from '@/components/suggestion-combobox'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useSearchCatalogs } from '@/hooks/use-search-catalogs'
import { useI18n } from '@/i18n'
import {
  CITY_SUGGESTIONS_MIN_LENGTH,
  fetchCitySuggestions,
  type CitySuggestion,
} from '@/lib/address-suggestions'
import type { BarberSearchFilters } from '@/lib/barber-search'
import {
  LANGUAGE_LABELS,
  type Language,
} from '@/lib/barber-profile-labels'
import { scrollToSection } from '@/lib/router'
import { cn } from '@/lib/utils'

type SearchFormProps = {
  isSearching: boolean
  onSearch: (filters: BarberSearchFilters) => void
}

/**
 * Высота всех контролов панели: 44 px на тач-экране, 40 px на десктопе.
 * Одно значение на поле и кнопку — иначе строка фильтров «пляшет» по вертикали.
 */
const CONTROL_HEIGHT = 'h-11 sm:h-10'

/**
 * Классы нативных селектов: та же высота и рамка, что у `Input`, плюс место
 * справа под свою иконку — системная стрелка выглядит по-разному в браузерах
 * и выбивается из общего ряда контролов панели.
 */
const SELECT_CLASSES = cn(
  'w-full min-w-0 appearance-none rounded-lg border border-input bg-transparent py-1 pr-8 pl-2.5 text-base transition-colors outline-none md:text-sm',
  'focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50',
  'disabled:cursor-not-allowed disabled:bg-input/50 disabled:opacity-50',
  CONTROL_HEIGHT
)

/** Подпись поля: мелкая и приглушённая, чтобы главным в поле осталось значение. */
const FIELD_LABEL_CLASSES =
  'text-xs leading-none font-medium text-muted-foreground'

const LANGUAGE_VALUES = Object.keys(LANGUAGE_LABELS) as Language[]

const ADDITIONAL_SERVICE_LIST_ID = 'search-additional-services'

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

/** Сегодняшняя дата в локальном поясе: `toISOString` дал бы UTC и мог сдвинуть день. */
function todayLocal(): string {
  const now = new Date()

  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/**
 * Обёртка поля панели: подпись сверху, контрол снизу.
 *
 * `className` несёт позицию в общей сетке, поэтому `col-span`/`row-start`
 * заданы только здесь — раскладка остаётся в одном месте, а сами поля
 * (включая поле с подсказками) не знают, в каком столбце они стоят.
 */
function FieldShell({
  className,
  children,
}: {
  className?: string
  children: ReactNode
}) {
  return (
    <div className={cn('flex min-w-0 flex-col gap-1.5', className)}>
      {children}
    </div>
  )
}

function FieldLabel({
  htmlFor,
  children,
}: {
  htmlFor: string
  children: ReactNode
}) {
  return (
    <Label htmlFor={htmlFor} className={FIELD_LABEL_CLASSES}>
      {children}
    </Label>
  )
}

type SelectFieldProps = {
  id: string
  name: string
  label: string
  value: string
  /** Пункт «без фильтра»: «Любой язык», «Загрузка…» и т. п. */
  emptyLabel: string
  options: readonly { id: string; name: string }[]
  disabled?: boolean
  className?: string
  onChange: (value: string) => void
}

function SelectField({
  id,
  name,
  label,
  value,
  emptyLabel,
  options,
  disabled,
  className,
  onChange,
}: SelectFieldProps) {
  // Нативное поле не умеет многоточие: длинное название обрежется краем. `title`
  // даёт полное значение при наведении — дешёвая страховка, а не замена списку;
  // у пустого фильтра подсказка не нужна, там и так «Любой язык».
  const selectedLabel =
    options.find((option) => option.id === value)?.name ?? emptyLabel

  return (
    <FieldShell className={className}>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      <div className="relative">
        <select
          id={id}
          name={name}
          value={value}
          disabled={disabled}
          title={value === '' ? undefined : selectedLabel}
          className={SELECT_CLASSES}
          onChange={(event) => onChange(event.target.value)}
        >
          <option value="">{emptyLabel}</option>
          {options.map((option) => (
            <option key={option.id} value={option.id}>
              {option.name}
            </option>
          ))}
        </select>
        <ChevronDown
          aria-hidden
          className="pointer-events-none absolute top-1/2 right-2.5 size-4 -translate-y-1/2 text-muted-foreground"
        />
      </div>
    </FieldShell>
  )
}

/**
 * Форма поиска: город, дата, язык, специализация, услуга и дополнительная услуга.
 *
 * Все запросы уходят только по submit — изменение поля ничего не перезапрашивает.
 * Справочники грузятся отдельно: их ошибка не блокирует город, дату и язык.
 * Пустой справочник скрывает свой селект, чтобы не висел мёртвый список.
 *
 * Раскладка — одна панель на всю ширину витрины (`max-w-5xl`, как у сетки
 * результатов). На `lg` это сетка из 12 колонок и двух ярусов:
 * - первый — где, когда и на каком языке: город (4), дата (3), язык (2) и
 *   кнопка (3), прижатая к правому краю и стоящая на одной линии с полями;
 * - второй — что именно: специализация, услуга и доп. услуга, по 4 колонки.
 *   Услуга и доп. услуга делят строку поровну, поэтому длинное название
 *   каталога и свободный ввод не упираются в край поля.
 * Кнопка — последняя в разметке: на телефоне и планшете она уходит под все
 * фильтры, а в первую строку встаёт только на `lg` (там у неё явные
 * `col-start`/`row-start`), поэтому на узком экране её не приходится искать
 * посреди панели.
 * Подписи видимые: значение «Любой язык» без подписи читается как выбранный
 * фильтр, а пустая дата — как незаполненное обязательное поле.
 *
 * Город — поле с серверными подсказками DaData (`GET /api/addresses/cities`).
 * Выбор пункта подставляет голое имя города, а не подпись из списка: сервер
 * сравнивает `salon.City` точно, поэтому «Екатеринбург» из подсказки находит
 * салоны, а набранное «Екат» — нет. Подсказки — прогрессивное улучшение:
 * эндпоинт требует входа, и у гостя список просто не открывается, а поле
 * остаётся обычным свободным вводом (401 там тихий, как и 409).
 *
 * Дополнительная услуга — свободный текст с подсказками `<datalist>`: значения
 * приходят из карточек барберов, но выбрать можно и частичное совпадение
 * («бород» найдёт «Оформление бороды»). Подсказки — не зависимость поиска:
 * без них поле остаётся рабочим.
 */
export function SearchForm({ isSearching, onSearch }: SearchFormProps) {
  const { t } = useI18n()
  const [city, setCity] = useState('')
  const [date, setDate] = useState('')
  const [language, setLanguage] = useState<Language | ''>('')
  const [specializationId, setSpecializationId] = useState('')
  const [hairServiceId, setHairServiceId] = useState('')
  const [additionalService, setAdditionalService] = useState('')

  const catalogs = useSearchCatalogs()
  const isLoading = catalogs.status === 'loading'
  const isReady = catalogs.status === 'ready'

  // Пока справочник не загружен, селект остаётся на месте: лейаут не прыгает.
  const showSpecializations =
    !isReady || catalogs.specializations.length > 0
  const showHairServices = !isReady || catalogs.hairServices.length > 0

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    scrollToSection('barbers')
    onSearch({
      city,
      date,
      specializationId,
      hairServiceId,
      language,
      additionalService,
    })
  }

  return (
    <form
      role="search"
      aria-label={t('Поиск барбера')}
      className="mt-8 w-full rounded-2xl bg-card p-4 ring-1 ring-foreground/10 sm:p-5"
      onSubmit={handleSubmit}
    >
      {/* `gap-y-3`: подпись второго яруса сама отделяет его от полей первого,
          поэтому 20 px между ярусами читались как пустой ряд. */}
      <div className="grid grid-cols-1 gap-x-3 gap-y-3 sm:grid-cols-2 lg:grid-cols-12">
        {/* Первый ярус: город, дата и язык — то, что клиент выбирает первым. */}
        <SuggestionCombobox<CitySuggestion>
          id="search-city"
          label={t('Город')}
          labelClassName={FIELD_LABEL_CLASSES}
          className="gap-1.5 sm:col-span-2 lg:col-span-4 lg:col-start-1 lg:row-start-1"
          value={city}
          // Ручной ввод оставляем как есть: свободный город без подсказки — норма.
          onValueChange={setCity}
          // Город пишем по `city`, а не по подписи: подпись — «г. Екатеринбург».
          onSelect={(suggestion) => setCity(suggestion.city)}
          getKey={(suggestion) => suggestion.providerId ?? suggestion.display}
          getLabel={(suggestion) => suggestion.display}
          fetchSuggestions={fetchCitySuggestions}
          minLength={CITY_SUGGESTIONS_MIN_LENGTH}
          listLabel={t('Подсказки города')}
          placeholder={t('Москва')}
          inputClassName={CONTROL_HEIGHT}
        />

        <FieldShell className="lg:col-span-3 lg:col-start-5 lg:row-start-1">
          <FieldLabel htmlFor="search-date">{t('Дата')}</FieldLabel>
          <Input
            id="search-date"
            name="date"
            type="date"
            min={todayLocal()}
            value={date}
            // Системная иконка календаря — единственный чёрный глиф в панели;
            // приглушаем её до веса стрелок у селектов.
            className={cn(
              CONTROL_HEIGHT,
              '[&::-webkit-calendar-picker-indicator]:opacity-60'
            )}
            onChange={(event) => setDate(event.target.value)}
          />
        </FieldShell>

        <SelectField
          id="search-language"
          name="language"
          label={t('Язык')}
          value={language}
          emptyLabel={t('Любой язык')}
          options={LANGUAGE_VALUES.map((value) => ({
            id: value,
            name: LANGUAGE_LABELS[value],
          }))}
          className="lg:col-span-2 lg:col-start-8 lg:row-start-1"
          onChange={(value) => setLanguage(value as Language | '')}
        />

        {/* Второй ярус: что именно ищет клиент. Три равных поля: у услуги и
            доп. услуги длинные названия и свободный ввод, им нужна ширина.
            Точных столбцов нет — поля занимают строку слева направо, поэтому
            скрытый пустой справочник не оставляет дыру в середине. */}
        {showSpecializations && (
          <SelectField
            id="search-specialization"
            name="specializationId"
            label={t('Специализация')}
            value={specializationId}
            disabled={!isReady}
            emptyLabel={
              isLoading ? t('Загрузка…') : t('Любая специализация')
            }
            options={catalogs.specializations}
            className="lg:col-span-4 lg:row-start-2"
            onChange={setSpecializationId}
          />
        )}

        {showHairServices && (
          <SelectField
            id="search-hair-service"
            name="hairServiceId"
            label={t('Услуга')}
            value={hairServiceId}
            disabled={!isReady}
            emptyLabel={isLoading ? t('Загрузка…') : t('Любая услуга')}
            options={catalogs.hairServices}
            className="lg:col-span-4 lg:row-start-2"
            onChange={setHairServiceId}
          />
        )}

        <FieldShell className="sm:col-span-2 lg:col-span-4 lg:row-start-2">
          <FieldLabel htmlFor="search-additional-service">
            {t('Дополнительная услуга')}
          </FieldLabel>
          <Input
            id="search-additional-service"
            name="additionalService"
            type="search"
            list={ADDITIONAL_SERVICE_LIST_ID}
            value={additionalService}
            placeholder={t('Например, борода')}
            autoComplete="off"
            className={CONTROL_HEIGHT}
            onChange={(event) => setAdditionalService(event.target.value)}
          />
          {catalogs.additionalServices.length > 0 && (
            <datalist id={ADDITIONAL_SERVICE_LIST_ID}>
              {catalogs.additionalServices.map((service) => (
                <option key={service} value={service} />
              ))}
            </datalist>
          )}
        </FieldShell>

        {/* Кнопка — последняя в разметке: на телефоне и планшете она стоит под
            всеми фильтрами, а на `lg` явные `col-start`/`row-start` ставят её в
            первую строку, на одну линию с полями. */}
        <div className="flex min-w-0 flex-col justify-end sm:col-span-2 lg:col-span-3 lg:col-start-10 lg:row-start-1">
          <Button
            type="submit"
            size="lg"
            className={cn('w-full px-5', CONTROL_HEIGHT)}
            aria-busy={isSearching}
          >
            {isSearching ? (
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
            ) : (
              <Search data-icon="inline-start" aria-hidden />
            )}
            {t('Найти')}
          </Button>
        </div>
      </div>

      {catalogs.status === 'error' && (
        <div
          role="alert"
          className="mt-4 flex flex-col gap-2 rounded-lg border border-destructive/30 p-3 sm:flex-row sm:items-center sm:justify-between"
        >
          <p className="text-sm text-muted-foreground">
            {catalogs.error ?? t('Не удалось загрузить фильтры.')}
          </p>
          <Button
            type="button"
            variant="outline"
            className="h-11 w-full sm:h-9 sm:w-auto"
            onClick={catalogs.retry}
          >
            {t('Повторить')}
          </Button>
        </div>
      )}
    </form>
  )
}
