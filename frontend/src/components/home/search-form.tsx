import { Loader2, Search } from 'lucide-react'
import { useState, type FormEvent } from 'react'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import type { BarberSearchFilters } from '@/lib/barber-search'
import { scrollToSection } from '@/lib/router'

type SearchFormProps = {
  isSearching: boolean
  onSearch: (filters: BarberSearchFilters) => void
}

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

/** Сегодняшняя дата в локальном поясе: `toISOString` дал бы UTC и мог сдвинуть день. */
function todayLocal(): string {
  const now = new Date()

  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/**
 * Форма поиска v1: город и дата.
 *
 * Текстового поиска и фильтров по услугам в API нет, поэтому полей ровно два.
 * Состояние полей локальное: последний отправленный фильтр хранит страница,
 * чтобы подсказка в пустой выдаче говорила про тот город, который искали.
 */
export function SearchForm({ isSearching, onSearch }: SearchFormProps) {
  const [city, setCity] = useState('')
  const [date, setDate] = useState('')

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    scrollToSection('barbers')
    onSearch({ city, date })
  }

  return (
    <form
      className="mt-8 flex max-w-xl flex-col gap-2 sm:flex-row"
      onSubmit={handleSubmit}
    >
      <div className="flex-1">
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

      <div className="sm:w-44">
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

      <Button
        type="submit"
        size="lg"
        className="h-11 sm:h-10"
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
    </form>
  )
}
