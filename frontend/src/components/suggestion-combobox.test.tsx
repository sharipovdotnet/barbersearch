import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'

import { SuggestionCombobox } from '@/components/suggestion-combobox'
import {
  CITY_SUGGESTIONS_MIN_LENGTH,
  fetchCitySuggestions,
  SUGGESTIONS_DEBOUNCE_MS,
  type CitySuggestion,
} from '@/lib/address-suggestions'
import { problemDetails } from '@/test/msw/handlers'
import { recordRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const CITIES: CitySuggestion[] = [
  {
    providerId: 'p-1',
    display: 'г. Екатеринбург',
    city: 'Екатеринбург',
    region: 'Свердловская обл.',
    country: 'Россия',
    latitude: 56.8389,
    longitude: 60.5975,
  },
  {
    providerId: 'p-2',
    display: 'г. Екатеринбург, пос. Исток',
    city: 'Екатеринбург',
    region: 'Свердловская обл.',
    country: 'Россия',
    latitude: 56.9,
    longitude: 60.7,
  },
]

function serveCities(items: readonly CitySuggestion[] = CITIES): void {
  server.use(http.get('/api/addresses/cities', () => HttpResponse.json(items)))
}

function serveFailure(status: number, code: string): void {
  server.use(
    http.get('/api/addresses/cities', () =>
      HttpResponse.json(problemDetails(status, { errorCodes: [code] }), {
        status,
      })
    )
  )
}

/**
 * Поле-обёртка вместо формы: выбор города пишется в `value` так же, как это делает
 * форма салона, — по `city`, а не по подписи из списка.
 */
function CityField({
  onSelect = () => {},
  enabled = true,
}: {
  onSelect?: (suggestion: CitySuggestion) => void
  enabled?: boolean
}) {
  const [value, setValue] = useState('')

  return (
    <SuggestionCombobox<CitySuggestion>
      id="salon-city"
      label="Город"
      value={value}
      onValueChange={setValue}
      onSelect={(suggestion) => {
        setValue(suggestion.city)
        onSelect(suggestion)
      }}
      getKey={(suggestion) => suggestion.providerId ?? suggestion.display}
      getLabel={(suggestion) => suggestion.display}
      fetchSuggestions={fetchCitySuggestions}
      minLength={CITY_SUGGESTIONS_MIN_LENGTH}
      listLabel="Подсказки города"
      placeholder="Начните вводить город"
      enabled={enabled}
    />
  )
}

/** Дать дебаунсу и ответу MSW отработать: таймеры настоящие, `waitFor` только читает. */
async function settle(delay = SUGGESTIONS_DEBOUNCE_MS + 150): Promise<void> {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, delay))
  })
}

const CITY_REQUESTS = '/api/addresses/cities'

describe('SuggestionCombobox: сеть', () => {
  it('один символ не ходит в сеть, двух хватает', async () => {
    const user = userEvent.setup()
    const requests = recordRequests()

    serveCities()
    render(<CityField />)

    const input = screen.getByLabelText('Город')

    await user.type(input, 'Е')
    await settle()
    expect(requests).toStrictEqual([])

    await user.type(input, 'к')
    await settle()
    expect(requests).toStrictEqual([CITY_REQUESTS])
  })

  it('три нажатия подряд дают один запрос после дебаунса', async () => {
    const user = userEvent.setup()
    const requests = recordRequests()

    serveCities()
    render(<CityField />)

    await user.type(screen.getByLabelText('Город'), 'Ека')
    await settle()

    expect(requests).toStrictEqual([CITY_REQUESTS])
  })

  it('выключенное поле не ходит в сеть и не открывает список', async () => {
    const user = userEvent.setup()
    const requests = recordRequests()

    serveCities()
    render(<CityField enabled={false} />)

    await user.type(screen.getByLabelText('Город'), 'Екатеринбург')
    await settle()

    expect(requests).toStrictEqual([])
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('размонтирование отменяет запрос без ошибки и без обновления', async () => {
    const user = userEvent.setup()
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})

    server.use(
      http.get(CITY_REQUESTS, async () => {
        await new Promise((resolve) => setTimeout(resolve, 300))

        return HttpResponse.json(CITIES)
      })
    )

    try {
      const { unmount } = render(<CityField />)

      await user.type(screen.getByLabelText('Город'), 'Ек')
      // Запрос уже ушёл, ответ ещё нет.
      await act(async () => {
        await new Promise((resolve) =>
          setTimeout(resolve, SUGGESTIONS_DEBOUNCE_MS + 20)
        )
      })

      unmount()
      await settle(400)

      expect(consoleError).not.toHaveBeenCalled()
    } finally {
      consoleError.mockRestore()
    }
  })
})

