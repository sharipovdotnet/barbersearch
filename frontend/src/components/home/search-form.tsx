import { Loader2, Search } from 'lucide-react'
import { useState, type FormEvent } from 'react'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useSearchCatalogs } from '@/hooks/use-search-catalogs'
import type { BarberSearchFilters } from '@/lib/barber-search'
import {
  LANGUAGE_LABELS,
  type Language,
} from '@/lib/barber-profile-labels'
import { scrollToSection } from '@/lib/router'

type SearchFormProps = {
  isSearching: boolean
  onSearch: (filters: BarberSearchFilters) => void
}

/** Классы нативных селектов: одна высота с `Input`, без своих `ui/select` в проекте. */
const SELECT_CLASSES =
  'h-10 w-full min-w-0 rounded-lg border border-input bg-transparent px-2.5 text-base transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 md:text-sm'

const LANGUAGE_VALUES = Object.keys(LANGUAGE_LABELS) as Language[]

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

/** Сегодняшняя дата в локальном поясе: `toISOString` дал бы UTC и мог сдвинуть день. */
function todayLocal(): string {
  const now = new Date()

  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/**
 * Форма поиска: город, дата, язык, специализация и услуга.
 *
 * Все запросы уходят только по submit — изменение поля ничего не перезапрашивает.
 * Справочники грузятся отдельно: их ошибка не блокирует город, дату и язык.
 * Пустой справочник скрывает свой селект, чтобы не висел мёртвый список.
 */
export function SearchForm({ isSearching, onSearch }: SearchFormProps) {
  const [city, setCity] = useState('')
  const [date, setDate] = useState('')
  const [language, setLanguage] = useState<Language | ''>('')
  const [specializationId, setSpecializationId] = useState('')
  const [hairServiceId, setHairServiceId] = useState('')

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
    onSearch({ city, date, specializationId, hairServiceId, language })
  }

  return (
    <form
      className="mt-8 grid w-full max-w-3xl gap-2 sm:grid-cols-2 lg:flex lg:flex-wrap"
      onSubmit={handleSubmit}
    >
      <div className="min-w-0 sm:col-span-2 lg:min-w-56 lg:flex-[2]">
        <Label htmlFor="search-city" className="sr-only">
          Город салона
        </Label>
        <Input
          id="search-city"
          name="city"
          value={city}
          placeholder="Москва"
          autoComplete="address-level2"
          className="h-10"
          onChange={(event) => setCity(event.target.value)}
        />
      </div>

      <div className="min-w-0 lg:min-w-40 lg:flex-[1.2]">
        <Label htmlFor="search-date" className="sr-only">
          Дата
        </Label>
        <Input
          id="search-date"
          name="date"
          type="date"
          min={todayLocal()}
          value={date}
          className="h-10"
          onChange={(event) => setDate(event.target.value)}
        />
      </div>

      <div className="min-w-0 lg:min-w-40 lg:flex-[1.2]">
        <Label htmlFor="search-language" className="sr-only">
          Язык
        </Label>
        <select
          id="search-language"
          name="language"
          value={language}
          className={SELECT_CLASSES}
          onChange={(event) => setLanguage(event.target.value as Language | '')}
        >
          <option value="">Любой язык</option>
          {LANGUAGE_VALUES.map((value) => (
            <option key={value} value={value}>
              {LANGUAGE_LABELS[value]}
            </option>
          ))}
        </select>
      </div>

      {showSpecializations && (
        <div className="min-w-0 lg:min-w-44 lg:flex-[1.4]">
          <Label htmlFor="search-specialization" className="sr-only">
            Специализация
          </Label>
          <select
            id="search-specialization"
            name="specializationId"
            value={specializationId}
            disabled={!isReady}
            className={SELECT_CLASSES}
            onChange={(event) => setSpecializationId(event.target.value)}
          >
            <option value="">
              {isLoading ? 'Загрузка…' : 'Любая специализация'}
            </option>
            {catalogs.specializations.map((specialization) => (
              <option key={specialization.id} value={specialization.id}>
                {specialization.name}
              </option>
            ))}
          </select>
        </div>
      )}

      {showHairServices && (
        <div className="min-w-0 lg:min-w-44 lg:flex-[1.4]">
          <Label htmlFor="search-hair-service" className="sr-only">
            Услуга
          </Label>
          <select
            id="search-hair-service"
            name="hairServiceId"
            value={hairServiceId}
            disabled={!isReady}
            className={SELECT_CLASSES}
            onChange={(event) => setHairServiceId(event.target.value)}
          >
            <option value="">{isLoading ? 'Загрузка…' : 'Любая услуга'}</option>
            {catalogs.hairServices.map((service) => (
              <option key={service.id} value={service.id}>
                {service.name}
              </option>
            ))}
          </select>
        </div>
      )}

      <Button
        type="submit"
        size="lg"
        className="h-11 w-full sm:h-10 lg:w-auto lg:shrink-0"
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
        Найти
      </Button>

      {catalogs.status === 'error' && (
        <div
          role="alert"
          className="flex flex-col gap-2 rounded-lg border border-destructive/30 p-3 sm:col-span-2 sm:flex-row sm:items-center sm:justify-between lg:basis-full"
        >
          <p className="text-sm text-muted-foreground">
            {catalogs.error ?? 'Не удалось загрузить фильтры.'}
          </p>
          <Button
            type="button"
            variant="outline"
            className="h-11 w-full sm:h-9 sm:w-auto"
            onClick={catalogs.retry}
          >
            Повторить
          </Button>
        </div>
      )}
    </form>
  )
}
