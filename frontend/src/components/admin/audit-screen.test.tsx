import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { AuditScreen } from '@/components/admin/audit-screen'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Экран журнала: загрузка, смена фильтра и состояния.
 *
 * Сеть — только MSW на границе HTTP. Проверяем обещания экрана, которые не видит
 * unit-тест транспорта: строки списка, смену типа сущности (новая первая страница)
 * и тексты пустого/ошибочного состояния. Русская копия сравнивается целиком.
 */

const ENTRIES = {
  all: [
    {
      id: '9e700000-0000-4000-8000-000000000021',
      occurredAt: '2026-09-10T10:21:00+00:00',
      action: 'user.updated',
      entityType: 'user',
      entityId: '9e000000-0000-4000-8000-000000000002',
      actor: null,
      changes: [{ field: 'isActive', from: true, to: false }],
    },
    {
      id: '9e700000-0000-4000-8000-000000000020',
      occurredAt: '2026-09-10T10:20:00+00:00',
      action: 'user.updated',
      entityType: 'user',
      entityId: '9e000000-0000-4000-8000-000000000002',
      actor: {
        id: '9e000000-0000-4000-8000-000000000001',
        email: 'e2e-admin@barbersearch.test',
      },
      changes: [{ field: 'role', from: 'Customer', to: 'Admin' }],
    },
  ],
  review: [
    {
      id: '9e700000-0000-4000-8000-000000000018',
      occurredAt: '2026-09-10T10:18:00+00:00',
      action: 'review.updated',
      entityType: 'review',
      entityId: '9e800000-0000-4000-8000-000000000001',
      actor: {
        id: '9e000000-0000-4000-8000-000000000001',
        email: 'e2e-admin@barbersearch.test',
      },
      changes: [{ field: 'isPublished', from: false, to: true }],
    },
  ],
}

type Entry = (typeof ENTRIES)['all'][number]

/** Ответ списка с запоминанием параметров каждого запроса. */
function mockAuditPage(entries: Entry[]): URLSearchParams[] {
  const requests: URLSearchParams[] = []

  server.use(
    http.get('/api/admin/audit', ({ request }) => {
      requests.push(new URL(request.url).searchParams)

      return HttpResponse.json({ items: entries, nextCursor: 'cursor-2' })
    })
  )

  return requests
}

describe('AuditScreen', () => {
  it('загружает строки, заголовок, подпись и диапазон пейджера', async () => {
    mockAuditPage(ENTRIES.all)

    render(<AuditScreen />)

    expect(
      screen.getByRole('heading', { name: 'Журнал', level: 1 })
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'История изменений, которую пишет сервер. Записи не редактируются.'
      )
    ).toBeInTheDocument()

    const list = await screen.findByRole('list', { name: 'Записи журнала' })

    expect(within(list).getAllByRole('listitem')).toHaveLength(2)
    expect(within(list).getByText('Система')).toBeInTheDocument()

    const pager = screen.getByRole('navigation', { name: 'Страницы журнала' })

    expect(within(pager).getByText('1–2')).toBeInTheDocument()
  })

  it('смена типа сущности начинает первую страницу с новым фильтром', async () => {
    const user = userEvent.setup()
    const requests: URLSearchParams[] = []

    server.use(
      http.get('/api/admin/audit', ({ request }) => {
        const params = new URL(request.url).searchParams
        requests.push(params)

        return HttpResponse.json({
          items:
            params.get('entityType') === 'review'
              ? ENTRIES.review
              : ENTRIES.all,
          nextCursor: null,
        })
      })
    )

    render(<AuditScreen />)

    await screen.findByRole('list', { name: 'Записи журнала' })

    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Тип сущности' }),
      'review'
    )

    await waitFor(() => {
      expect(requests.at(-1)?.get('entityType')).toBe('review')
    })

    // Список перемонтируется на время загрузки: ищем его заново, а не держим
    // ссылку на старый `<ul>`.
    await waitFor(() => {
      const list = screen.getByRole('list', { name: 'Записи журнала' })

      expect(within(list).getByText('Отзыв')).toBeInTheDocument()
    })

    // Курсор прошлой страницы не переиспользуется: фильтр начинает с начала.
    expect(requests.at(-1)?.has('cursor')).toBe(false)
  })

  it('пустой ответ без фильтров показывает «Журнал пуст»', async () => {
    mockAuditPage([])

    render(<AuditScreen />)

    expect(await screen.findByText('Журнал пуст')).toBeInTheDocument()
    expect(
      screen.queryByRole('list', { name: 'Записи журнала' })
    ).not.toBeInTheDocument()
  })

  it('403 показывает текст про роль, а не английский detail', async () => {
    server.use(
      http.get('/api/admin/audit', () =>
        HttpResponse.json(
          problemDetails(403, {
            detail: 'Forbidden.',
            errorCodes: ['auth.forbidden'],
          }),
          { status: 403 }
        )
      )
    )

    render(<AuditScreen />)

    expect(
      await screen.findByText('Раздел доступен только администратору.')
    ).toBeInTheDocument()
    expect(screen.getByText('Не удалось загрузить журнал')).toBeInTheDocument()
    expect(screen.queryByText('Forbidden.')).not.toBeInTheDocument()
  })
})
