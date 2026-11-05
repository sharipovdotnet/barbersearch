import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { BarberRequestCard } from '@/components/account/barber-request-card'
import type { BarberRequest } from '@/lib/barber-requests'
import { problemDetails, unauthorized } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Карточка заявки в кабинете клиента.
 *
 * Она не блокирует список записей: своя загрузка, своя ошибка и своя кнопка
 * «Повторить», а `401` не показывается вовсе — гейт кабинета уже выше.
 */

const MINE_URL = '/api/barber-requests/mine'

const PENDING: BarberRequest = {
  id: '9ea00000-0000-4000-8000-000000000001',
  status: 'Pending',
  displayName: 'Иван Петров',
  yearsOfExperience: 7,
  city: 'Москва',
  about: null,
  hasSalon: false,
  createdAt: '2026-11-06T10:00:00+00:00',
  reviewedAt: null,
  rejectionReason: null,
}

function renderCard() {
  window.history.replaceState(null, '', '/account')

  return render(<BarberRequestCard />)
}

describe('BarberRequestCard', () => {
  it('без заявки зовёт «Стать барбером» и ведёт на страницу заявки', async () => {
    const user = userEvent.setup()

    server.use(http.get(MINE_URL, () => HttpResponse.json({ request: null })))

    renderCard()

    const cta = await screen.findByRole('button', { name: 'Стать барбером' })

    await user.click(cta)

    await waitFor(() => {
      expect(window.location.pathname).toBe('/become-a-barber')
    })
  })

  it('с заявкой показывает статус, дату и ссылку «Подробнее»', async () => {
    server.use(
      http.get(MINE_URL, () => HttpResponse.json({ request: PENDING }))
    )

    renderCard()

    expect(await screen.findByText('На рассмотрении')).toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: 'Подробнее' })
    ).toHaveAttribute('href', '/become-a-barber')
    expect(
      screen.queryByRole('button', { name: 'Стать барбером' })
    ).not.toBeInTheDocument()
  })

  it('отклонённая заявка показывает причину', async () => {
    server.use(
      http.get(MINE_URL, () =>
        HttpResponse.json({
          request: {
            ...PENDING,
            status: 'Rejected',
            rejectionReason: 'Не хватает опыта',
          },
        })
      )
    )

    renderCard()

    expect(
      await screen.findByText('Причина: Не хватает опыта')
    ).toBeInTheDocument()
  })

  it('429 показывает тихую строку с «Повторить» и повторяет чтение', async () => {
    const user = userEvent.setup()
    let attempts = 0

    server.use(
      http.get(MINE_URL, () => {
        attempts += 1

        if (attempts === 1) {
          return HttpResponse.json(problemDetails(429), { status: 429 })
        }

        return HttpResponse.json({ request: PENDING })
      })
    )

    renderCard()

    expect(
      await screen.findByText(
        /Слишком много запросов\. Подождите минуту и повторите\./
      )
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Повторить' }))

    expect(await screen.findByText('На рассмотрении')).toBeInTheDocument()
    expect(attempts).toBe(2)
  })

  it('401 не показывает ни ошибки, ни кнопок', async () => {
    server.use(http.get(MINE_URL, () => unauthorized()))

    const { container } = renderCard()

    await waitFor(() => {
      expect(container).toBeEmptyDOMElement()
    })
  })
})
