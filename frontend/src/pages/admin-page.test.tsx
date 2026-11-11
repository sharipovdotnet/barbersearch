import { render, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'

import {
  SessionContext,
  type SessionContextValue,
} from '@/hooks/use-session'
import type { SessionUser } from '@/lib/auth'
import { AdminPage } from '@/pages/admin-page'
import { server } from '@/test/msw/server'

/**
 * Раздел администрирования: маршрут очереди фото, заголовок вкладки и рейл.
 *
 * Экран фото живёт внутри `AdminPage` (ленивый чанк админки), поэтому проверяем
 * именно страницу: разбор `/admin/photos?tab=…`, заголовок вкладки и пункт рейла
 * после «Отзывов». Данные очереди — пустые: тест про маршрут, а не про список.
 */

const ADMIN: SessionUser = {
  id: '55555555-5555-4555-8555-555555555555',
  email: 'admin@barbersearch.test',
  role: 'Admin',
  barberProfileId: null,
  isEmailVerified: true,
}

const session: SessionContextValue = {
  status: 'authenticated',
  user: ADMIN,
  signIn: async () => ADMIN,
  signOut: async () => {},
  retrySession: () => {},
}

function renderAdminPage() {
  return render(
    <SessionContext.Provider value={session}>
      <AdminPage />
    </SessionContext.Provider>
  )
}

beforeEach(() => {
  window.history.replaceState(null, '', '/admin/photos')
  document.title = ''
})

describe('AdminPage: очередь фото', () => {
  it('открывает /admin/photos, ставит заголовок вкладки и пункт рейла', async () => {
    renderAdminPage()

    expect(
      await screen.findByRole('heading', { name: 'Фотографии', level: 1 })
    ).toBeInTheDocument()

    await waitFor(() => {
      expect(document.title).toBe('Фото — администрирование BarberSearch')
    })

    const rail = screen.getByRole('navigation', {
      name: 'Разделы администрирования',
    })
    const photoLink = within(rail).getByRole('link', { name: 'Фото' })
    const links = within(rail)
      .getAllByRole('link')
      .map((link) => link.textContent)

    expect(photoLink).toHaveAttribute('href', '/admin/photos')
    expect(photoLink).toHaveAttribute('aria-current', 'page')
    // Пункт «Фото» стоит сразу после «Отзывов».
    expect(links.indexOf('Фото')).toBe(links.indexOf('Отзывы') + 1)

    expect(screen.getByRole('tab', { name: 'Аватары' })).toHaveAttribute(
      'aria-selected',
      'true'
    )
  })

  it('читает вкладку портфолио из ?tab= и помечает её выбранной', async () => {
    window.history.replaceState(null, '', '/admin/photos?tab=portfolio')

    const requests: string[] = []

    server.use(
      http.get('/api/admin/photos/portfolio', ({ request }) => {
        requests.push(new URL(request.url).pathname)

        return HttpResponse.json({ items: [], nextCursor: null })
      })
    )

    renderAdminPage()

    expect(
      await screen.findByRole('tab', { name: 'Портфолио' })
    ).toHaveAttribute('aria-selected', 'true')

    await waitFor(() => {
      expect(requests).toContain('/api/admin/photos/portfolio')
    })

    await waitFor(() => {
      expect(document.title).toBe('Фото — администрирование BarberSearch')
    })
  })
})
