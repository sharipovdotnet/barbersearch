import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { BarberPage } from '@/pages/barber-page'
import { testBarberProfile } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Порядок секций публичной карточки. Отзывов у профиля в этом тесте нет,
 * поэтому страница не делает лишних запросов и проверяется на общих
 * MSW-обработчиках — без отдельной сети и тяжёлой обвязки.
 */
describe('BarberPage: порядок секций', () => {
  it('ставит «Специализации» между «О мастере» и «Услугами»', async () => {
    server.use(
      http.get('/api/barbers/:id', () =>
        HttpResponse.json({ ...testBarberProfile, reviewsCount: 0 })
      )
    )

    render(<BarberPage id={testBarberProfile.id} />)

    // Ждём готовый профиль: до ответа секций ещё нет.
    expect(
      await screen.findByRole('region', { name: 'Специализации' })
    ).toBeInTheDocument()

    const names = screen
      .getAllByRole('region')
      .map((region) => region.querySelector('h2')?.textContent)

    expect(names).toStrictEqual([
      'Свободное время',
      'О мастере',
      'Специализации',
      'Услуги',
      'Отзывы',
      'Салоны',
    ])
  })
})
