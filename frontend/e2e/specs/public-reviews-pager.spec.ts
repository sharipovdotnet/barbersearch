import { expect, test, type Page } from '../fixtures/test'

import { fixtures } from '../fixtures/accounts'
import { ProfilePage } from '../pages/profile-page'

/**
 * Главный сценарий этапа: пагинация публичных отзывов (§4.1).
 *
 * Проверяем то, чего не поймать юнит-тестами: настоящий роутер, сеть, кэш страниц,
 * фокус и живые регионы. Числа берутся из фикстур: 12 опубликованных отзывов.
 */
const FIRST_PAGE = '1–10 из 12'
const LAST_PAGE = '11–12 из 12'

/** Запросы списка отзывов: их число и курсор — суть проверки. */
function trackReviewRequests(page: Page): string[] {
  const urls: string[] = []

  page.on('request', (request) => {
    const url = request.url()

    if (new URL(url).pathname === '/api/reviews') {
      urls.push(url)
    }
  })

  return urls
}

test.describe('публичные отзывы: пагинация', () => {
  test('первая страница: 10 карточек, диапазон и недоступное «Назад»', async ({ page }) => {
    const profile = new ProfilePage(page)
    await profile.goto()
    await profile.waitForFirstPage()

    await profile.pager.expectRange(FIRST_PAGE)
    await expect(profile.pager.previous).toBeDisabled()
    await expect(profile.pager.next).toBeEnabled()
  })

  test('«Дальше»: один запрос с курсором, фокус на списке, «Назад» без запросов', async ({
    page,
  }) => {
    const profile = new ProfilePage(page)
    const requests = trackReviewRequests(page)

    await profile.goto()
    await profile.waitForFirstPage()
    const firstPageTexts = await profile.cardTexts()

    const before = requests.length
    await profile.pager.clickNext()

    // Диапазон и карточки второй страницы: 12 отзывов дают неполную страницу.
    await profile.pager.expectRange(LAST_PAGE)
    await expect(profile.reviewCards).toHaveCount(2)

    const newRequests = requests.slice(before)
    expect(newRequests, 'один новый GET /api/reviews на «Дальше»').toHaveLength(1)
    expect(newRequests[0]).toContain('cursor=')

    // Фокус уходит на список, а заголовок секции остаётся в зоне видимости.
    await expect(profile.reviewsList).toBeFocused()
    await expect(profile.heading).toBeInViewport()

    // «Назад» идёт из кэша страниц: запросов быть не должно.
    const beforeBack = requests.length
    await profile.pager.clickPrevious()
    await profile.pager.expectRange(FIRST_PAGE)
    await expect(profile.reviewCards).toHaveCount(10)

    expect(requests.length - beforeBack, '«Назад» не ходит в сеть').toBe(0)
    expect(await profile.cardTexts()).toEqual(firstPageTexts)
  })

  test('последняя страница неполная и «Дальше» недоступно', async ({ page }) => {
    const profile = new ProfilePage(page)
    await profile.goto()
    await profile.waitForFirstPage()

    await profile.pager.clickNext()
    await profile.pager.expectRange(LAST_PAGE)

    await expect(profile.pager.next).toBeDisabled()
    await expect(profile.pager.previous).toBeEnabled()
  })

  test('ошибка следующей страницы: карточки остаются, повтор грузит страницу', async ({
    page,
  }) => {
    const profile = new ProfilePage(page)
    await profile.goto()
    await profile.waitForFirstPage()

    // Инъекция ошибки в сеть: успешные ответы не мокаем (§5).
    await page.route(
      (url) => url.pathname === '/api/reviews' && url.searchParams.has('cursor'),
      (route) => route.abort()
    )

    await profile.pager.clickNext()

    await expect(profile.pager.alert).toBeVisible()
    await expect(profile.reviewCards, 'страница не пропала').toHaveCount(10)

    // Кнопка «Дальше» становится повтором с другим доступным именем.
    await expect(profile.pager.retry).toBeEnabled()
    await expect(profile.pager.next).toHaveCount(0)

    await page.unrouteAll()

    await profile.pager.retry.click()
    await profile.pager.expectRange(LAST_PAGE)
    await expect(profile.reviewCards).toHaveCount(2)
    await expect(profile.pager.alert).toHaveCount(0)
  })

  test('живой регион объявляет диапазон', async ({ page }) => {
    const profile = new ProfilePage(page)
    await profile.goto()
    await profile.waitForFirstPage()

    await expect(profile.pager.announcement).toHaveText(`Страницы отзывов: ${FIRST_PAGE}`)
    await expect(profile.pager.announcement).toHaveCount(1)

    await profile.pager.clickNext()
    await expect(profile.pager.announcement).toHaveText(`Страницы отзывов: ${LAST_PAGE}`)
  })

  test('отзывы приходят только для витринного барбера', async ({ page }) => {
    const profile = new ProfilePage(page)
    await profile.goto()
    await profile.waitForFirstPage()

    // Сводка профиля берётся из DTO, а не из текущей страницы.
    await expect(profile.summary).toContainText(String(fixtures.publicReviews.total))
  })
})
