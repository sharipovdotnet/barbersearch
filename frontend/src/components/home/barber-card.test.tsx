import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { BarberCard } from '@/components/home/barber-card'
import type { BarberSearchItem } from '@/lib/barber-search'

const BARBER: BarberSearchItem = {
  id: '11111111-1111-4111-8111-111111111111',
  displayName: 'Тимур Барберов',
  grade: 'SeniorBarber',
  yearsOfExperience: 8,
  languages: ['Ru'],
  rating: null,
  reviewsCount: 0,
  isVerified: true,
  primarySalonId: '22222222-2222-4222-8222-222222222222',
  primarySalonName: 'Барбершоп на Ленина',
  primaryPlaceKind: 'Salon',
  city: 'Екатеринбург',
  additionalServices: [],
}

/**
 * Подпись места в результате поиска: название и тип, а для дома и выезда —
 * понятная фраза вместо выдуманного названия «салона».
 */
describe('BarberCard: тип места', () => {
  it('для барбершопа показывает название, город и тип', () => {
    render(<BarberCard barber={BARBER} />)

    expect(
      screen.getByText('Барбершоп на Ленина · Екатеринбург · Барбершоп')
    ).toBeInTheDocument()
  })

  it('для приёма на дому — «На дому в <город>»', () => {
    render(
      <BarberCard
        barber={{ ...BARBER, primaryPlaceKind: 'Home', primarySalonName: 'Дом' }}
      />
    )

    expect(screen.getByText('На дому в Екатеринбург')).toBeInTheDocument()
  })

  it('для выезда — «Выезд по городу»', () => {
    render(
      <BarberCard
        barber={{
          ...BARBER,
          primaryPlaceKind: 'Mobile',
          primarySalonName: 'Выезд',
        }}
      />
    )

    expect(screen.getByText('Выезд по городу')).toBeInTheDocument()
  })

  it('без места оставляет прежний прочерк', () => {
    render(
      <BarberCard
        barber={{
          ...BARBER,
          primarySalonId: null,
          primarySalonName: null,
          primaryPlaceKind: null,
          city: null,
        }}
      />
    )

    expect(screen.getByText('Салон не указан')).toBeInTheDocument()
  })
})
