import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { BarberRequestForm } from '@/components/barber/barber-request-form'
import { SessionContext, type SessionContextValue } from '@/hooks/use-session'
import type { BarberRequest } from '@/lib/barber-requests'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Форма заявки «Стать барбером».
 *
 * Сеть — только MSW на границе HTTP. Проверяем обещания, которые не видит тест
 * транспорта: клиентские границы полей, ровно одно тело контракта (без роли!),
 * передачу созданной заявки наверх после `201` и русские тексты публичных кодов.
 * Карточку «Заявка отправлена» рисует страница, поэтому её сценарий живёт в
 * `become-a-barber-page.test.tsx`.
 */

const CREATED: BarberRequest = {
  id: '9ea00000-0000-4000-8000-000000000001',
  status: 'Pending',
  displayName: 'Иван Петров',
  yearsOfExperience: 7,
  city: 'Москва',
  about: 'Стригу и брею',
  hasSalon: true,
  createdAt: '2026-11-06T10:00:00+00:00',
  reviewedAt: null,
  rejectionReason: null,
}

const CUSTOMER = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'client@barbersearch.test',
  role: 'Customer',
  barberProfileId: null,
  isEmailVerified: true,
} as const

function sessionValue(): SessionContextValue {
  return {
    status: 'authenticated',
    user: CUSTOMER,
    signIn: async () => {},
    signOut: async () => {},
    retrySession: () => {},
  }
}

function renderForm() {
  const onCreated = vi.fn<(request: BarberRequest) => void>()

  render(
    <SessionContext.Provider value={sessionValue()}>
      <BarberRequestForm onCreated={onCreated} />
    </SessionContext.Provider>
  )

  return onCreated
}

type CapturedCreate = {
  body: unknown
  csrf: string | null
}

/** Перехватывает `POST /api/barber-requests` и отдаёт подготовленный ответ. */
function mockCreate(
  respond: () => Response = () =>
    HttpResponse.json(CREATED, { status: 201 })
): CapturedCreate[] {
  const captured: CapturedCreate[] = []

  server.use(
    http.post('/api/barber-requests', async ({ request }) => {
      captured.push({
        body: await request.json(),
        csrf: request.headers.get('X-CSRF-TOKEN'),
      })

      return respond()
    })
  )

  return captured
}

