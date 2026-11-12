import { render, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse, type JsonBodyType } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'

import { AdminQueuesProvider } from '@/components/admin/admin-queues-provider'
import { AdminRail } from '@/components/admin/admin-rail'
import { server } from '@/test/msw/server'

/**
 * Счётчики рейла «Разделы».
 *
 * Числа приходят из снимка сводки, поэтому тест подменяет только её ответ: рейл
 * не считает ничего сам. Проверяем обещания бейджа: цифра живёт внутри круга,
 * при нуле бейджа нет, кап «99+», имя ссылки содержит точное число, активный
 * пункт не теряет счётчик, `href` не меняется.
 */

const SUMMARY_URL = '/api/admin/summary'

/** Семь пунктов со счётчиками: 12 / 5 / 4 / 8 / 3 / 2 / 99+. */
const COUNTS = {
  reviews: { pendingCount: 12, oldest: null },
  barbers: { unverifiedCount: 4 },
  salons: { inactiveCount: 7, pendingModerationCount: 2 },
  photos: { pendingAvatarsCount: 2, pendingPortfolioCount: 3 },
  users: { unverifiedCount: 8 },
  barberRequests: { pendingCount: 3 },
  bookings: { confirmedCount: 140 },
}

const EMPTY = {
  reviews: { pendingCount: 0, oldest: null },
  barbers: { unverifiedCount: 0 },
  salons: { inactiveCount: 0, pendingModerationCount: 0 },
  photos: { pendingAvatarsCount: 0, pendingPortfolioCount: 0 },
  users: { unverifiedCount: 0 },
  barberRequests: { pendingCount: 0 },
  bookings: { confirmedCount: 0 },
}

function serveSummary(body: JsonBodyType) {
  server.use(http.get(SUMMARY_URL, () => HttpResponse.json(body)))
}

function renderRail() {
  return render(
    <AdminQueuesProvider>
      <AdminRail />
    </AdminQueuesProvider>
  )
}

function rail(): HTMLElement {
  return screen.getByRole('navigation', { name: 'Разделы администрирования' })
}

/** Сам круг с цифрой: единственный `aria-hidden` внутри ссылки. */
function badgeOf(link: HTMLElement): HTMLElement | null {
  return link.querySelector<HTMLElement>('span[aria-hidden="true"]')
}

/**
 * Пункт по видимой подписи.
 *
 * Имя ссылки при `count > 0` — это `aria-label` с числом, поэтому подпись пункта
 * ищется как префикс имени: `Фото` находит и «Фото, на модерации: 5…», и голое
 * «Фото», когда счётчика нет.
 */
function labelPattern(label: string): RegExp {
  return new RegExp(
    `^${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(,|$)`
  )
}

async function link(label: string): Promise<HTMLElement> {
  return within(rail()).findByRole('link', { name: labelPattern(label) })
}

/** Пункт по точному имени: так проверяется сам текст `aria-label`. */
async function exactLink(name: string): Promise<HTMLElement> {
  return within(rail()).findByRole('link', { name })
}

beforeEach(() => {
  window.history.replaceState(null, '', '/admin/photos')
})

describe('AdminRail: счётчики очередей', () => {
  it('рисует цифру внутри круга у семи пунктов', async () => {
    serveSummary(COUNTS)

    renderRail()

    const expected: readonly [string, string][] = [
      ['Отзывы', '12'],
      ['Фото', '5'],
      ['Барберы', '4'],
      ['Пользователи', '8'],
      ['Заявки', '3'],
      ['Каталог', '2'],
      ['Брони', '99+'],
    ]

    for (const [name, value] of expected) {
      const item = await link(name)
      const badge = badgeOf(item)

      expect(badge, name).not.toBeNull()
      // Цифра именно в самом бейдже, а не рядом с ним.
      expect(badge?.textContent, name).toBe(value)
    }
  })

  it('не рисует бейджи у «Сводки», «Статистики» и «Журнала»', async () => {
    serveSummary(COUNTS)

    renderRail()

    for (const name of ['Сводка', 'Статистика', 'Журнал']) {
      const item = await link(name)

      expect(badgeOf(item), name).toBeNull()
    }
  })

  it('при нуле бейджа нет и имени с числом тоже', async () => {
    serveSummary(EMPTY)

    renderRail()

    const reviews = await link('Отзывы')

    expect(badgeOf(reviews)).toBeNull()
    expect(reviews).not.toHaveAttribute('aria-label')
    expect(reviews.textContent).toBe('Отзывы')
  })

  it('кладёт точное число в имя ссылки, а «Фото» — ещё и разбивку', async () => {
    serveSummary(COUNTS)

    renderRail()

    expect(await exactLink('Отзывы, на модерации: 12')).toHaveAttribute(
      'href',
      '/admin/reviews'
    )
    expect(
      await exactLink('Фото, на модерации: 5 (аватары: 2, портфолио: 3)')
    ).toHaveAttribute('href', '/admin/photos')
    expect(await exactLink('Барберы, на проверке: 4')).toBeInTheDocument()
    expect(
      await exactLink('Пользователи, без подтверждения email: 8')
    ).toBeInTheDocument()
    expect(await exactLink('Заявки, в очереди: 3')).toBeInTheDocument()
    expect(await exactLink('Каталог, салоны на проверке: 2')).toBeInTheDocument()
    // Точное число остаётся в имени, даже когда бейдж показывает «99+».
    expect(await exactLink('Брони, подтверждённые: 140')).toBeInTheDocument()
  })

  it('активный пункт сохраняет бейдж и aria-current', async () => {
    serveSummary(COUNTS)

    renderRail()

    const photos = await link('Фото')

    expect(photos).toHaveAttribute('aria-current', 'page')
    expect(badgeOf(photos)?.textContent).toBe('5')
  })

  it('не меняет href пунктов', async () => {
    serveSummary(COUNTS)

    renderRail()

    const hrefs = await waitFor(() =>
      within(rail())
        .getAllByRole('link')
        .map((item) => item.getAttribute('href'))
    )

    expect(hrefs).toStrictEqual([
      '/admin/summary',
      '/admin/statistics',
      '/admin/reviews',
      '/admin/photos',
      '/admin/barbers',
      '/admin/users',
      '/admin/barber-requests',
      '/admin/catalogue',
      '/admin/bookings',
      '/admin/audit',
    ])
  })

  it('пока снимка нет, бейджей нет и имя ссылки остаётся текстом пункта', async () => {
    // Первый ответ задерживаем: до него рейл уже отрисован.
    let release: () => void = () => {}
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })

    server.use(
      http.get(SUMMARY_URL, async () => {
        await gate

        return HttpResponse.json(COUNTS)
      })
    )

    renderRail()

    const reviews = within(rail()).getByRole('link', {
      name: labelPattern('Отзывы'),
    })

    expect(badgeOf(reviews)).toBeNull()
    expect(reviews.textContent).toBe('Отзывы')

    release()

    await waitFor(() => {
      expect(
        badgeOf(within(rail()).getByRole('link', { name: labelPattern('Отзывы') }))
      ).not.toBeNull()
    })
  })
})