describe('SuggestionCombobox: клавиатура и состояния', () => {
  it('показывает список и выбирает подсказку стрелкой и Enter', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()

    serveCities()
    render(<CityField onSelect={onSelect} />)

    const input = screen.getByLabelText('Город')

    await user.type(input, 'Ек')
    await screen.findByRole('option', { name: 'г. Екатеринбург' })
    await screen.findByRole('listbox', { name: 'Подсказки города' })

    await user.keyboard('{ArrowDown}')

    const option = screen.getByRole('option', { name: 'г. Екатеринбург' })

    expect(option).toHaveAttribute('data-highlighted')

    await user.keyboard('{Enter}')

    expect(onSelect).toHaveBeenCalledTimes(1)
    expect(onSelect.mock.calls[0]?.[0]).toMatchObject({
      city: 'Екатеринбург',
    })
    // В поле — голое имя города, а не подпись из списка.
    expect(input).toHaveValue('Екатеринбург')
    await waitFor(() => {
      expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    })
  })

  it('выбор мышью пишет в поле город, а не подпись из списка', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()

    serveCities()
    render(<CityField onSelect={onSelect} />)

    const input = screen.getByLabelText('Город')

    await user.type(input, 'Ека')

    const option = await screen.findByRole('option', {
      name: 'г. Екатеринбург',
    })

    await user.click(option)

    expect(onSelect).toHaveBeenCalledTimes(1)
    expect(input).toHaveValue('Екатеринбург')
  })

  it('Escape закрывает список и не выбирает', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()

    serveCities()
    render(<CityField onSelect={onSelect} />)

    const input = screen.getByLabelText('Город')

    await user.type(input, 'Ек')
    await screen.findByRole('listbox', { name: 'Подсказки города' })

    await user.keyboard('{Escape}')

    await waitFor(() => {
      expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    })
    expect(onSelect).not.toHaveBeenCalled()
    expect(input).toHaveValue('Ек')
  })

  it('Tab уходит из поля без выбора и без потери текста', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()

    serveCities()
    render(<CityField onSelect={onSelect} />)

    const input = screen.getByLabelText('Город')

    await user.type(input, 'Ек')
    await screen.findByRole('listbox', { name: 'Подсказки города' })

    await user.keyboard('{Tab}')

    await waitFor(() => {
      expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    })
    expect(onSelect).not.toHaveBeenCalled()
    expect(input).toHaveValue('Ек')
  })

  it('две подсказки с одинаковой подписью не дают дублирующихся ключей', async () => {
    const user = userEvent.setup()
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})

    const duplicates: CitySuggestion[] = [
      {
        providerId: null,
        display: 'г. Казань',
        city: 'Казань',
        region: null,
        country: 'Россия',
        latitude: null,
        longitude: null,
      },
      {
        providerId: null,
        display: 'г. Казань',
        city: 'Казань',
        region: null,
        country: 'Россия',
        latitude: null,
        longitude: null,
      },
    ]

    server.use(
      http.get(CITY_REQUESTS, () => HttpResponse.json(duplicates))
    )

    try {
      render(<CityField />)

      await user.type(screen.getByLabelText('Город'), 'Каз')

      await waitFor(() => {
        expect(
          screen.getAllByRole('option', { name: 'г. Казань' })
        ).toHaveLength(2)
      })
      // React ругается в console.error на повторяющийся key: его быть не должно.
      expect(consoleError).not.toHaveBeenCalled()
    } finally {
      consoleError.mockRestore()
    }
  })

  it('пустой ответ — «Ничего не найдено.», а не ошибка поля', async () => {
    const user = userEvent.setup()

    serveCities([])
    render(<CityField />)

    await user.type(screen.getByLabelText('Город'), 'Ек')

    expect(await screen.findByText('Ничего не найдено.')).toBeInTheDocument()
  })

  it('битое тело не роняет компонент: показывается пустое состояние', async () => {
    const user = userEvent.setup()

    server.use(
      http.get(CITY_REQUESTS, () => HttpResponse.json([{ display: 'г. X' }]))
    )

    render(<CityField />)

    const input = screen.getByLabelText('Город')

    await user.type(input, 'Ек')

    expect(await screen.findByText('Ничего не найдено.')).toBeInTheDocument()
    expect(input).toHaveValue('Ек')
  })
})

describe('SuggestionCombobox: тихий отказ', () => {
  it('409 прячет подсказки до конца сессии и не блокирует поле', async () => {
    const user = userEvent.setup()
    const requests = recordRequests()

    serveFailure(409, 'addressSuggestions.unavailable')
    render(<CityField />)

    const input = screen.getByLabelText('Город')

    await user.type(input, 'Ек')

    await waitFor(() => {
      expect(requests).toStrictEqual([CITY_REQUESTS])
    })
    await settle()

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(input).toBeEnabled()
    expect(input).toHaveValue('Ек')

    // После 409 новых запросов нет: провайдер недоступен целиком.
    await user.type(input, 'атеринбург')
    await settle()

    expect(requests).toStrictEqual([CITY_REQUESTS])
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('429 не повторяет запрос автоматически', async () => {
    const user = userEvent.setup()
    const requests = recordRequests()

    serveFailure(429, 'rateLimit')
    render(<CityField />)

    const input = screen.getByLabelText('Город')

    await user.type(input, 'Ек')

    await waitFor(() => {
      expect(requests).toStrictEqual([CITY_REQUESTS])
    })

    // Ждём заметно дольше дебаунса: автоматического повтора быть не должно.
    await settle(SUGGESTIONS_DEBOUNCE_MS * 3)

    expect(requests).toStrictEqual([CITY_REQUESTS])
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()

    // Следующая правка строки — новая попытка, а не тишина до конца сессии.
    await user.type(input, 'а')
    await waitFor(() => {
      expect(requests).toStrictEqual([CITY_REQUESTS, CITY_REQUESTS])
    })
  })
})
