import { render, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { BarberCabinetHeading } from '@/components/barber/barber-cabinet-gates'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

describe('BarberUnreadBell', () => {
  it('показывает бейдж и имя со счётчиком непрочитанных', async () => {
    server.use(
      http.get('/api/barber/notifications/unread-count', () =>
        HttpResponse.json({ count: 3 })
      )
    )

    render(<BarberCabinetHeading />)

    const link = await screen.findByRole('link', {
      name: 'Уведомления, непрочитанных: 3',
    })

    expect(link).toHaveAttribute('href', '/barber/notifications')
    expect(within(link).getByText('3')).toBeInTheDocument()
    expect(
      screen.getByText('Непрочитанных уведомлений: 3')
    ).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      'Кабинет барбера'
    )
  })

  it('ограничивает бейдж значением 99+, а имя оставляет точным', async () => {
    server.use(
      http.get('/api/barber/notifications/unread-count', () =>
        HttpResponse.json({ count: 150 })
      )
    )

    render(<BarberCabinetHeading />)

    const link = await screen.findByRole('link', {
      name: 'Уведомления, непрочитанных: 150',
    })

    expect(within(link).getByText('99+')).toBeInTheDocument()
  })

  it('при нуле не рисует бейдж', async () => {
    render(<BarberCabinetHeading />)

    const link = await screen.findByRole('link', {
      name: 'Уведомления, непрочитанных: 0',
    })

    expect(within(link).queryByText('0')).not.toBeInTheDocument()
  })

  it('при ошибке счётчика молчит и скрывает бейдж', async () => {
    let calls = 0

    server.use(
      http.get('/api/barber/notifications/unread-count', () => {
        calls += 1

        return HttpResponse.json(problemDetails(500), { status: 500 })
      })
    )

    render(<BarberCabinetHeading />)

    await waitFor(() => {
      expect(calls).toBe(1)
    })

    const link = screen.getByRole('link', { name: 'Уведомления' })

    expect(link).toBeInTheDocument()
    expect(within(link).queryByText(/\d/)).not.toBeInTheDocument()
  })
})
