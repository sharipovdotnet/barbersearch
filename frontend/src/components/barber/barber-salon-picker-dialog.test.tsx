import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { SalonPickerDialog } from '@/components/barber/barber-salon-picker-dialog'
import { problemDetails } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const PROFILE_ID = '11111111-1111-4111-8111-111111111111'
const SALON_ID = '22222222-2222-4222-8222-222222222222'
const ATTACH_URL = `/api/barber-profiles/${PROFILE_ID}/salons`

const SALON = {
  id: SALON_ID,
  name: 'Барбершоп на Ленина',
  city: 'Екатеринбург',
  address: 'ул. Ленина, 1',
  timeZoneId: 'Asia/Yekaterinburg',
  kind: 'Salon',
}

function renderDialog(hasSalons = false) {
  const onAttached = vi.fn()
  const onStale = vi.fn()

  render(
    <SalonPickerDialog
      barberProfileId={PROFILE_ID}
      hasSalons={hasSalons}
      onAttached={onAttached}
      onStale={onStale}
    />
  )

  return { onAttached, onStale }
}

async function openDialog() {
  await userEvent.click(screen.getByRole('button', { name: 'Добавить салон' }))
  return screen.getByLabelText('Поиск')
}

function mockCatalog(rows: unknown[] = [SALON]) {
  const urls: string[] = []

  server.use(
    http.get('/api/salons', ({ request }) => {
      urls.push(request.url)

      return HttpResponse.json(rows)
    })
  )

  return urls
}

describe('SalonPickerDialog', () => {
  it('ищет с debounce и одним запросом на ввод', async () => {
    const urls = mockCatalog()
    renderDialog()

    const search = await openDialog()
    await userEvent.type(search, 'Бар')

    expect(await screen.findByRole('radio')).toBeInTheDocument()

    // Три нажатия — один запрос: предыдущий таймер снимается.
    expect(urls).toHaveLength(1)
    expect(new URL(urls[0]!).searchParams.get('search')).toBe('Бар')
  })

  it('отменяет запрос при закрытии диалога', async () => {
    const urls = mockCatalog()
    const requests = countRequests('/api/salons')
    renderDialog()

    const search = await openDialog()
    await userEvent.type(search, 'Бар')

    await userEvent.click(screen.getByRole('button', { name: 'Отмена' }))

    await new Promise((resolve) => setTimeout(resolve, 400))

    expect(requests()).toBe(0)
    expect(urls).toHaveLength(0)
  })

  it('первый салон: checkbox основной, disabled и отмечен', async () => {
    mockCatalog()
    let body: unknown = null

    server.use(
      http.post(ATTACH_URL, async ({ request }) => {
        body = await request.json()

        return new HttpResponse(null, { status: 204 })
      })
    )

    const { onAttached } = renderDialog(false)

    const search = await openDialog()
    await userEvent.type(search, 'Бар')
    await userEvent.click(await screen.findByRole('radio'))

    const checkbox = screen.getByRole('checkbox')
    expect(checkbox).toHaveAttribute('aria-disabled', 'true')
    expect(checkbox).toBeChecked()

    await userEvent.click(screen.getByRole('button', { name: 'Добавить' }))

    await waitFor(() => {
      expect(onAttached).toHaveBeenCalledTimes(1)
    })
    expect(body).toEqual({ salonId: SALON_ID, isPrimary: true })
  })

  it('при существующих салонах «сделать основным» — выбор пользователя', async () => {
    mockCatalog()
    let body: unknown = null

    server.use(
      http.post(ATTACH_URL, async ({ request }) => {
        body = await request.json()

        return new HttpResponse(null, { status: 204 })
      })
    )

    renderDialog(true)

    const search = await openDialog()
    await userEvent.type(search, 'Бар')
    await userEvent.click(await screen.findByRole('radio'))

    const checkbox = screen.getByRole('checkbox')
    expect(checkbox).not.toHaveAttribute('aria-disabled', 'true')
    expect(checkbox).not.toBeChecked()

    await userEvent.click(screen.getByRole('button', { name: 'Добавить' }))

    await waitFor(() => {
      expect(body).toEqual({ salonId: SALON_ID, isPrimary: false })
    })
  })

  it('409 оставляет диалог открытым и обновляет «Мои салоны»', async () => {
    mockCatalog()
    server.use(
      http.post(ATTACH_URL, () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['barberProfile.addSalon.duplicate'],
          }),
          { status: 409 }
        )
      )
    )

    const { onAttached, onStale } = renderDialog()

    const search = await openDialog()
    await userEvent.type(search, 'Бар')
    await userEvent.click(await screen.findByRole('radio'))
    await userEvent.click(screen.getByRole('button', { name: 'Добавить' }))

    expect(
      await screen.findByText('Этот салон уже добавлен.')
    ).toBeInTheDocument()
    expect(screen.getByRole('radio')).toBeInTheDocument()
    expect(onAttached).not.toHaveBeenCalled()
    expect(onStale).toHaveBeenCalledTimes(1)
  })

  it('404 показывает русский текст и не закрывает диалог', async () => {
    mockCatalog()

    server.use(
      http.post(ATTACH_URL, () =>
        HttpResponse.json(
          problemDetails(404, {
            errorCodes: ['barberProfile.addSalon.salonNotFound'],
          }),
          { status: 404 }
        )
      )
    )

    const { onAttached } = renderDialog()

    const search = await openDialog()
    await userEvent.type(search, 'Бар')
    await userEvent.click(await screen.findByRole('radio'))
    await userEvent.click(screen.getByRole('button', { name: 'Добавить' }))

    expect(await screen.findByText('Салон больше не доступен.')).toBeInTheDocument()
    expect(screen.getByRole('radio')).toBeInTheDocument()
    expect(onAttached).not.toHaveBeenCalled()
  })

  it('429 каталога показывает текст лимита и кнопку повтора', async () => {
    server.use(
      http.get('/api/salons', () =>
        HttpResponse.json(problemDetails(429), { status: 429 })
      )
    )

    renderDialog()

    const search = await openDialog()
    await userEvent.type(search, 'Бар')

    expect(
      await screen.findByText(
        'Слишком много запросов. Подождите минуту и повторите.'
      )
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })

  it('короткий запрос не ходит в каталог', async () => {
    const requests = countRequests('/api/salons')
    renderDialog()

    const search = await openDialog()
    await userEvent.type(search, 'Б')

    await new Promise((resolve) => setTimeout(resolve, 400))

    expect(requests()).toBe(0)
    expect(
      screen.getByText(/минимум 2 символа/)
    ).toBeInTheDocument()
  })
})
