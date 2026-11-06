import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { SearchForm } from '@/components/home/search-form'
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
