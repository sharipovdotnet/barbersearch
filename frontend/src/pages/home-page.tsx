import { useEffect, useState } from 'react'

import { BarberResults } from '@/components/home/barber-results'
import { HomeCta } from '@/components/home/home-cta'
import { HomeHero } from '@/components/home/home-hero'
import { HowItWorks } from '@/components/home/how-it-works'
import { useBarberSearch } from '@/hooks/use-barber-search'
import type { BarberSearchFilters } from '@/lib/barber-search'
import { scrollToSection } from '@/lib/router'

const INITIAL_FILTERS: BarberSearchFilters = { city: '', date: '' }

/**
 * Витрина: поиск барбера и объяснение того, как устроена запись.
 *
 * Ссылки вида `/#barbers` ведут сюда и с кабинета: после монтирования страница
 * доскролливает до секции из адреса, потому что `pushState` сам этого не делает.
 * Фильтры живут в состоянии, а не в URL: шаринг ссылки на поиск — отдельная задача.
 */
export function HomePage() {
  const search = useBarberSearch()
  const [filters, setFilters] = useState<BarberSearchFilters>(INITIAL_FILTERS)

  useEffect(() => {
    document.title = 'BarberSearch — поиск барберов и запись'

    const target = window.location.hash.replace('#', '')
    if (target) {
      scrollToSection(target)
    }
  }, [])

  function handleSearch(next: BarberSearchFilters) {
    const applied: BarberSearchFilters = {
      city: next.city.trim(),
      date: next.date,
    }

    setFilters(applied)
    search.submit(applied)
  }

  return (
    <>
      <HomeHero
        isSearching={search.status === 'loading'}
        onSearch={handleSearch}
      />
      <HowItWorks />
      <BarberResults
        items={search.items}
        status={search.status}
        error={search.error}
        loadMoreError={search.loadMoreError}
        hasMore={search.hasMore}
        isLoadingMore={search.isLoadingMore}
        filters={filters}
        onRetry={search.retry}
        onLoadMore={search.loadMore}
      />
      <HomeCta />
    </>
  )
}
