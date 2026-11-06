import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { BarberProfileForm } from '@/components/barber/barber-profile-form'
import type { BarberProfilePrefill } from '@/components/barber/barber-profile-form'
import { problemDetails } from '@/test/msw/handlers'
import { countRequests, recordRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const CREATE_URL = '/api/barber-profiles'
const CREATED_ID = '77777777-7777-4777-8777-777777777777'

/** Тело создания профиля целиком: экран не вправе придумывать лишние поля. */
const EXPECTED_BODY = {
  displayName: 'Тимур Барберов',
  yearsOfExperience: 8,
  grade: 'SeniorBarber',
  bio: 'Стрижки и борода.',
  languages: ['Ru', 'En'],
  certificatesAndTraining: ['Курс барберинга', 'Обучение колористике'],
  achievements: ['Топ-10 в районе'],
  paymentMethods: ['Card', 'Sbp'],
  communicationFormat: ['Люблю поболтать'],
  goodies: ['Кофе'],
  additionalServices: ['Бритьё головы', 'Камуфляж седины'],
  latePolicy: 'Жду максимум 10 минут',
}

function renderForm(prefill?: BarberProfilePrefill) {
  const onCreated = vi.fn()
  const onAlreadyExists = vi.fn()

  render(
    <BarberProfileForm
      onCreated={onCreated}
      onAlreadyExists={onAlreadyExists}
      prefill={prefill}
    />
  )

  return { onCreated, onAlreadyExists }
}

/** Значение поля: `fireEvent.change` быстрее посимвольного ввода и тот же `onChange`. */
function change(label: string, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } })
}

async function clickOption(name: string) {
  await userEvent.click(screen.getByRole('checkbox', { name }))
}

async function fillValidForm() {
  change('Как вас представить', 'Тимур Барберов')
  change('Опыт, лет', '8')
  change('Грейд', 'SeniorBarber')
  change('О себе', 'Стрижки и борода.')
  await clickOption('Русский')
  await clickOption('English')
  await clickOption('Карта')
  await clickOption('СБП')
  // Пустая строка и пробелы по краям не должны попасть в тело запроса.
  change(
    'Сертификаты и обучение',
    'Курс барберинга\n\n  Обучение колористике  '
  )
  change('Достижения', 'Топ-10 в районе')
  change('Формат общения', 'Люблю поболтать')
  change('Комфорт в кресле', 'Кофе')
  change('Дополнительные услуги', 'Бритьё головы\n\n  Камуфляж седины  ')
  change('Опоздания', 'Жду максимум 10 минут')
}

async function submit() {
  await userEvent.click(
    screen.getByRole('button', { name: 'Создать профиль' })
  )
}

