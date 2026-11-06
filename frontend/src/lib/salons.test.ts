import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  fetchSalonDirectory,
  salonDirectoryUrl,
  SALON_SEARCH_MIN_LENGTH,
} from '@/lib/salons'
import { server } from '@/test/msw/server'

const SALON = {
  id: '22222222-2222-4222-8222-222222222222',
  name: 'Барбершоп на Ленина',
  city: 'Екатеринбург',
  address: 'ул. Ленина, 1',
  timeZoneId: 'Asia/Yekaterinburg',
}

describe('salonDirectoryUrl', () => {
  it('без фильтров отдаёт голый путь', () => {
    expect(salonDirectoryUrl()).toBe('/salons')
    expect(salonDirectoryUrl({ city: '  ', search: '' })).toBe('/salons')
  })

  it('тримит и кодирует фильтры', () => {
    expect(salonDirectoryUrl({ city: '  Екатеринбург  ' })).toBe(
      '/salons?city=%D0%95%D0%BA%D0%B0%D1%82%D0%B5%D1%80%D0%B8%D0%BD%D0%B1%D1%83%D1%80%D0%B3'
    )
    expect(salonDirectoryUrl({ search: 'Ленина 1' })).toBe(
      '/salons?search=%D0%9B%D0%B5%D0%BD%D0%B8%D0%BD%D0%B0+1'
    )
    expect(salonDirectoryUrl({ city: 'Berlin', search: 'Fade' })).toBe(
      '/salons?city=Berlin&search=Fade'
    )
  })
})

describe('fetchSalonDirectory', () => {
  it('читает массив и передаёт фильтры в URL', async () => {
    const urls: string[] = []

    server.use(
      http.get('/api/salons', ({ request }) => {
        urls.push(request.url)

        return HttpResponse.json([{ ...SALON, extra: 'ignored' }])
      })
    )

    const salons = await fetchSalonDirectory({
      city: ' Екатеринбург ',
      search: 'Ленина',
    })

    expect(salons).toHaveLength(1)
    expect(salons[0]).toMatchObject(SALON)
    expect(urls).toHaveLength(1)

    const url = new URL(urls[0]!)

    expect(url.searchParams.get('city')).toBe('Екатеринбург')
    expect(url.searchParams.get('search')).toBe('Ленина')
  })

  it('отвергает битое тело строгим guard-ом', async () => {
    server.use(
      http.get('/api/salons', () =>
        HttpResponse.json([{ id: SALON.id, name: SALON.name }])
      )
    )

    await expect(fetchSalonDirectory({ search: 'Fa' })).rejects.toBeInstanceOf(
      ApiError
    )
  })

  it('не отправляет короткий поиск сам по себе — это забота экрана', () => {
    // Минимум символов экспортируется, чтобы диалог и тест не расходились.
    expect(SALON_SEARCH_MIN_LENGTH).toBe(2)
  })
})
