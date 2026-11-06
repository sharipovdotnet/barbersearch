import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { BarberSlots } from '@/components/barber/barber-slots'
import type { BarberProfile } from '@/lib/catalog'
import { recordRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const PROFILE_ID = '11111111-1111-4111-8111-111111111111'
const USER_ID = '22222222-2222-4222-8222-222222222222'
const SALON_ID = '33333333-3333-4333-8333-333333333333'
const STUDIO_ID = '44444444-4444-4444-8444-444444444444'

const PROFILE: BarberProfile = {
  id: PROFILE_ID,
  userId: USER_ID,
  displayName: 'Тимур Барберов',
  grade: 'SeniorBarber',
  bio: null,
  yearsOfExperience: 8,
  languages: ['Ru'],
  certificatesAndTraining: [],
  rating: null,
  reviewsCount: 0,
  achievements: [],
  paymentMethods: [],
  communicationFormat: [],
  goodies: [],
  additionalServices: [],
  latePolicy: '',
  isActive: true,
  isVerified: true,
  services: [],
  salons: [],
  specializations: [],
}

const SALON = {
  id: '55555555-5555-4555-8555-555555555555',
  salonId: SALON_ID,
  name: 'Барбершоп на Ленина',
  address: 'ул. Ленина, 1',
  city: 'Екатеринбург',
  country: 'Россия',
  timeZoneId: 'Asia/Yekaterinburg',
  kind: 'Salon' as const,
  isPrimary: true,
  workingSchedules: [],
}

const STUDIO = {
  ...SALON,
  id: '66666666-6666-4666-8666-666666666666',
  salonId: STUDIO_ID,
  name: 'Кабинет на Мира',
  kind: 'SoloStudio' as const,
  isPrimary: false,
}

const SLOTS = {
  barberProfileId: PROFILE_ID,
  salonId: SALON_ID,
  barberServiceId: null,
  hairServiceId: null,
  date: '2026-11-05',
  timeZoneId: 'Asia/Yekaterinburg',
  dayStart: '2026-11-05T00:00:00Z',
  dayEnd: '2026-11-05T23:59:59Z',
  durationMinutes: 30,
  isWorkingDay: true,
  slots: [],
}

/**
 * «Свободное время» без места приёма.
 *
 * Раньше секция молча исчезала (`return null`); теперь клиент видит объяснение,
 * а `/slots` не запрашивается вовсе — записываться некуда.
 */
describe('BarberSlots без места приёма', () => {
  it('показывает объяснение и не запрашивает /slots', () => {
    const requests = recordRequests()

    render(<BarberSlots profile={PROFILE} />)

    expect(
      screen.getByRole('heading', { level: 2, name: 'Свободное время' })
    ).toBeInTheDocument()
    expect(
      screen.getByText(/Барбер пока не указал место приёма/)
    ).toBeInTheDocument()
    expect(
      screen.getByText(/Онлайн-запись появится, когда место будет добавлено/)
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: /Записаться/ })
    ).not.toBeInTheDocument()
    expect(requests).toStrictEqual([])
  })
})

describe('BarberSlots, подпись места в селекте', () => {
  it('показывает «Название · Тип» при нескольких местах', async () => {
    server.use(
      http.get(`/api/barbers/${PROFILE_ID}/slots`, () =>
        HttpResponse.json(SLOTS)
      )
    )

    render(
      <BarberSlots profile={{ ...PROFILE, salons: [SALON, STUDIO] }} />
    )

    const select = screen.getByRole('combobox', { name: 'Салон' })

    expect(select).toBeInTheDocument()
    expect(
      screen.getByRole('option', { name: 'Барбершоп на Ленина · Барбершоп' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('option', { name: 'Кабинет на Мира · Кабинет' })
    ).toBeInTheDocument()
  })
})
