import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import { searchBarberClients } from '@/lib/barber-clients'
import { server } from '@/test/msw/server'

const CLIENT = {
  id: '22222222-2222-4222-8222-222222222222',
  firstName: 'Иван',
  lastName: 'Петров',
  email: 'client@example.com',
  phoneNumber: '+7 999 000-00-00',
}

describe('searchBarberClients', () => {
  it('кодирует строку и возвращает проверенный список клиентов', async () => {
    const urls: string[] = []

    server.use(
      http.get('/api/barber/clients', ({ request }) => {
        urls.push(request.url)

        return HttpResponse.json([CLIENT])
      })
    )

    const clients = await searchBarberClients(' client@example.com ')

    expect(clients).toStrictEqual([CLIENT])
    expect(urls).toHaveLength(1)
    expect(new URL(urls[0]).searchParams.get('search')).toBe(
      ' client@example.com '
    )
  })

  it('отвергает битый ответ и не отдаёт его в форму', async () => {
    server.use(
      http.get('/api/barber/clients', () =>
        HttpResponse.json([{ ...CLIENT, id: '' }])
      )
    )

    await expect(searchBarberClients('client')).rejects.toBeInstanceOf(
      ApiError
    )
  })
})