async function fillValidForm() {
  const user = userEvent.setup()

  await user.type(screen.getByLabelText('Как вас представить'), 'Иван Петров')
  await user.type(screen.getByLabelText('Опыт, лет'), '7')
  await user.type(screen.getByLabelText('Город'), 'Москва')

  return user
}
describe('BarberRequestForm', () => {
  it('пустая форма не отправляет запрос и показывает ошибки полей', async () => {
    const user = userEvent.setup()
    const captured = mockCreate()

    renderForm()

    await user.click(screen.getByRole('button', { name: 'Отправить заявку' }))

    expect(
      screen.getByText('Введите имя, которое увидят клиенты.')
    ).toBeInTheDocument()
    expect(screen.getByText('Укажите стаж в годах.')).toBeInTheDocument()
    expect(screen.getByText('Укажите город.')).toBeInTheDocument()
    expect(captured).toHaveLength(0)
  })

  it('стаж вне 1..70 отклоняется до запроса', async () => {
    const user = userEvent.setup()
    const captured = mockCreate()

    renderForm()

    await user.type(screen.getByLabelText('Как вас представить'), 'Иван Петров')
    await user.type(screen.getByLabelText('Город'), 'Москва')

    await user.type(screen.getByLabelText('Опыт, лет'), '0')
    await user.click(screen.getByRole('button', { name: 'Отправить заявку' }))

    expect(
      screen.getByText('Стаж — целое число от 1 года до 70 лет.')
    ).toBeInTheDocument()

    await user.clear(screen.getByLabelText('Опыт, лет'))
    await user.type(screen.getByLabelText('Опыт, лет'), '71')
    await user.click(screen.getByRole('button', { name: 'Отправить заявку' }))

    expect(
      screen.getByText('Стаж — целое число от 1 года до 70 лет.')
    ).toBeInTheDocument()
    expect(captured).toHaveLength(0)
  })

  it('границы текстовых полей: имя 121, город 101 и «о себе» 2001 отклоняются до запроса', async () => {
    const captured = mockCreate()
    const user = userEvent.setup()

    renderForm()

    fireEvent.change(screen.getByLabelText('Как вас представить'), {
      target: { value: 'я'.repeat(121) },
    })
    fireEvent.change(screen.getByLabelText('Опыт, лет'), {
      target: { value: '7' },
    })
    fireEvent.change(screen.getByLabelText('Город'), {
      target: { value: 'я'.repeat(101) },
    })
    fireEvent.change(screen.getByLabelText('О себе — необязательно'), {
      target: { value: 'я'.repeat(2001) },
    })

    await user.click(screen.getByRole('button', { name: 'Отправить заявку' }))

    expect(
      screen.getByText('Имя — не длиннее 120 символов.')
    ).toBeInTheDocument()
    expect(
      screen.getByText('Город — не длиннее 100 символов.')
    ).toBeInTheDocument()
    expect(
      screen.getByText('О себе — не длиннее 2000 символов.')
    ).toBeInTheDocument()
    expect(captured).toHaveLength(0)
  })

  it('отправляет тело контракта, CSRF-заголовок и отдаёт созданную заявку наверх', async () => {
    const captured = mockCreate()
    const onCreated = renderForm()

    const user = await fillValidForm()

    await user.type(screen.getByLabelText('О себе — необязательно'), '  Стригу и брею  ')
    await user.click(screen.getByRole('checkbox', { name: 'Уже работаю в салоне' }))
    await user.click(screen.getByRole('button', { name: 'Отправить заявку' }))

    await vi.waitFor(() => {
      expect(onCreated).toHaveBeenCalledWith(CREATED)
    })

    expect(captured).toHaveLength(1)
    expect(captured[0].csrf).toBe('csrf-token')
    // Роль в теле не передаётся: её выдаёт только администратор.
    expect(captured[0].body).toStrictEqual({
      displayName: 'Иван Петров',
      yearsOfExperience: 7,
      city: 'Москва',
      about: 'Стригу и брею',
      hasSalon: true,
    })
  })

  it('пустое «о себе» уходит на сервер как null', async () => {
    const captured = mockCreate()

    renderForm()

    await fillValidForm()
    await userEvent.setup().click(
      screen.getByRole('button', { name: 'Отправить заявку' })
    )

    expect(captured).toHaveLength(1)
    expect((captured[0].body as { about: unknown }).about).toBeNull()
  })

  it('409 barberRequest.create.alreadyPending: «Заявка уже на рассмотрении», без повтора', async () => {
    mockCreate(
      () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['barberRequest.create.alreadyPending'],
          }),
          { status: 409 }
        )
    )

    renderForm()
    await fillValidForm()
    await userEvent.setup().click(
      screen.getByRole('button', { name: 'Отправить заявку' })
    )

    expect(
      await screen.findByRole('alert')
    ).toHaveTextContent(/Заявка уже на рассмотрении/)
    expect(
      screen.queryByRole('button', { name: 'Повторить' })
    ).not.toBeInTheDocument()
    expect(screen.queryByText(/HTTP 409/)).not.toBeInTheDocument()
  })

  it('403: заявку может подать только клиент', async () => {
    mockCreate(
      () =>
        HttpResponse.json(
          problemDetails(403, {
            errorCodes: ['barberRequest.create.forbidden'],
          }),
          { status: 403 }
        )
    )

    renderForm()
    await fillValidForm()
    await userEvent.setup().click(
      screen.getByRole('button', { name: 'Отправить заявку' })
    )

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Заявку может подать только клиент.'
    )
  })

  it('429 предлагает «Повторить» и повтор отправляет вторую попытку', async () => {
    const user = userEvent.setup()
    let attempts = 0

    server.use(
      http.post('/api/barber-requests', () => {
        attempts += 1

        if (attempts === 1) {
          return HttpResponse.json(problemDetails(429), { status: 429 })
        }

        return HttpResponse.json(CREATED, { status: 201 })
      })
    )

    const onCreated = renderForm()
    await fillValidForm()
    await user.click(screen.getByRole('button', { name: 'Отправить заявку' }))

    const alert = await screen.findByRole('alert')

    expect(alert).toHaveTextContent(
      'Слишком много запросов. Подождите минуту и повторите.'
    )

    await user.click(screen.getByRole('button', { name: 'Повторить' }))

    await vi.waitFor(() => {
      expect(onCreated).toHaveBeenCalledWith(CREATED)
    })
    expect(attempts).toBe(2)
  })

  it('сетевой сбой показывает русский текст и «Повторить»', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('/api/barber-requests', () => HttpResponse.error())
    )

    renderForm()
    await fillValidForm()
    await user.click(screen.getByRole('button', { name: 'Отправить заявку' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Нет связи с сервером. Проверьте подключение и повторите попытку.'
    )
    expect(screen.getByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })
})
