import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { SearchForm } from '@/components/home/search-form'
import type { CitySuggestion } from '@/lib/address-suggestions'
import { problemDetails } from '@/test/msw/handlers'
import { recordRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const SPECIALIZATION = {
  id: '11111111-1111-4111-8111-111111111111',
  name: 'Барберинг',
  description: null,
  category: 'Haircut',
}

const BEARD_SPECIALIZATION = {
  id: '33333333-3333-4333-8333-333333333333',
  name: 'Борода',
  description: null,
  category: 'Beard',
}

const STYLING_SPECIALIZATION = {
  id: '88888888-8888-4888-8888-888888888888',
  name: 'Укладка',
  description: null,
  category: 'Styling',
}

const OTHER_SPECIALIZATION = {
  id: '99999999-9999-4999-8999-999999999999',
  name: 'Афрокудри',
  description: null,
  category: 'Other',
}

const HAIR_SERVICE = {
  id: '22222222-2222-4222-8222-222222222222',
  name: 'Стрижка',
  category: 'Haircut',
  defaultDurationMinutes: 45,
  specializationId: SPECIALIZATION.id,
}

const HAIR_SERVICE_FADE = {
  id: '44444444-4444-4444-8444-444444444444',
  name: 'Фейд',
  category: 'Haircut',
  defaultDurationMinutes: 45,
  specializationId: SPECIALIZATION.id,
}

const HAIR_SERVICE_BEARD = {
  id: '55555555-5555-4555-8555-555555555555',
  name: 'Оформление бороды',
  category: 'Beard',
  defaultDurationMinutes: 30,
  specializationId: BEARD_SPECIALIZATION.id,
}

/** Специализация этой услуги выключена и в публичный список не попала. */
const HAIR_SERVICE_WITHOUT_SPECIALIZATION = {
  id: '66666666-6666-4666-8666-666666666666',
  name: 'Бритьё головы',
  category: 'Other',
  defaultDurationMinutes: 20,
  specializationId: '77777777-7777-4777-8777-777777777777',
}

const ADDITIONAL_SERVICES = ['Оформление бороды', 'Бритьё головы']

function useCatalogs() {
  server.use(
    http.get('/api/specializations', () => HttpResponse.json([SPECIALIZATION])),
    http.get('/api/hair-services', () => HttpResponse.json([HAIR_SERVICE])),
    http.get('/api/barbers/additional-services', () =>
      HttpResponse.json(ADDITIONAL_SERVICES)
    )
  )
}

/** Справочники для проверки групп: две специализации и услуга без пары. */
function useGroupedCatalogs() {
  server.use(
    http.get('/api/specializations', () =>
      HttpResponse.json([SPECIALIZATION, BEARD_SPECIALIZATION])
    ),
    http.get('/api/hair-services', () =>
      HttpResponse.json([
        HAIR_SERVICE_FADE,
        HAIR_SERVICE,
        HAIR_SERVICE_BEARD,
        HAIR_SERVICE_WITHOUT_SPECIALIZATION,
      ])
    ),
    http.get('/api/barbers/additional-services', () =>
      HttpResponse.json(ADDITIONAL_SERVICES)
    )
  )
}

/**
 * Справочники для группировки специализаций: категории приходят вперемешку,
 * поэтому группы обязаны выстроиться по каталогу, а не по ответу API.
 */
function useGroupedSpecializationCatalogs() {
  server.use(
    http.get('/api/specializations', () =>
      HttpResponse.json([
        OTHER_SPECIALIZATION,
        BEARD_SPECIALIZATION,
        SPECIALIZATION,
        STYLING_SPECIALIZATION,
      ])
    ),
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

    const input = screen.getByLabelText('Город')

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

    const input = screen.getByLabelText('Город')

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

    const input = screen.getByLabelText('Город')

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

/**
 * Услуги сгруппированы по специализациям: нативный `<optgroup>` легче
 * просмотреть, чем плоский список из десятков строк. Выбранное значение —
 * по-прежнему id услуги, а «Любая услуга» стоит вне групп.
 */
describe('SearchForm, группировка услуг', () => {
  it('раскладывает услуги по <optgroup>: группы по алфавиту, строки в порядке API', async () => {
    useGroupedCatalogs()

    render(<SearchForm isSearching={false} onSearch={vi.fn()} />)

    const select = screen.getByLabelText('Услуга')

    await waitFor(() => {
      expect(within(select).getAllByRole('group')).toHaveLength(3)
    })

    const groups = within(select).getAllByRole('group')

    // Группы идут по алфавиту названий специализаций, «Прочие услуги» — последней.
    expect(groups.map((group) => group.getAttribute('label'))).toStrictEqual([
      'Барберинг',
      'Борода',
      'Прочие услуги',
    ])

    const labelsIn = (label: string): (string | null)[] =>
      within(within(select).getByRole('group', { name: label }))
        .getAllByRole('option')
        .map((option) => option.textContent)

    expect(labelsIn('Барберинг')).toStrictEqual(['Фейд', 'Стрижка'])
    expect(labelsIn('Борода')).toStrictEqual(['Оформление бороды'])
    expect(labelsIn('Прочие услуги')).toStrictEqual(['Бритьё головы'])

    const options = within(select).getAllByRole('option')

    // Первый пункт — «Любая услуга», и он не входит ни в одну группу.
    expect(options[0]).toHaveTextContent('Любая услуга')
    expect(options[0].closest('optgroup')).toBeNull()
    expect(options.map((option) => option.getAttribute('value'))).toStrictEqual([
      '',
      HAIR_SERVICE_FADE.id,
      HAIR_SERVICE.id,
      HAIR_SERVICE_BEARD.id,
      HAIR_SERVICE_WITHOUT_SPECIALIZATION.id,
    ])
  })

  it('выбор услуги из группы отправляет её id и не теряет title', async () => {
    useGroupedCatalogs()

    const onSearch = vi.fn()

    render(<SearchForm isSearching={false} onSearch={onSearch} />)

    const select = screen.getByLabelText('Услуга')

    await waitFor(() => {
      expect(within(select).getAllByRole('group')).toHaveLength(3)
    })

    await userEvent.selectOptions(select, HAIR_SERVICE_BEARD.id)

    expect(select).toHaveValue(HAIR_SERVICE_BEARD.id)
    expect(select).toHaveAttribute('title', 'Оформление бороды')

    await userEvent.click(screen.getByRole('button', { name: 'Найти' }))

    expect(onSearch).toHaveBeenCalledTimes(1)
    expect(onSearch).toHaveBeenCalledWith(
      expect.objectContaining({ hairServiceId: HAIR_SERVICE_BEARD.id })
    )
  })

  it('услуга без специализации попадает в хвостовую группу «Прочие услуги»', async () => {
    server.use(
      http.get('/api/specializations', () =>
        HttpResponse.json([SPECIALIZATION])
      ),
      // Незнакомая услуга идёт первой: группа «Прочие услуги» всё равно последняя.
      http.get('/api/hair-services', () =>
        HttpResponse.json([
          HAIR_SERVICE_WITHOUT_SPECIALIZATION,
          HAIR_SERVICE,
        ])
      ),
      http.get('/api/barbers/additional-services', () =>
        HttpResponse.json(ADDITIONAL_SERVICES)
      )
    )

    const onSearch = vi.fn()

    render(<SearchForm isSearching={false} onSearch={onSearch} />)

    const select = screen.getByLabelText('Услуга')

    await waitFor(() => {
      expect(within(select).getAllByRole('group')).toHaveLength(2)
    })

    const groups = within(select).getAllByRole('group')

    expect(groups.map((group) => group.getAttribute('label'))).toStrictEqual([
      'Барберинг',
      'Прочие услуги',
    ])
    expect(
      within(groups[1])
        .getAllByRole('option')
        .map((option) => option.textContent)
    ).toStrictEqual(['Бритьё головы'])

    await userEvent.selectOptions(
      select,
      HAIR_SERVICE_WITHOUT_SPECIALIZATION.id
    )
    await userEvent.click(screen.getByRole('button', { name: 'Найти' }))

    expect(onSearch).toHaveBeenCalledWith(
      expect.objectContaining({
        hairServiceId: HAIR_SERVICE_WITHOUT_SPECIALIZATION.id,
      })
    )
  })
})

/**
 * Специализации сгруппированы по категориям: заголовок `optgroup` — название
 * категории в каталожном порядке, «Любая специализация» стоит вне групп,
 * а выбранное значение — по-прежнему id специализации.
 */
describe('SearchForm, группировка специализаций', () => {
  it('раскладывает специализации по <optgroup>: группы по каталогу, строки в порядке API', async () => {
    useGroupedSpecializationCatalogs()

    render(<SearchForm isSearching={false} onSearch={vi.fn()} />)

    const select = screen.getByLabelText('Специализация')

    await waitFor(() => {
      expect(within(select).getAllByRole('group')).toHaveLength(4)
    })

    const groups = within(select).getAllByRole('group')

    // Категории идут как в каталоге, хотя API вернул их вперемешку.
    // Coloring и HairTreatment на входе нет — пустых групп тоже нет.
    expect(groups.map((group) => group.getAttribute('label'))).toStrictEqual([
      'Стрижка',
      'Борода',
      'Укладка',
      'Другое',
    ])

    const labelsIn = (label: string): (string | null)[] =>
      within(within(select).getByRole('group', { name: label }))
        .getAllByRole('option')
        .map((option) => option.textContent)

    expect(labelsIn('Стрижка')).toStrictEqual(['Барберинг'])
    expect(labelsIn('Борода')).toStrictEqual(['Борода'])
    expect(labelsIn('Укладка')).toStrictEqual(['Укладка'])
    expect(labelsIn('Другое')).toStrictEqual(['Афрокудри'])

    const options = within(select).getAllByRole('option')

    // Первый пункт — «Любая специализация», и он не входит ни в одну группу.
    expect(options[0]).toHaveTextContent('Любая специализация')
    expect(options[0].closest('optgroup')).toBeNull()
    expect(options.map((option) => option.getAttribute('value'))).toStrictEqual([
      '',
      SPECIALIZATION.id,
      BEARD_SPECIALIZATION.id,
      STYLING_SPECIALIZATION.id,
      OTHER_SPECIALIZATION.id,
    ])
  })

  it('выбор специализации из группы отправляет её id и не теряет title', async () => {
    useGroupedSpecializationCatalogs()

    const onSearch = vi.fn()

    render(<SearchForm isSearching={false} onSearch={onSearch} />)

    const select = screen.getByLabelText('Специализация')

    await waitFor(() => {
      expect(within(select).getAllByRole('group')).toHaveLength(4)
    })

    await userEvent.selectOptions(select, STYLING_SPECIALIZATION.id)

    expect(select).toHaveValue(STYLING_SPECIALIZATION.id)
    expect(select).toHaveAttribute('title', 'Укладка')

    await userEvent.click(screen.getByRole('button', { name: 'Найти' }))

    expect(onSearch).toHaveBeenCalledTimes(1)
    expect(onSearch).toHaveBeenCalledWith(
      expect.objectContaining({
        specializationId: STYLING_SPECIALIZATION.id,
      })
    )
  })
})

/**
 * Специализация сужает селект услуг: услуга принадлежит ровно одной
 * специализации, поэтому чужие строки в «Услуге» — ложный выбор. Старый выбор,
 * выпавший из сужённого списка, сбрасывается, а не остаётся скрытым значением.
 */
describe('SearchForm, услуги выбранной специализации', () => {
  it('оставляет в «Услуге» только услуги выбранной специализации', async () => {
    useGroupedCatalogs()

    render(<SearchForm isSearching={false} onSearch={vi.fn()} />)

    const specializationSelect = screen.getByLabelText('Специализация')
    const serviceSelect = screen.getByLabelText('Услуга')

    await waitFor(() => {
      expect(within(serviceSelect).getAllByRole('group')).toHaveLength(3)
    })

    await userEvent.selectOptions(specializationSelect, BEARD_SPECIALIZATION.id)

    const groups = within(serviceSelect).getAllByRole('group')

    expect(groups.map((group) => group.getAttribute('label'))).toStrictEqual([
      'Борода',
    ])
    expect(
      within(groups[0])
        .getAllByRole('option')
        .map((option) => option.textContent)
    ).toStrictEqual(['Оформление бороды'])
    // «Любая услуга» остаётся первым пунктом и вне групп, «Прочих услуг» нет.
    expect(
      within(serviceSelect)
        .getAllByRole('option')
        .map((option) => option.getAttribute('value'))
    ).toStrictEqual(['', HAIR_SERVICE_BEARD.id])
  })

  it('сбрасывает услугу другой специализации и отправляет пустой hairServiceId', async () => {
    useGroupedCatalogs()

    const onSearch = vi.fn()

    render(<SearchForm isSearching={false} onSearch={onSearch} />)

    const specializationSelect = screen.getByLabelText('Специализация')
    const serviceSelect = screen.getByLabelText('Услуга')

    await waitFor(() => {
      expect(within(serviceSelect).getAllByRole('group')).toHaveLength(3)
    })

    await userEvent.selectOptions(serviceSelect, HAIR_SERVICE_BEARD.id)
    expect(serviceSelect).toHaveValue(HAIR_SERVICE_BEARD.id)

    await userEvent.selectOptions(specializationSelect, SPECIALIZATION.id)

    expect(serviceSelect).toHaveValue('')
    expect(serviceSelect).not.toHaveAttribute('title')

    await userEvent.click(screen.getByRole('button', { name: 'Найти' }))

    expect(onSearch).toHaveBeenCalledTimes(1)
    expect(onSearch).toHaveBeenCalledWith(
      expect.objectContaining({
        specializationId: SPECIALIZATION.id,
        hairServiceId: '',
      })
    )
  })

  it('возврат к «Любой специализации» показывает все группы и сохраняет услугу', async () => {
    useGroupedCatalogs()

    render(<SearchForm isSearching={false} onSearch={vi.fn()} />)

    const specializationSelect = screen.getByLabelText('Специализация')
    const serviceSelect = screen.getByLabelText('Услуга')

    await waitFor(() => {
      expect(within(serviceSelect).getAllByRole('group')).toHaveLength(3)
    })

    await userEvent.selectOptions(specializationSelect, BEARD_SPECIALIZATION.id)
    await userEvent.selectOptions(serviceSelect, HAIR_SERVICE_BEARD.id)

    await userEvent.selectOptions(specializationSelect, '')

    expect(within(serviceSelect).getAllByRole('group')).toHaveLength(3)
    expect(serviceSelect).toHaveValue(HAIR_SERVICE_BEARD.id)
  })

  it('у специализации без услуг селект остаётся с одной «Любой услугой»', async () => {
    useGroupedSpecializationCatalogs()

    render(<SearchForm isSearching={false} onSearch={vi.fn()} />)

    const specializationSelect = screen.getByLabelText('Специализация')
    const serviceSelect = screen.getByLabelText('Услуга')

    await waitFor(() => {
      expect(within(serviceSelect).getAllByRole('group')).toHaveLength(1)
    })

    await userEvent.selectOptions(specializationSelect, STYLING_SPECIALIZATION.id)

    expect(within(serviceSelect).queryAllByRole('group')).toHaveLength(0)
    expect(within(serviceSelect).getAllByRole('option')).toHaveLength(1)
    expect(
      within(serviceSelect).getByRole('option', { name: 'Любая услуга' })
    ).toBeInTheDocument()
  })
})
