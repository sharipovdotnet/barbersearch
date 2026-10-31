import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { fetchAllMyReviews, restoreDeletedReviews } from '../fixtures/api'
import { MyReviewsPage } from '../pages/my-reviews-page'

/**
 * `/account/reviews` — мутации (§4.3).
 *
 * Отдельный аккаунт и отдельный файл: правка и удаление меняют счётчики строк,
 * поэтому читающие спеки работают с другим клиентом (§5).
 */
const EDITED_TEXT = 'Проверка правки E2E'

test.use({ storageState: storageStatePath('mutations') })

test.describe.configure({ mode: 'serial' })

test.describe('мои отзывы: правка и удаление', () => {
  test('правка: диалог, оценка, «Сохранить» — без нового GET списка', async ({ page }) => {
    const myReviews = new MyReviewsPage(page)

    await myReviews.goto()
    await myReviews.waitForFirstPage()

    const row = myReviews.rows.first()
    await myReviews.editButton(row).click()
    await expect(
      myReviews.editDialog.getByRole('heading', { name: 'Изменить отзыв' })
    ).toBeVisible()

    await myReviews.ratingButton(3).click()
    await myReviews.commentField.fill(EDITED_TEXT)

    const listRequests: string[] = []
    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/reviews/mine') {
        listRequests.push(request.url())
      }
    })

    await myReviews.saveButton.click()

    await expect(myReviews.editDialog).toBeHidden()
    await expect(myReviews.rows.first()).toContainText('3 / 5')
    await expect(myReviews.rows.first()).toContainText(EDITED_TEXT)

    expect(listRequests, 'правка обновляет строку локально').toHaveLength(0)
  })

  test('удаление: последняя строка непустой страницы возвращает на предыдущую', async ({
    page,
    request,
  }) => {
    const myReviews = new MyReviewsPage(page)

    await myReviews.goto()
    await myReviews.waitForFirstPage()

    // Удаление необратимо: запоминаем записи, чтобы вернуть число отзывов после теста.
    const before = await fetchAllMyReviews(request)

    try {
      await myReviews.pager.clickNext()
      await myReviews.pager.expectRange('11–12')
      await expect(myReviews.rows).toHaveCount(2)

      await myReviews.deleteButton(myReviews.rows.last()).click()
      await expect(myReviews.deleteDialog).toBeVisible()
      await myReviews.confirmDelete.click()
      await expect(myReviews.deleteDialog).toBeHidden()

      // Страница ещё не пуста — остаёмся на ней.
      await expect(myReviews.rows).toHaveCount(1)

      await myReviews.deleteButton(myReviews.rows.last()).click()
      await expect(myReviews.deleteDialog).toBeVisible()
      await myReviews.confirmDelete.click()
      await expect(myReviews.deleteDialog).toBeHidden()

      // Политика пустой страницы (#31): уходим на предыдущую, а не в пустое состояние.
      await expect(myReviews.rows).toHaveCount(10)
      // Курсоры сшиваются: список стал одностраничным, пагинатор исчезает (2.1).
      await expect(myReviews.pager.root).toHaveCount(0)
      await expect(page.getByText('Пока нет отзывов.')).toHaveCount(0)
      await expect(page.getByText('Загружаем отзывы…')).toHaveCount(0)
    } finally {
      await restoreDeletedReviews(request, before)
    }
  })
})
