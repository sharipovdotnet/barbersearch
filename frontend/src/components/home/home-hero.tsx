import { Badge } from '@/components/ui/badge'
import { SearchForm } from '@/components/home/search-form'
import type { BarberSearchFilters } from '@/lib/barber-search'

type HomeHeroProps = {
  isSearching: boolean
  onSearch: (filters: BarberSearchFilters) => void
}

/** Первый экран витрины: заголовок и форма поиска по городу, дате и фильтрам. */
export function HomeHero({ isSearching, onSearch }: HomeHeroProps) {
  return (
    <section className="mx-auto max-w-5xl px-4 pt-14 pb-10 sm:pt-20">
      <Badge variant="secondary" className="mb-4">
        Бета
      </Badge>
      <h1 className="max-w-2xl font-heading text-4xl font-semibold tracking-tight text-balance sm:text-5xl">
        Найди барбера и запишись за минуту
      </h1>
      <p className="mt-4 max-w-xl text-muted-foreground">
        Поиск по городу, дате, языку, специализации и услуге среди подтверждённых
        барберов. Без звонков и переписок.
      </p>

      <SearchForm isSearching={isSearching} onSearch={onSearch} />
    </section>
  )
}
