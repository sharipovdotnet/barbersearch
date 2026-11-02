import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { AdminBookingsPage } from '../pages/admin-bookings-page'

/**
 * `/admin/bookings` — две существующие операции поддержки: отмена чужой брони
 * и отметка неявки.
 *
 * Проект `mutations-chromium` подхватывает файл по суффиксу `mutations.spec.ts`
 * и идёт после читающих проектов. Домен не умеет «отменить отмену», поэтому
 * спек меняет фикстуры необратимо: повторный прогон требует пересева
 * `barbersearch_e2e` (`npm run test:e2e:seed`).
 *
 * Сцены не делят строки: тесты одного файла Playwright запускает параллельно
 * (`fullyParallel`), поэтому у каждой своя бронь — NN 01 (Pending), NN 19
 * (Confirmed) и NN 20 (Confirmed, остаётся нетронутой). Иначе отмена и неявка
 * гонялись бы за один статус.
 */
test.use({ storageState: storageStatePath('admin') })

const BOOKINGS = fixtures.adminBookings

test.describe('админка: брони, действия', () => {
  test('отмена Pending: подтверждение, статус «Отменена» и 17 Pending в поиске', async ({
    page,
  }) => {
    const adminBookings = new AdminBookingsPage(page)

    await adminBookings.goto()
    await adminBookings.searchFor(BOOKINGS.cancelId)
    await adminBookings.waitForRows(1)

    await adminBookings.openBooking(BOOKINGS.cancelId)

    await expect(adminBookings.detailStatus('Ждёт подтверждения')).toBeVisible()

    await adminBookings.actionButton('Отменить бронь').click()

    const dialog = adminBookings.confirmDialog
    await expect(dialog).toBeVisible()
    // Текст подтверждения — копия: он называет последствие для клиента и барбера.
    await expect(dialog).toContainText(
      'Бронь будет отменена. Клиент и барбер увидят новый статус.'
    )

    await dialog.getByRole('button', { name: 'Отменить бронь' }).click()

    await expect(adminBookings.detail.getByRole('status')).toContainText(
      'Бронь отменена.'
    )
    await expect(adminBookings.detailStatus('Отменена')).toBeVisible()
    // Терминальный статус: кнопок действий больше нет.
    await expect(adminBookings.actionButton('Отменить бронь')).toHaveCount(0)
    await expect(adminBookings.actionButton('Отметить неявку')).toHaveCount(0)

    // Список перечитан после действия: строка в поиске по id уже «Отменена».
    await expect(adminBookings.rowOf(BOOKINGS.cancelId)).toContainText('Отменена')

    // Было 18 Pending, одну отменили — осталось 17; поиск по email сохранился.
    await adminBookings.searchFor(BOOKINGS.email)
    await adminBookings.selectStatus('Pending', BOOKINGS.email)

    await expect(adminBookings.rows).toHaveCount(BOOKINGS.pending - 1)
    await expect(adminBookings.searchField).toHaveValue(BOOKINGS.email)
  })

  test('неявка Confirmed: подтверждение и статус «Неявка»', async ({ page }) => {
    const adminBookings = new AdminBookingsPage(page)

    await adminBookings.goto()
    await adminBookings.searchFor(BOOKINGS.noShowId)
    await adminBookings.waitForRows(1)

    await adminBookings.openBooking(BOOKINGS.noShowId)

    await expect(adminBookings.detailStatus('Подтверждена')).toBeVisible()

    // Начало визита в прошлом (2026-09-12 UTC), поэтому действие доступно.
    await adminBookings.actionButton('Отметить неявку').click()

    const dialog = adminBookings.confirmDialog
    await expect(dialog).toBeVisible()
    await expect(dialog).toContainText(
      'Визит будет отмечен как неявка. Отменить это действие нельзя.'
    )

    await dialog.getByRole('button', { name: 'Отметить неявку' }).click()

    await expect(adminBookings.detail.getByRole('status')).toContainText(
      'Неявка отмечена.'
    )
    await expect(adminBookings.detailStatus('Неявка')).toBeVisible()
    await expect(adminBookings.actionButton('Отменить бронь')).toHaveCount(0)
    await expect(adminBookings.actionButton('Отметить неявку')).toHaveCount(0)
  })

  test('«Оставить как есть» закрывает диалог и не меняет статус', async ({
    page,
  }) => {
    const adminBookings = new AdminBookingsPage(page)
    const mutations: string[] = []

    page.on('request', (request) => {
      const url = new URL(request.url())

      if (
        request.method() === 'POST' &&
        (url.pathname.endsWith('/cancellation') ||
          url.pathname.endsWith('/no-show'))
      ) {
        mutations.push(`${request.method()} ${url.pathname}`)
      }
    })

    await adminBookings.goto()
    await adminBookings.searchFor(BOOKINGS.untouchedId)
    await adminBookings.waitForRows(1)

    await adminBookings.openBooking(BOOKINGS.untouchedId)

    await expect(adminBookings.detailStatus('Подтверждена')).toBeVisible()

    await adminBookings.actionButton('Отменить бронь').click()

    const dialog = adminBookings.confirmDialog
    await expect(dialog).toBeVisible()

    await dialog.getByRole('button', { name: 'Оставить как есть' }).click()

    await expect(dialog).toHaveCount(0)
    // Статус и действие на месте, запроса на мутацию не было.
    await expect(adminBookings.detailStatus('Подтверждена')).toBeVisible()
    await expect(adminBookings.actionButton('Отменить бронь')).toBeVisible()

    expect(mutations, 'закрытие диалога не отправляет мутацию').toStrictEqual([])
  })
})
