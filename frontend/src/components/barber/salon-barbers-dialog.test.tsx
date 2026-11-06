import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { SalonBarbersDialog } from '@/components/barber/salon-barbers-dialog'
import type { BarberSalonSummary } from '@/lib/barber-salons'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const SALON_ID = '22222222-2222-4222-8222-222222222222'
const OWNER_USER_ID = '33333333-3333-4333-8333-333333333333'
const OWNER_PROFILE_ID = '55555555-5555-4555-8555-555555555555'
const OTHER_PROFILE_ID = '66666666-6666-4666-8666-666666666666'
const OTHER_USER_ID = '77777777-7777-4777-8777-777777777777'
const CANDIDATE_PROFILE_ID = '88888888-8888-4888-8888-888888888888'
const CANDIDATE_USER_ID = '99999999-9999-4999-8999-999999999999'

const LIST_URL = `/api/salons/${SALON_ID}/barbers`
const SEARCH_URL = `/api/salons/${SALON_ID}/barber-candidates`

const SALON: BarberSalonSummary = {
  salonId: SALON_ID,
  name: 'Барбершоп на Ленина',
  city: 'Екатеринбург',
  address: 'ул. Ленина, 1',
  timeZoneId: 'Asia/Yekaterinburg',
  kind: 'Salon',
  isListed: true,
  isPrimary: true,
  country: 'Russia',
  postalCode: '620000',
  latitude: 56.8389,
  longitude: 60.6057,
  phoneNumber: null,
  websiteUrl: null,
  ownerUserId: OWNER_USER_ID,
  moderationStatus: 'Active',
  rejectionReason: null,
}

const OWNER = {
  barberProfileId: OWNER_PROFILE_ID,
  userId: OWNER_USER_ID,
  displayName: 'Владелец Салонова',
  email: 'owner@barbersearch.test',
  isActive: true,
  isVerified: true,
}

const OTHER = {
  barberProfileId: OTHER_PROFILE_ID,
  userId: OTHER_USER_ID,
  displayName: 'Другой Барберов',
  email: 'other@barbersearch.test',
  isActive: true,
  isVerified: true,
}

const CANDIDATE = {
  barberProfileId: CANDIDATE_PROFILE_ID,
  userId: CANDIDATE_USER_ID,
  displayName: 'Новый Барберов',
  email: 'new@barbersearch.test',
  isActive: true,
  isVerified: false,
}

function renderDialog() {
  return render(<SalonBarbersDialog salon={SALON} onClose={() => {}} />)
}

describe('SalonBarbersDialog', () => {
  it('показывает состав, помечает владельца и не даёт его убрать', async () => {
    server.use(http.get(LIST_URL, () => HttpResponse.json([OWNER, OTHER])))

    renderDialog()

    expect(await screen.findByText(OWNER.displayName)).toBeInTheDocument()
    expect(screen.getByText(OTHER.displayName)).toBeInTheDocument()
    expect(screen.getByText('Владелец')).toBeInTheDocument()

    expect(
      screen.queryByRole('button', {
        name: `Убрать ${OWNER.displayName} из салона`,
      })
    ).not.toBeInTheDocument()
    expect(
      screen.getByRole('button', {
        name: `Убрать ${OTHER.displayName} из салона`,
      })
    ).toBeInTheDocument()
  })

  it('ищет кандидата, прикрепляет его и перечитывает состав', async () => {
    const user = userEvent.setup()
    let listCalls = 0
    let search: string | null = null
    let body: unknown = null

    server.use(
      http.get(LIST_URL, () => {
        listCalls += 1

        return HttpResponse.json(
          listCalls === 1 ? [OWNER] : [OWNER, CANDIDATE]
        )
      }),
      http.get(SEARCH_URL, ({ request }) => {
        search = new URL(request.url).searchParams.get('search')

        return HttpResponse.json([CANDIDATE])
      }),
      http.post(LIST_URL, async ({ request }) => {
        body = await request.json()

        return new HttpResponse(null, { status: 204 })
      })
    )

    renderDialog()

    expect(await screen.findByText(OWNER.displayName)).toBeInTheDocument()

    await user.type(screen.getByLabelText('Поиск барбера'), 'Новый')

    await waitFor(() => {
      expect(search).toBe('Новый')
    })

    const candidateRow = (await screen.findByText(CANDIDATE.displayName)).closest(
      'li'
    )

    expect(candidateRow).not.toBeNull()
    expect(within(candidateRow!).getByText('На проверке')).toBeInTheDocument()

    await user.click(
      within(candidateRow!).getByRole('button', {
        name: `Добавить барбера ${CANDIDATE.displayName}`,
      })
    )

    await waitFor(() => {
      expect(body).toEqual({ barberProfileId: CANDIDATE_PROFILE_ID })
    })

    expect(
      await screen.findByText(`Барбер добавлен: ${CANDIDATE.displayName}.`)
    ).toBeInTheDocument()
    expect(await screen.findByText(CANDIDATE.email)).toBeInTheDocument()
    expect(listCalls).toBe(2)
  })

  it('убирает барбера только после подтверждения', async () => {
    const user = userEvent.setup()
    let listCalls = 0
    let removed = false

    server.use(
      http.get(LIST_URL, () => {
        listCalls += 1

        return HttpResponse.json(removed ? [OWNER] : [OWNER, OTHER])
      }),
      http.delete(`${LIST_URL}/${OTHER_PROFILE_ID}`, () => {
        removed = true

        return new HttpResponse(null, { status: 204 })
      })
    )

    renderDialog()

    await user.click(
      await screen.findByRole('button', {
        name: `Убрать ${OTHER.displayName} из салона`,
      })
    )

    expect(
      screen.getByText(`Убрать ${OTHER.displayName} из салона?`, {
        exact: false,
      })
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Убрать' }))

    expect(
      await screen.findByText(
        `Барбер убран из салона: ${OTHER.displayName}.`
      )
    ).toBeInTheDocument()
    await waitFor(() => {
      expect(screen.queryByText(OTHER.displayName)).not.toBeInTheDocument()
    })
    expect(listCalls).toBe(2)
  })

  it('409 дубликата показывает причину и перечитывает состав', async () => {
    const user = userEvent.setup()
    let listCalls = 0

    server.use(
      http.get(LIST_URL, () => {
        listCalls += 1

        return HttpResponse.json([OWNER])
      }),
      http.get(SEARCH_URL, () => HttpResponse.json([CANDIDATE])),
      http.post(LIST_URL, () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['barberProfile.addSalon.duplicate'],
          }),
          { status: 409 }
        )
      )
    )

    renderDialog()

    expect(await screen.findByText(OWNER.displayName)).toBeInTheDocument()

    await user.type(screen.getByLabelText('Поиск барбера'), 'Новый')

    const candidateRow = (await screen.findByText(CANDIDATE.displayName)).closest(
      'li'
    )

    expect(candidateRow).not.toBeNull()

    await user.click(
      within(candidateRow!).getByRole('button', {
        name: `Добавить барбера ${CANDIDATE.displayName}`,
      })
    )

    expect(
      await screen.findByText('Этот барбер уже работает в салоне.')
    ).toBeInTheDocument()
    await waitFor(() => {
      expect(listCalls).toBe(2)
    })
  })
})
