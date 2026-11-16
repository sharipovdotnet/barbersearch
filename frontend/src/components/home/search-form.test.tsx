import { act, render, screen, waitFor } from '@testing-library/react'
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
}

const HAIR_SERVICE = {
  id: '22222222-2222-4222-8222-222222222222',
  name: 'Стрижка',
  category: 'Haircut',
  defaultDurationMinutes: 45,
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
