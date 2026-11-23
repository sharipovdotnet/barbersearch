import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { SearchForm } from '@/components/home/search-form'
import type { CitySuggestion } from '@/lib/address-suggestions'
import type { SpecializationOption } from '@/lib/specializations'
import { problemDetails } from '@/test/msw/handlers'
import { recordRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

/**
 * Строка справочника: `category` обязательна, строгий guard отклоняет список без неё,
 * и тогда форма показывает «Не удалось загрузить фильтры.».
 */
const SPECIALIZATION = {
  id: '11111111-1111-4111-8111-111111111111',
  name: 'Барберинг',
  description: null,
  category: 'Haircut',
} as const

/**
 * По одной строке на каждую из шести категорий, вперемешку: порядок групп задаёт
 * `SERVICE_CATEGORY_ORDER`, а не порядок ответа API.
 */
const SPECIALIZATIONS: readonly SpecializationOption[] = [
  {
    id: '55555555-5555-4555-8555-555555555555',
    name: 'Уход за волосами',
    description: null,
    category: 'HairTreatment',
  },
  SPECIALIZATION,
  {
    id: '66666666-6666-4666-8666-666666666666',
    name: 'Бритьё головы',
    description: null,
    category: 'Other',
  },
  {
    id: '22222222-3333-4222-8222-222222222222',
    name: 'Оформление бороды',
    description: null,
    category: 'Beard',
  },
  {
    id: '44444444-4444-4444-8444-444444444444',
    name: 'Укладка',
    description: null,
    category: 'Styling',
  },
  {
    id: '33333333-3333-4333-8333-333333333333',
    name: 'Окрашивание',
    description: null,
    category: 'Coloring',
  },
]

const HAIR_SERVICE = {
  id: '22222222-2222-4222-8222-222222222222',
  name: 'Стрижка',
  category: 'Haircut',
  defaultDurationMinutes: 45,
}

const ADDITIONAL_SERVICES = ['Оформление бороды', 'Бритьё головы']

function useCatalogs() {
  server.use(
    http.get('/api/specializations', () => HttpResponse.json(SPECIALIZATIONS)),
    http.get('/api/hair-services', () => HttpResponse.json([HAIR_SERVICE])),
    http.get('/api/barbers/additional-services', () =>
      HttpResponse.json(ADDITIONAL_SERVICES)
    )
  )
}

/** Значения списка подсказок: `<option>` в datalist живут вне видимой сетки. */
function suggestionValues(): string[] {
  const list = document.getElementById('search-additional-services')

  return Array.from(list?.querySelectorAll('option') ?? []).map(
    (option) => option.value
  )
}

describe('SearchForm, дополнительная услуга', () => {
  it('подсказки приходят из API и уходят подстрокой при submit', async () => {
    useCatalogs()

    const onSearch = vi.fn()

    render(<SearchForm isSearching={false} onSearch={onSearch} />)

    const input = screen.getByLabelText('Дополнительная услуга')

    expect(input).toHaveAttribute('list', 'search-additional-services')

    await waitFor(() => {
      expect(suggestionValues()).toStrictEqual(ADDITIONAL_SERVICES)
    })

    await userEvent.type(input, '  бород  ')
    await userEvent.click(screen.getByRole('button', { name: 'Найти' }))

    expect(onSearch).toHaveBeenCalledTimes(1)
    expect(onSearch).toHaveBeenCalledWith({
      city: '',
      date: '',
      specializationId: '',
      hairServiceId: '',
      language: '',
      additionalService: '  бород  ',
    })
  })

  it('сбой подсказок не блокирует свободное поле', async () => {
    useCatalogs()
    server.use(
      http.get('/api/barbers/additional-services', () => HttpResponse.error())
    )

    const onSearch = vi.fn()

    render(<SearchForm isSearching={false} onSearch={onSearch} />)

    const input = screen.getByLabelText('Дополнительная услуга')

    await waitFor(() => {
      expect(
        screen.getByRole('option', { name: 'Барберинг' })
      ).toBeInTheDocument()
    })

    expect(suggestionValues()).toStrictEqual([])

    await userEvent.type(input, 'борода')
    await userEvent.click(screen.getByRole('button', { name: 'Найти' }))

    expect(onSearch).toHaveBeenCalledWith(
      expect.objectContaining({ additionalService: 'борода' })
    )
  })
})

describe('SearchForm, группы специализаций', () => {
  it('строит шесть optgroup в порядке категорий с переведёнными подписями', async () => {
    useCatalogs()

    render(<SearchForm isSearching={false} onSearch={vi.fn()} />)

    const select = await screen.findByLabelText('Специализация')

    // До ответа справочника селект показывает одну опцию «Загрузка…», групп ещё нет.
    await waitFor(() => {
      expect(within(select).getAllByRole('group')).toHaveLength(6)
    })

    // Подписи — `SERVICE_CATEGORY_LABELS` через `t()`, а не имена enum из API;
    // порядок групп задаёт `SERVICE_CATEGORY_ORDER`, а не порядок ответа.
    expect(
      within(select)
        .getAllByRole('group')
        .map((group) => ({
          label: group.getAttribute('label'),
          options: Array.from(group.querySelectorAll('option')).map(
            (option) => option.textContent
          ),
        }))
    ).toStrictEqual([
      { label: 'Стрижка', options: ['Барберинг'] },
      { label: 'Борода', options: ['Оформление бороды'] },
      { label: 'Окрашивание', options: ['Окрашивание'] },
      { label: 'Укладка', options: ['Укладка'] },
      { label: 'Уход', options: ['Уход за волосами'] },
      { label: 'Другое', options: ['Бритьё головы'] },
    ])
  })

  it('оставляет «Любая специализация» первой и вне групп, не теряя id опций', async () => {
    useCatalogs()

    render(<SearchForm isSearching={false} onSearch={vi.fn()} />)

    const select = await screen.findByLabelText('Специализация')

    await waitFor(() => {
      expect(select.querySelectorAll('optgroup')).toHaveLength(6)
    })

    // Первый элемент селекта — сама опция, а не `optgroup`.
    const options = Array.from(select.querySelectorAll('option'))
    const first = options[0]

    expect(first).toBe(select.firstElementChild)
    expect(first.value).toBe('')
    expect(first).toHaveTextContent('Любая специализация')
    expect(first.closest('optgroup')).toBeNull()

    // Ни одна строка справочника не потерялась: id и подписи те же, что в ответе.
    const namesById = new Map(
      SPECIALIZATIONS.map((item) => [item.id, item.name])
    )
    const groupedOptions = options.slice(1)

    expect(groupedOptions.map((option) => option.value).sort()).toStrictEqual(
      SPECIALIZATIONS.map((item) => item.id).sort()
    )
    expect(
      new Map(
        groupedOptions.map((option) => [
          option.value,
          option.textContent?.trim() ?? '',
        ])
      )
    ).toStrictEqual(namesById)
  })
})

const CITY_SUGGESTIONS: CitySuggestion[] = [
  {
    providerId: 'e2e-city-ekb',
    display: 'г. Екатеринбург',
    city: 'Екатеринбург',
    region: 'Свердловская обл.',
    country: 'Россия',
    latitude: 56.8389,
    longitude: 60.5975,
  },
]

function serveCities(items: readonly CitySuggestion[] = CITY_SUGGESTIONS): void {
  server.use(http.get('/api/addresses/cities', () => HttpResponse.json(items)))
}

/**
 * Поле города — серверный combobox: в поле пишется голое имя города (`city`), а не
 * подпись подсказки. Это контракт фильтра: сервер сравнивает `salon.City` точно.
 */
describe('SearchForm, подсказки города', () => {
  it('выбор подсказки подставляет город и уходит на сервер точным названием', async () => {
    useCatalogs()
    serveCities()

    const onSearch = vi.fn()

    render(<SearchForm isSearching={false} onSearch={onSearch} />)

    const input = screen.getByLabelText('Город салона')

    await userEvent.type(input, 'Екат')

    const option = await screen.findByRole('option', {
      name: 'г. Екатеринбург',
    })

    await userEvent.click(option)

    expect(input).toHaveValue('Екатеринбург')

    await userEvent.click(screen.getByRole('button', { name: 'Найти' }))

    expect(onSearch).toHaveBeenCalledTimes(1)
    expect(onSearch).toHaveBeenCalledWith(
      expect.objectContaining({ city: 'Екатеринбург' })
    )
  })

  it('Enter при открытом списке выбирает город и не отправляет форму', async () => {
    useCatalogs()
    serveCities()

    const onSearch = vi.fn()

    render(<SearchForm isSearching={false} onSearch={onSearch} />)

    const input = screen.getByLabelText('Город салона')

    await userEvent.type(input, 'Ек')
    await screen.findByRole('option', { name: 'г. Екатеринбург' })

    await userEvent.keyboard('{ArrowDown}')

    // jsdom не отправляет форму по Enter, поэтому проверяем сам контракт: combobox
    // гасит Enter, пока список открыт, иначе браузер отправил бы форму вместе с выбором.
    const form = input.closest('form')
    let enterEvent: KeyboardEvent | null = null

    form?.addEventListener('keydown', (event) => {
      if (event.key === 'Enter') {
        enterEvent = event
      }
    })

    await userEvent.keyboard('{Enter}')

    expect(enterEvent).not.toBeNull()
    expect((enterEvent as KeyboardEvent | null)?.defaultPrevented).toBe(true)

    expect(onSearch).not.toHaveBeenCalled()
    expect(input).toHaveValue('Екатеринбург')
    await waitFor(() => {
      expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    })
  })

  it('401 анониму тихий: подсказок нет, свободный ввод и поиск работают', async () => {
    useCatalogs()
    server.use(
      http.get('/api/addresses/cities', () =>
        HttpResponse.json(
          problemDetails(401, { errorCodes: ['auth.required'] }),
          { status: 401 }
        )
      )
    )

    const onSearch = vi.fn()
    const requests = recordRequests()

    render(<SearchForm isSearching={false} onSearch={onSearch} />)

    const input = screen.getByLabelText('Город салона')

    await userEvent.type(input, 'Канзас-Сити')

    // Ждём сам запрос и ответ: только тогда видно, что 401 не открыл ни списка, ни ошибки.
    await waitFor(() => {
      expect(requests).toContain('/api/addresses/cities')
    })
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 400))
    })

    // Список не открывается и ошибка не показывается: форма — не обязана подсказывать.
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Найти' }))

    expect(onSearch).toHaveBeenCalledWith(
      expect.objectContaining({ city: 'Канзас-Сити' })
    )
  })
})