describe('BarberProfileForm', () => {
  it('пустая форма: ошибки у имени, опыта и языков, запросов нет', async () => {
    const requests = recordRequests()

    renderForm()
    await submit()

    expect(
      screen.getByText('Укажите, как вас представить.')
    ).toBeInTheDocument()
    expect(screen.getByText('Укажите опыт работы в годах.')).toBeInTheDocument()
    expect(screen.getByText('Выберите грейд.')).toBeInTheDocument()
    expect(screen.getByText('Выберите хотя бы один язык.')).toBeInTheDocument()
    expect(requests.filter((path) => path.startsWith('/api/barber'))).toEqual([])
  })

  it('грейд обязателен и «Без грейда» в выборе нет', () => {
    renderForm()

    expect(screen.getByLabelText('Грейд')).toHaveValue('')
    expect(
      screen.queryByRole('option', { name: 'Без грейда' })
    ).not.toBeInTheDocument()
    expect(
      screen.getByRole('option', { name: 'Старший барбер' })
    ).toBeInTheDocument()
  })

  it('опыт 0 и 71: ошибка диапазона, запросов нет', async () => {    const createCalls = countRequests(CREATE_URL)

    renderForm()
    change('Как вас представить', 'Тимур')
    await clickOption('Русский')

    change('Опыт, лет', '0')
    await submit()
    expect(screen.getByText('Опыт — от 1 до 70 лет.')).toBeInTheDocument()

    change('Опыт, лет', '71')
    await submit()
    expect(screen.getByText('Опыт — от 1 до 70 лет.')).toBeInTheDocument()

    expect(createCalls()).toBe(0)
  })

  it('валидная форма: одно POST с телом по контракту', async () => {
    let body: unknown = null

    server.use(
      http.post(CREATE_URL, async ({ request }) => {
        body = await request.json()

        return HttpResponse.json({ id: CREATED_ID }, { status: 201 })
      })
    )

    const { onCreated, onAlreadyExists } = renderForm()

    await fillValidForm()
    await submit()

    await waitFor(() => {
      expect(onCreated).toHaveBeenCalledWith(CREATED_ID)
    })

    expect(body).toStrictEqual(EXPECTED_BODY)
    expect(onAlreadyExists).not.toHaveBeenCalled()
  })

  it('409 «профиль уже есть» отдаётся экрану, а не показывается ошибкой', async () => {
    server.use(
      http.post(CREATE_URL, () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['barberProfile.create.alreadyExists'],
          }),
          { status: 409 }
        )
      )
    )

    const { onCreated, onAlreadyExists } = renderForm()

    await fillValidForm()
    await submit()

    await waitFor(() => {
      expect(onAlreadyExists).toHaveBeenCalledTimes(1)
    })

    expect(onCreated).not.toHaveBeenCalled()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('400 «слишком длинное имя» показывается у поля, форма остаётся живой', async () => {
    server.use(
      http.post(CREATE_URL, () =>
        HttpResponse.json(
          problemDetails(400, {
            errorCodes: ['barberProfile.displayName.tooLong'],
          }),
          { status: 400 }
        )
      )
    )

    const { onCreated } = renderForm()

    await fillValidForm()
    await submit()

    expect(
      await screen.findByText('Не длиннее 120 символов.')
    ).toBeInTheDocument()
    expect(onCreated).not.toHaveBeenCalled()
    expect(
      screen.getByRole('button', { name: 'Создать профиль' })
    ).toBeEnabled()
  })

  it('429: русский текст с повтором, повтор шлёт ровно один запрос', async () => {
    const createCalls = countRequests(CREATE_URL)

    server.use(
      http.post(CREATE_URL, () =>
        HttpResponse.json(problemDetails(429), { status: 429 })
      )
    )

    renderForm()
    await fillValidForm()
    await submit()

    expect(
      await screen.findByText(
        'Слишком много запросов. Подождите минуту и повторите.'
      )
    ).toBeInTheDocument()
    expect(createCalls()).toBe(1)

    await userEvent.click(screen.getByRole('button', { name: 'Повторить' }))

    await waitFor(() => {
      expect(createCalls()).toBe(2)
    })
  })

  it('сетевая ошибка: «Нет связи с сервером» и повтор', async () => {
    server.use(http.post(CREATE_URL, () => HttpResponse.error()))

    renderForm()
    await fillValidForm()
    await submit()

    expect(
      await screen.findByText(
        'Нет связи с сервером. Проверьте подключение и повторите попытку.'
      )
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })

  it('двойной клик по кнопке отправляет один запрос', async () => {
    const createCalls = countRequests(CREATE_URL)

    server.use(
      http.post(CREATE_URL, async () => {
        await new Promise((resolve) => {
          setTimeout(resolve, 50)
        })

        return HttpResponse.json({ id: CREATED_ID }, { status: 201 })
      })
    )

    renderForm()
    await fillValidForm()

    const button = screen.getByRole('button', { name: 'Создать профиль' })

    fireEvent.click(button)
    fireEvent.click(button)

    await waitFor(() => {
      expect(createCalls()).toBe(1)
    })
  })

  it('валидная форма без необязательных полей: пустые строки null, списки []', async () => {
    let body: unknown = null

    server.use(
      http.post(CREATE_URL, async ({ request }) => {
        body = await request.json()

        return HttpResponse.json({ id: CREATED_ID }, { status: 201 })
      })
    )

    renderForm()
    change('Как вас представить', 'Тимур')
    change('Опыт, лет', '1')
    change('Грейд', 'Intern')
    await clickOption('Русский')
    await submit()

    await waitFor(() => {
      expect(body).not.toBeNull()
    })

    expect(body).toStrictEqual({
      displayName: 'Тимур',
      yearsOfExperience: 1,
      grade: 'Intern',
      bio: null,
      languages: ['Ru'],
      certificatesAndTraining: [],
      achievements: [],
      paymentMethods: [],
      communicationFormat: [],
      goodies: [],
      additionalServices: [],
      latePolicy: null,
    })
  })

  it('заявка: имя, опыт и «О себе» заполнены и только для чтения', () => {
    renderForm({
      displayName: 'Тимур Барберов',
      yearsOfExperience: 8,
      bio: 'Стрижки и борода.',
    })

    expect(screen.getByLabelText('Как вас представить')).toHaveValue(
      'Тимур Барберов'
    )
    expect(screen.getByLabelText('Опыт, лет')).toHaveValue(8)
    expect(screen.getByLabelText('О себе')).toHaveValue('Стрижки и борода.')

    for (const label of ['Как вас представить', 'Опыт, лет', 'О себе']) {
      expect(screen.getByLabelText(label)).toHaveAttribute('readonly')
    }

    expect(
      screen.getAllByText('Перенесено из заявки — изменить нельзя.')
    ).toHaveLength(3)
  })

  it('заявка без «О себе»: описание остаётся редактируемым', () => {
    renderForm({
      displayName: 'Тимур Барберов',
      yearsOfExperience: 8,
      bio: null,
    })

    expect(screen.getByLabelText('Как вас представить')).toHaveAttribute(
      'readonly'
    )
    expect(screen.getByLabelText('Опыт, лет')).toHaveAttribute('readonly')
    expect(screen.getByLabelText('О себе')).not.toHaveAttribute('readonly')
    expect(
      screen.getAllByText('Перенесено из заявки — изменить нельзя.')
    ).toHaveLength(2)
  })

  it('профиль уходит с данными заявки: POST повторяет её значения', async () => {
    let body: unknown = null

    server.use(
      http.post(CREATE_URL, async ({ request }) => {
        body = await request.json()

        return HttpResponse.json({ id: CREATED_ID }, { status: 201 })
      })
    )

    renderForm({
      displayName: 'Тимур Барберов',
      yearsOfExperience: 8,
      bio: null,
    })

    change('Грейд', 'ArtDirector')
    await clickOption('Русский')
    await submit()

    await waitFor(() => {
      expect(body).not.toBeNull()
    })

    expect(body).toStrictEqual({
      displayName: 'Тимур Барберов',
      yearsOfExperience: 8,
      grade: 'ArtDirector',
      bio: null,
      languages: ['Ru'],
      certificatesAndTraining: [],
      achievements: [],
      paymentMethods: [],
      communicationFormat: [],
      goodies: [],
      additionalServices: [],
      latePolicy: null,
    })
  })
})
